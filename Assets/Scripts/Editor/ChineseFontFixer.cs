using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 修复"中文字全部显示成方框（tofu）"。
    ///
    /// 病因（本次实测）：
    ///   ① TMP Settings 的默认字体、主菜单/关卡选择里 16+ 处引用，都指向一个已经不存在的
    ///      中文字体资产（guid 4ce8f07a…）→ 引用悬空，TMP 退回内置的 LiberationSans，
    ///      而 LiberationSans 里一个汉字都没有，于是全渲染成方框。
    ///   ② GamePlay / 第一关 用的是 Assets/Fonts/msyh SDF.asset，但它的字符表是空的
    ///      （m_glyphInfoList: []、没有任何 m_Unicode 条目），同样渲染不出字。
    ///   ③ TMP Settings 的 m_fallbackFontAssets 是空数组，没有兜底字体可用。
    ///
    /// 治法（本脚本一键完成，全程先备份）：
    ///   ① 用 Assets/Fonts/NotoSansSC-Regular.ttf（思源黑体，开源可商用）现场生成
    ///      Assets/Fonts/NotoSansSC SDF.asset：Dynamic + 2048 图集 + SDFAA。
    ///   ② 扫描 Assets/Scenes 下所有 TMP 组件里的 m_text，把它们实际用到的字符全部预烘进图集，
    ///      界面上出现的字 100% 覆盖（Dynamic 只作为兜底）。
    ///   ③ 就地给 msyh SDF 补上同样的字符（保留它自己的 guid，GamePlay/第一关 的引用不动）。
    ///   ④ 互相挂 fallback、更新 TMP Settings 默认字体与兜底列表。
    ///   ⑤ 把场景里那条悬空的旧 guid 接管到新资产（改前先备份场景文件）。
    ///
    /// 菜单：Tools/字体修复/③ 修复中文方框（推荐）
    /// </summary>
    public static class ChineseFontFixer
    {
        private const string SourceFontPath = "Assets/Fonts/NotoSansSC-Regular.ttf";
        private const string NewAssetPath = "Assets/Fonts/NotoSansSC SDF.asset";
        private const string MsyhAssetPath = "Assets/Fonts/msyh SDF.asset";
        private const string LiberationAssetPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string ScenesFolder = "Assets/Scenes";
        private const string BackupRoot = "SceneBackup_手动/FontFixBackup";

        /// <summary>场景里已经失效、需要接管过来的旧字体 guid（原 NotoSansSC SDF，文件已被删）。</summary>
        private static readonly string[] GuidsToTakeOver = { "4ce8f07ad5f639d48afefbac59ef6afa" };

        private const int SamplingPointSize = 90;
        private const int AtlasPadding = 9;
        private const int AtlasSize = 2048;

        /// <summary>
        /// 保底字符集：ASCII 可见字符 + 中英文标点。
        /// 界面上真正的汉字由"扫描场景 m_text"得到，这里只保证符号和字母一定有。
        /// </summary>
        private const string BaseCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "，。、；：？！“”‘’（）《》【】—…·＋－×÷＝％℃￥＃＠" +
            "关卡选择开始游戏返回继续暂停重新设置退出主菜单联机创建加入房间码简介说明" +
            "操作移动跳跃射击血量时间得分胜利失败重来第新邻驾到玩家房主客户端连接断开" +
            "等待正在加载请稍候确认取消保存删除编辑帮助关于音量全屏分辨率语言速度护盾冷却道具";

        [MenuItem("Tools/字体修复/③ 修复中文方框（推荐）", false, 40)]
        public static void Fix()
        {
            var log = new StringBuilder();
            log.AppendLine("[字体修复] ===== 开始修复中文方框 =====");

            // ---------- ① 源字体 ----------
            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                Debug.LogError($"[字体修复] 找不到源字体：{SourceFontPath}\n" +
                               "请把思源黑体（NotoSansSC-Regular.ttf）放回 Assets/Fonts/ 再执行。");
                return;
            }

            log.AppendLine($"源字体：{SourceFontPath}（{sourceFont.name}）");

            // ---------- ② 收集要烘的字符 ----------
            HashSet<char> charSet = CollectCharactersFromScenes();
            int sceneCharCount = charSet.Count;
            foreach (char c in BaseCharacters)
            {
                charSet.Add(c);
            }

            char[] all = new char[charSet.Count];
            charSet.CopyTo(all);
            Array.Sort(all);
            string bakeText = new string(all);

            log.AppendLine($"需要烘焙的字符：场景里扫描到 {sceneCharCount} 个，加上保底字符共 {bakeText.Length} 个。");

            // ---------- ③ 生成 / 重建 NotoSansSC SDF ----------
            TMP_FontAsset noto = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NewAssetPath);

            if (noto == null)
            {
                // 关掉多图集：图集万一装不下，TMP 只会返回"缺字"而不会陷进
                // TryAddGlyphsToNewAtlasTexture 的打包循环把编辑器卡死。
                noto = TMP_FontAsset.CreateFontAsset(
                    sourceFont,
                    SamplingPointSize,
                    AtlasPadding,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                    AtlasSize,
                    AtlasSize,
                    AtlasPopulationMode.Dynamic,
                    false);

                if (noto == null)
                {
                    Debug.LogError("[字体修复] 创建字体资产失败。通常是 TTF 的导入设置里没勾 " +
                                   "“Include Font Data”：选中 Assets/Fonts/NotoSansSC-Regular.ttf，" +
                                   "在 Inspector 里勾上并 Apply，然后重试。");
                    return;
                }

                // CreateFontAsset 只建了内存对象，材质和图集纹理必须显式挂成子资产，否则保存后丢失。
                noto.name = "NotoSansSC SDF";
                noto.material.name = "NotoSansSC SDF Material";
                noto.atlasTextures[0].name = "NotoSansSC SDF Atlas";

                AssetDatabase.CreateAsset(noto, NewAssetPath);
                AssetDatabase.AddObjectToAsset(noto.material, noto);
                AssetDatabase.AddObjectToAsset(noto.atlasTextures[0], noto);
                AssetDatabase.SaveAssets();

                log.AppendLine($"已新建字体资产：{NewAssetPath}（{AtlasSize}x{AtlasSize}，Dynamic，SDFAA）");
            }
            else
            {
                log.AppendLine($"字体资产已存在，就地修复：{NewAssetPath}");
            }

            EnsureDynamicSettings(noto, sourceFont);

            // ---------- ④ 预烘 ----------
            int baked = Bake(noto, bakeText, log);

            // ---------- ⑤ 就地修 msyh SDF（保留 guid） ----------
            var msyh = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MsyhAssetPath);
            if (msyh != null)
            {
                EnsureDynamicSettings(msyh, msyh.sourceFontFile);
                Bake(msyh, bakeText, log, "msyh SDF");
            }
            else
            {
                log.AppendLine($"未找到 {MsyhAssetPath}，跳过（GamePlay/第一关 会改用新字体）。");
            }

            // ---------- ⑥ fallback 互相兜底（单向，不成环） ----------
            var liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationAssetPath);

            noto.fallbackFontAssetTable.Clear();
            if (msyh != null)
            {
                noto.fallbackFontAssetTable.Add(msyh);
            }

            if (liberation != null)
            {
                noto.fallbackFontAssetTable.Add(liberation);
            }

            EditorUtility.SetDirty(noto);

            if (msyh != null)
            {
                msyh.fallbackFontAssetTable.Clear();
                msyh.fallbackFontAssetTable.Add(noto);
                if (liberation != null)
                {
                    msyh.fallbackFontAssetTable.Add(liberation);
                }

                EditorUtility.SetDirty(msyh);
            }

            // ---------- ⑦ TMP Settings ----------
            UpdateTmpSettings(noto, msyh, liberation, log);

            // ---------- ⑧ 接管场景里悬空的旧引用 ----------
            string newGuid = AssetDatabase.AssetPathToGUID(NewAssetPath);
            if (!string.IsNullOrEmpty(newGuid))
            {
                TakeOverSceneReferences(newGuid, log);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.AppendLine($"[字体修复] ===== 完成（烘焙成功 {baked} 字符）=====");
            log.AppendLine("请保存场景并重启 Unity，然后重新打开场景查看效果。");
            Debug.Log(log.ToString());
        }

        // ------------------------------------------------------------------
        // 内部实现
        // ------------------------------------------------------------------

        /// <summary>扫描所有场景文件，收集 TMP / UGUI 文本组件里实际用到的字符。</summary>
        private static HashSet<char> CollectCharactersFromScenes()
        {
            var result = new HashSet<char>();
            if (!Directory.Exists(ScenesFolder))
            {
                return result;
            }

            var regex = new Regex(@"^\s*m_text:\s*(.*)$");

            foreach (string scene in Directory.GetFiles(ScenesFolder, "*.unity", SearchOption.AllDirectories))
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(scene, Encoding.UTF8);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[字体修复] 读取场景失败，已跳过：{scene}（{e.Message}）");
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
                        // 跳过 YAML 结构字符，其余（含汉字）全部收进来
                        if (c != '\r' && c != '\n')
                        {
                            result.Add(c);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>把图集参数校正为"Dynamic + 单图集 + 打包不清空"。</summary>
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

        /// <summary>分批把字符烘进图集，返回成功数量。</summary>
        private static int Bake(TMP_FontAsset font, string text, StringBuilder log, string label = "NotoSansSC SDF")
        {
            int total = 0;
            const int batchSize = 256;

            for (int i = 0; i < text.Length; i += batchSize)
            {
                int len = Mathf.Min(batchSize, text.Length - i);
                string batch = text.Substring(i, len);

                try
                {
                    font.TryAddCharacters(batch, out string missing);
                    total += string.IsNullOrEmpty(missing) ? batch.Length : batch.Length - missing.Length;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[字体修复] {label} 烘焙第 {i / batchSize + 1} 批时出错：{e.Message}");
                }
            }

            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();

            log.AppendLine($"{label}：成功烘焙 {total} / {text.Length} 字符。");
            return total;
        }

        private static void UpdateTmpSettings(TMP_FontAsset noto, TMP_FontAsset msyh,
                                              TMP_FontAsset liberation, StringBuilder log)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null)
            {
                Debug.LogWarning($"[字体修复] 找不到 TMP Settings：{TmpSettingsPath}");
                return;
            }

            var so = new SerializedObject(settings);

            var defaultProp = so.FindProperty("m_defaultFontAsset");
            if (defaultProp != null)
            {
                defaultProp.objectReferenceValue = noto;
            }

            var fallbackProp = so.FindProperty("m_fallbackFontAssets");
            if (fallbackProp != null)
            {
                fallbackProp.arraySize = 0;
                int index = 0;

                if (msyh != null)
                {
                    fallbackProp.InsertArrayElementAtIndex(index);
                    fallbackProp.GetArrayElementAtIndex(index).objectReferenceValue = msyh;
                    index++;
                }

                if (liberation != null)
                {
                    fallbackProp.InsertArrayElementAtIndex(index);
                    fallbackProp.GetArrayElementAtIndex(index).objectReferenceValue = liberation;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            log.AppendLine("TMP Settings：默认字体已切到 NotoSansSC SDF，并补上兜底字体列表。");
        }

        /// <summary>把场景 YAML 里失效的旧字体 guid 换成新资产的 guid（改前先整份备份）。</summary>
        private static void TakeOverSceneReferences(string newGuid, StringBuilder log)
        {
            if (!Directory.Exists(ScenesFolder))
            {
                return;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupDir = Path.Combine(BackupRoot, stamp);

            foreach (string scene in Directory.GetFiles(ScenesFolder, "*.unity", SearchOption.AllDirectories))
            {
                string content;
                try
                {
                    content = File.ReadAllText(scene, Encoding.UTF8);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[字体修复] 读取场景失败，已跳过：{scene}（{e.Message}）");
                    continue;
                }

                string before = content;
                foreach (string oldGuid in GuidsToTakeOver)
                {
                    content = content.Replace("guid: " + oldGuid, "guid: " + newGuid);
                }

                if (content == before)
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(backupDir);
                    File.Copy(scene, Path.Combine(backupDir, Path.GetFileName(scene)), true);
                    File.WriteAllText(scene, content, new UTF8Encoding(false));
                    log.AppendLine($"场景已更新（原文件备份到 {backupDir}）：{Path.GetFileName(scene)}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[字体修复] 写回场景失败：{scene}（{e.Message}）");
                }
            }
        }

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
