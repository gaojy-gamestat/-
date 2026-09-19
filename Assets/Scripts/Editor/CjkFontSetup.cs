using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 用思源黑体（Noto Sans SC，SIL OFL 1.1，免费可商用、可随游戏发布）
    /// 生成 TextMeshPro 字体资产，并应用到项目里所有文字。
    ///
    /// 做四件事：
    ///   ① 用 Assets/Fonts/NotoSansSC-Regular.ttf 生成 Assets/Fonts/NotoSansSC SDF.asset
    ///      （2048 图集 + SDFAA + Dynamic，缺字会自动补，不会卡死）
    ///   ② 扫描所有场景/预制体里真正出现的文字，预烘进图集
    ///   ③ 把 TMP Settings 的默认字体换成思源黑体，LiberationSans 留作兜底
    ///   ④ 场景/预制体里所有 m_fontAsset（TMP）和 m_Font（旧版 UI Text）
    ///      的引用统一改成思源黑体（改前整份备份）
    ///
    /// 菜单：Tools/字体修复/⓪ 安装思源黑体并应用到全项目（推荐）
    /// 首次编译后若字体资产还没建，会自动跑一次（EditorPrefs 记录，只跑一次）。
    /// </summary>
    public static class CjkFontSetup
    {
        private const string SourceFontPath = "Assets/Fonts/NotoSansSC-Regular.ttf";
        private const string FontAssetPath = "Assets/Fonts/NotoSansSC SDF.asset";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string LiberationAssetPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string BackupRoot = "SceneBackup_手动/FontFixBackup";
        private const string AutoRunKey = "GameNet.CjkFontSetup.AutoRun.v1";

        private const int SamplingPointSize = 90;
        private const int AtlasPadding = 9;
        private const int AtlasSize = 2048;

        /// <summary>保底字符集：ASCII 可见字符 + 中英文标点 + 界面常用汉字。</summary>
        private const string BaseCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "，。、；：？！“”‘’（）《》【】—…·＋－×÷＝％℃￥＃＠" +
            "关卡选择开始游戏返回继续暂停重新设置退出主菜单联机创建加入房间码简介说明" +
            "操作移动跳跃射击血量时间得分胜利失败重来第新邻驾到玩家房主客户端连接断开" +
            "等待正在加载请稍候确认取消保存删除编辑帮助关于音量全屏分辨率语言速度护盾冷却道具" +
            "制作人员美术程序策划音效测试感谢游玩退出确定应用恢复默认音乐" +
            "一二三四五六七八九十零百分秒分钟小时任务进度怒气房东邻居装修";

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        [MenuItem("Tools/字体修复/⓪ 安装思源黑体并应用到全项目（推荐）", false, 20)]
        public static void ApplyMenu()
        {
            Apply();
        }

        [MenuItem("Tools/字体修复/检查当前文字都用的什么字体", false, 21)]
        public static void ReportMenu()
        {
            Report();
        }

        [InitializeOnLoadMethod]
        private static void AutoRunOnce()
        {
            if (EditorPrefs.GetBool(AutoRunKey, false))
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoRunOnce;
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(AutoRunKey, false))
                {
                    return;
                }

                // 播放模式下不能改磁盘资产，等退出播放模式后再来一次
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                    return;
                }

                if (!File.Exists(SourceFontPath))
                {
                    Debug.LogWarning($"[字体] 没找到源字体 {SourceFontPath}，跳过自动安装。");
                    return;
                }

                // 光有字体资产还不够：TMP Settings 也必须指向它，否则新建文字仍是方框
                if (File.Exists(FontAssetPath) && TmpSettingsUsesCjk())
                {
                    EditorPrefs.SetBool(AutoRunKey, true);
                    return;
                }

                Debug.Log("[字体] 检测到思源黑体尚未完全接管，自动安装一次……");
                Apply();
            };
        }

        /// <summary>
        /// TMP Settings 的默认字体是否已经指向思源黑体。
        /// 直接读文件文本，不依赖资产对象——播放模式下的内存改动可能根本没落盘。
        /// </summary>
        private static bool TmpSettingsUsesCjk()
        {
            if (!File.Exists(TmpSettingsPath))
            {
                return false;
            }

            string sdfGuid = AssetDatabase.AssetPathToGUID(FontAssetPath);
            if (string.IsNullOrEmpty(sdfGuid))
            {
                return false;
            }

            var m = Regex.Match(
                File.ReadAllText(TmpSettingsPath),
                @"m_defaultFontAsset:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-fA-F]{32})");

            return m.Success && m.Groups[1].Value == sdfGuid;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.delayCall += AutoRunOnce;
        }

        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(
                    "[字体] 当前处于播放模式，此时不能修改字体资产。\n" +
                    "请先退出播放模式，再执行：Tools → 字体修复 → ⓪ 安装思源黑体并应用到全项目（推荐）");
                return;
            }

            var log = new StringBuilder();
            log.AppendLine("[字体] ===== 安装思源黑体并应用到全项目 =====");

            Font sourceFont = EnsureSourceFont(log);
            if (sourceFont == null)
            {
                Debug.LogError(log.ToString());
                return;
            }

            TMP_FontAsset cjk = GetOrCreateFontAsset(sourceFont, log);
            if (cjk == null)
            {
                Debug.LogError(log.ToString());
                return;
            }

            Bake(cjk, CollectCharacters(), log);
            UpdateTmpSettings(cjk, log);

            try
            {
                string sdfGuid = AssetDatabase.AssetPathToGUID(FontAssetPath);
                string ttfGuid = AssetDatabase.AssetPathToGUID(SourceFontPath);
                ReplaceFontReferences(sdfGuid, ttfGuid, log);
                ReloadOpenScenes(log);
            }
            finally
            {
                // 无论中途是否出错，已改动的资产都必须落盘
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            EditorPrefs.SetBool(AutoRunKey, true);

            log.AppendLine("[字体] ===== 完成 =====");
            log.AppendLine("若界面上仍有方框：保存场景后重启 Unity 再打开场景。");
            Debug.Log(log.ToString());
        }

        // ------------------------------------------------------------------
        // ① 源字体
        // ------------------------------------------------------------------

        private static Font EnsureSourceFont(StringBuilder log)
        {
            if (!File.Exists(SourceFontPath))
            {
                log.AppendLine($"找不到源字体：{SourceFontPath}");
                log.AppendLine("请把 NotoSansSC-Regular.ttf 放回 Assets/Fonts/ 再执行。");
                return null;
            }

            AssetDatabase.ImportAsset(SourceFontPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(SourceFontPath);
            if (importer != null)
            {
                var importerSo = new SerializedObject(importer);
                var includeProp = importerSo.FindProperty("m_IncludeFontData");
                if (includeProp != null && !includeProp.boolValue)
                {
                    includeProp.boolValue = true;
                    importerSo.ApplyModifiedPropertiesWithoutUndo();
                    importer.SaveAndReimport();
                    log.AppendLine("已开启源字体的 Include Font Data（Dynamic 补字形必需）。");
                }
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            log.AppendLine($"源字体：{SourceFontPath}（{(font != null ? font.name : "加载失败")}）");
            return font;
        }

        // ------------------------------------------------------------------
        // ② 生成 / 复用字体资产
        // ------------------------------------------------------------------

        private static TMP_FontAsset GetOrCreateFontAsset(Font sourceFont, StringBuilder log)
        {
            var cjk = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (cjk != null)
            {
                log.AppendLine($"已存在字体资产，就地复用：{FontAssetPath}");
                EnsureDynamicSettings(cjk, sourceFont);
                return cjk;
            }

            cjk = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                SamplingPointSize,
                AtlasPadding,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic,
                false);

            if (cjk == null)
            {
                log.AppendLine("创建字体资产失败：通常是 TTF 没勾 Include Font Data。");
                return null;
            }

            cjk.name = "NotoSansSC SDF";
            cjk.material.name = "NotoSansSC SDF Material";
            cjk.atlasTextures[0].name = "NotoSansSC SDF Atlas";

            AssetDatabase.CreateAsset(cjk, FontAssetPath);
            AssetDatabase.AddObjectToAsset(cjk.material, cjk);
            AssetDatabase.AddObjectToAsset(cjk.atlasTextures[0], cjk);
            AssetDatabase.SaveAssets();

            log.AppendLine($"已新建字体资产：{FontAssetPath}（{AtlasSize}x{AtlasSize}，Dynamic，SDFAA）");
            return cjk;
        }

        private static void EnsureDynamicSettings(TMP_FontAsset font, Font sourceFont)
        {
            var so = new SerializedObject(font);
            bool dirty = false;

            dirty |= SetInt(so, "m_AtlasPopulationMode", (int)AtlasPopulationMode.Dynamic);
            dirty |= SetInt(so, "m_AtlasWidth", AtlasSize);
            dirty |= SetInt(so, "m_AtlasHeight", AtlasSize);
            dirty |= SetInt(so, "m_AtlasPadding", AtlasPadding);
            dirty |= SetBool(so, "m_IsMultiAtlasTexturesEnabled", false);
            dirty |= SetBool(so, "m_ClearDynamicDataOnBuild", false);

            if (sourceFont != null)
            {
                var srcProp = so.FindProperty("m_SourceFontFile");
                if (srcProp != null && srcProp.objectReferenceValue == null)
                {
                    srcProp.objectReferenceValue = sourceFont;
                    dirty = true;
                }
            }

            if (dirty)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(font);
        }

        // ------------------------------------------------------------------
        // ③ 预烘字符
        // ------------------------------------------------------------------

        private static string CollectCharacters()
        {
            var set = new HashSet<char>();
            foreach (char c in BaseCharacters)
            {
                set.Add(c);
            }

            var regex = new Regex(@"^\s*m_text:\s*(.*)$");
            foreach (string file in EnumerateTargets())
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(file, Encoding.UTF8);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[字体] 读取失败已跳过：{file}（{e.Message}）");
                    continue;
                }

                foreach (string line in lines)
                {
                    Match m = regex.Match(line);
                    if (!m.Success)
                    {
                        continue;
                    }

                    foreach (char c in m.Groups[1].Value)
                    {
                        if (c != '\r' && c != '\n')
                        {
                            set.Add(c);
                        }
                    }
                }
            }

            char[] all = new char[set.Count];
            set.CopyTo(all);
            Array.Sort(all);
            return new string(all);
        }

        private static void Bake(TMP_FontAsset font, string text, StringBuilder log)
        {
            int total = 0;
            const int batchSize = 256;

            for (int i = 0; i < text.Length; i += batchSize)
            {
                string batch = text.Substring(i, Mathf.Min(batchSize, text.Length - i));
                try
                {
                    font.TryAddCharacters(batch, out string missing);
                    total += string.IsNullOrEmpty(missing) ? batch.Length : batch.Length - missing.Length;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[字体] 烘焙第 {i / batchSize + 1} 批出错：{e.Message}");
                }
            }

            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            log.AppendLine($"烘焙字符：成功 {total} / {text.Length}。");
        }

        // ------------------------------------------------------------------
        // ④ TMP Settings
        // ------------------------------------------------------------------

        private static void UpdateTmpSettings(TMP_FontAsset cjk, StringBuilder log)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null)
            {
                log.AppendLine($"找不到 TMP Settings：{TmpSettingsPath}");
                return;
            }

            var so = new SerializedObject(settings);

            var defaultProp = so.FindProperty("m_defaultFontAsset");
            if (defaultProp != null)
            {
                defaultProp.objectReferenceValue = cjk;
            }

            var fallbackProp = so.FindProperty("m_fallbackFontAssets");
            if (fallbackProp != null)
            {
                fallbackProp.arraySize = 0;
                var liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationAssetPath);
                if (liberation != null && liberation != cjk)
                {
                    fallbackProp.InsertArrayElementAtIndex(0);
                    fallbackProp.GetArrayElementAtIndex(0).objectReferenceValue = liberation;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            log.AppendLine("TMP Settings：默认字体 = 思源黑体，LiberationSans 作兜底。");
        }

        // ------------------------------------------------------------------
        // ⑤ 场景 / 预制体里的字体引用
        // ------------------------------------------------------------------

        private static IEnumerable<string> EnumerateTargets()
        {
            foreach (string f in Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories))
            {
                yield return f;
            }

            foreach (string f in Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories))
            {
                yield return f;
            }
        }

        private static void ReplaceFontReferences(string sdfGuid, string ttfGuid, StringBuilder log)
        {
            if (string.IsNullOrEmpty(sdfGuid))
            {
                log.AppendLine("拿不到字体资产的 GUID，跳过引用替换。");
                return;
            }

            var tmpPattern = new Regex(@"m_fontAsset: \{fileID: 11400000, guid: [0-9a-fA-F]{32}");
            var legacyPattern = new Regex(@"m_Font: \{fileID: \d+, guid: [0-9a-fA-F]{32}");

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupDir = Path.Combine(BackupRoot, stamp);

            int tmpCount = 0;
            int legacyCount = 0;
            int fileCount = 0;

            foreach (string file in EnumerateTargets())
            {
                string content;
                try
                {
                    content = File.ReadAllText(file, Encoding.UTF8);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[字体] 读取失败已跳过：{file}（{e.Message}）");
                    continue;
                }

                string before = content;

                content = tmpPattern.Replace(content, m =>
                {
                    tmpCount++;
                    return "m_fontAsset: {fileID: 11400000, guid: " + sdfGuid;
                });

                if (!string.IsNullOrEmpty(ttfGuid))
                {
                    content = legacyPattern.Replace(content, m =>
                    {
                        legacyCount++;
                        return "m_Font: {fileID: 12800000, guid: " + ttfGuid;
                    });
                }

                if (content == before)
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(backupDir);
                    File.Copy(file, Path.Combine(backupDir, Path.GetFileName(file)), true);
                    File.WriteAllText(file, content, new UTF8Encoding(false));
                    fileCount++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[字体] 写回失败：{file}（{e.Message}）");
                }
            }

            log.AppendLine($"改写引用：TMP {tmpCount} 处 / 旧版 UI Text {legacyCount} 处，共 {fileCount} 个文件。");
            if (fileCount > 0)
            {
                log.AppendLine($"原文件已备份到：{backupDir}");
            }
        }

        /// <summary>场景文件是在磁盘上改的，内存里还开着的话要重新载入才生效。</summary>
        private static void ReloadOpenScenes(StringBuilder log)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                log.AppendLine("播放模式下不自动重载场景；退出播放模式后重新打开场景即可看到新字体。");
                return;
            }

            var paths = new List<string>();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var sc = EditorSceneManager.GetSceneAt(i);
                if (sc.isLoaded && !string.IsNullOrEmpty(sc.path))
                {
                    paths.Add(sc.path);
                }
            }

            foreach (string path in paths)
            {
                var sc = EditorSceneManager.GetSceneByPath(path);
                if (sc.isDirty)
                {
                    log.AppendLine($"注意：{path} 有未保存的修改，未自动重载，请手动保存后重新打开该场景。");
                    continue;
                }

                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }

            if (paths.Count > 0)
            {
                log.AppendLine($"已重新载入 {paths.Count} 个打开的场景。");
            }
        }

        // ------------------------------------------------------------------
        // 体检报告
        // ------------------------------------------------------------------

        public static void Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[字体] 当前字体引用统计：");

            var tmp = new Regex(@"m_fontAsset: \{fileID: 11400000, guid: ([0-9a-fA-F]{32})");
            var legacy = new Regex(@"m_Font: \{fileID: \d+, guid: ([0-9a-fA-F]{32})");
            var tmpStats = new Dictionary<string, int>();
            var legacyStats = new Dictionary<string, int>();

            foreach (string file in EnumerateTargets())
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(file, Encoding.UTF8);
                }
                catch
                {
                    continue;
                }

                foreach (string line in lines)
                {
                    Count(tmp, line, tmpStats);
                    Count(legacy, line, legacyStats);
                }
            }

            foreach (var kv in tmpStats)
            {
                string path = AssetDatabase.GUIDToAssetPath(kv.Key);
                sb.AppendLine($"  TMP   {kv.Value,4} 处  {kv.Key}  {(string.IsNullOrEmpty(path) ? "【引用已丢失】" : path)}");
            }

            foreach (var kv in legacyStats)
            {
                string path = AssetDatabase.GUIDToAssetPath(kv.Key);
                sb.AppendLine($"  Text  {kv.Value,4} 处  {kv.Key}  {(string.IsNullOrEmpty(path) ? "【内置字体】" : path)}");
            }

            Debug.Log(sb.ToString());
        }

        private static void Count(Regex regex, string line, Dictionary<string, int> stats)
        {
            Match m = regex.Match(line);
            if (!m.Success)
            {
                return;
            }

            string guid = m.Groups[1].Value;
            stats.TryGetValue(guid, out int n);
            stats[guid] = n + 1;
        }

        // ------------------------------------------------------------------

        private static bool SetInt(SerializedObject so, string path, int value)
        {
            var prop = so.FindProperty(path);
            if (prop == null || prop.intValue == value)
            {
                return false;
            }

            prop.intValue = value;
            return true;
        }

        private static bool SetBool(SerializedObject so, string path, bool value)
        {
            var prop = so.FindProperty(path);
            if (prop == null || prop.boolValue == value)
            {
                return false;
            }

            prop.boolValue = value;
            return true;
        }
    }
}
