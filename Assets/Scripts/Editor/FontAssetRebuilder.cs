using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 修复 TMP 中文字体资产（NotoSansSC SDF）。
    ///
    /// 病灶：上一次预烘焙把资产写坏了 —— 字形表 / 自由矩形区按 1024x1024 图集记录，
    /// 但真正序列化进资产的图集纹理只有 1x1。
    /// 后果：① 所有文字（连 ASCII 字母）都渲染不出来；
    ///       ② TMP 在编辑器里补字形时，图集尺寸与打包记录对不上，会陷进打包循环，把 Unity 主线程卡死。
    ///
    /// 治法：就地清理重建（不删文件、不换 GUID，场景里的 TMP 引用不会断）：
    ///   ① 图集生成模式改回 Dynamic（缺字才会自动补）
    ///   ② 补回源字体引用（Assets/Fonts/NotoSansSC-Regular.ttf）
    ///   ③ ClearFontAssetData 清掉与图集纹理不一致的字形 / 矩形记录
    ///   ④ 预烘界面实际会用到的字符
    ///   ⑤ 材质与图集纹理重新登记为子资产并保存
    ///
    /// 脚本编译完成后若检测到资产仍然损坏，会自动执行一次（EditorPrefs 记住，只跑一次）。
    /// 也可以手动执行菜单：Tools/字体修复/② 一键重建中文字体（推荐）
    /// </summary>
    public static class FontAssetRebuilder
    {
        private const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string FontAssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/NotoSansSC SDF.asset";
        private const string SourceFontPath = "Assets/Fonts/NotoSansSC-Regular.ttf";
        private const string BackupFolder = "SceneBackup_手动/FontAssetBackup";

        // 自动执行标记：只在"检测到损坏"时跑一次，避免每次编辑器启动都动资产
        private const string AutoRunKey = "GameNet.FontAssetRebuilder.AutoRun.v1";

        private const int SamplingPointSize = 90;
        private const int AtlasPadding = 9;
        private const int AtlasSize = 2048;

        /// <summary>
        /// 预烘字符：ASCII 全部可见字符 + 中英文标点 + 界面/关卡常用汉字。
        /// 资产保持 Dynamic，之后遇到这里没有的字（例如玩家输入）仍会自动补进图集。
        /// </summary>
        private const string PrewarmCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "，。、；：？！“”‘’（）《》【】—…·＋－×÷＝％" +
            "分数关卡选择开始游戏返回继续暂停重新设置退出主菜单联机创建加入房间码" +
            "简介说明操作移动跳跃射击血量时间得分胜利失败重来第" +
            "一二三四五六七八九十新邻驾到玩家房主客户端连接断开等待正在加载请稍候" +
            "确认取消保存删除编辑帮助关于音量全屏分辨率语言速度护盾冷却道具";

        [MenuItem("Tools/字体修复/① 检查字体资产状态", false, 30)]
        public static void Report()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                Debug.LogError($"[字体修复] 找不到字体资产：{FontAssetPath}");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("[字体修复] 字体资产状态：");
            sb.AppendLine($"  路径：{FontAssetPath}");
            sb.AppendLine($"  生成模式：{font.atlasPopulationMode}（Dynamic=1 / Static=0）");
            sb.AppendLine($"  源字体：{(font.sourceFontFile != null ? font.sourceFontFile.name : "缺失")}");
            sb.AppendLine($"  图集设定：{font.atlasWidth}x{font.atlasHeight}，padding={font.atlasPadding}，渲染模式={font.atlasRenderMode}");
            sb.AppendLine($"  图集纹理：{(font.atlasTextures == null ? 0 : font.atlasTextures.Length)} 页");
            if (font.atlasTextures != null)
            {
                for (int i = 0; i < font.atlasTextures.Length; i++)
                {
                    var tex = font.atlasTextures[i];
                    sb.AppendLine($"    [{i}] {(tex == null ? "null" : tex.width + "x" + tex.height)}");
                }
            }

            sb.AppendLine($"  字形数：{font.glyphTable.Count}，字符数：{font.characterTable.Count}");
            sb.AppendLine($"  判定：{(IsBroken(font) ? "[损坏] 图集纹理尺寸与设定不符，需要重建" : "[正常]")}");
            Debug.Log(sb.ToString());
        }

        [MenuItem("Tools/字体修复/② 一键重建中文字体（推荐）", false, 31)]
        public static void RebuildMenu()
        {
            Rebuild();
        }

        /// <summary>
        /// 脚本编译完成且检测到字体资产损坏时自动修一次。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoRebuildIfBroken()
        {
            if (EditorPrefs.GetBool(AutoRunKey, false))
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoRebuildIfBroken;
                return;
            }

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (!IsBroken(font))
            {
                EditorPrefs.SetBool(AutoRunKey, true);
                return;
            }

            EditorApplication.delayCall += Rebuild;
        }

        public static void Rebuild()
        {
            EditorPrefs.SetBool(AutoRunKey, true);
            var total = Stopwatch.StartNew();

            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fontAsset == null)
            {
                Debug.LogError($"[字体修复] 找不到字体资产：{FontAssetPath}");
                return;
            }

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                Debug.LogError($"[字体修复] 找不到源字体：{SourceFontPath}（中文字形无法生成）");
                return;
            }

            BackupAssetFiles();
            Debug.Log($"[字体修复] 开始修复 {FontAssetPath}");

            // ① 图集参数必须"先设、后清空"：
            //    ClearFontAssetData 会按当前的 m_AtlasWidth/m_AtlasHeight 重建自由矩形区，
            //    若先清空再改尺寸，矩形区与真实纹理又会对不上 —— 那正是上次卡死的成因。
            //    （TMP 这几个属性是 internal set，脚本里只能走 SerializedObject。）
            var fontSo = new SerializedObject(fontAsset);
            bool fontDirty = false;

            // 生成模式改回 Dynamic，缺字才会自动补进图集
            fontDirty |= SetIntIfDifferent(fontSo, "m_AtlasPopulationMode", (int)AtlasPopulationMode.Dynamic, "图集生成模式");

            // 图集放大到 2048，预烘字符单页即可放下，避免多页图集带来的额外 draw call
            fontDirty |= SetIntIfDifferent(fontSo, "m_AtlasWidth", AtlasSize, "图集宽度");
            fontDirty |= SetIntIfDifferent(fontSo, "m_AtlasHeight", AtlasSize, "图集高度");
            fontDirty |= SetIntIfDifferent(fontSo, "m_AtlasPadding", AtlasPadding, "图集 padding");

            // 源字体引用必须存在，否则动态生成字形无从谈起
            var srcProp = fontSo.FindProperty("m_SourceFontFile");
            if (srcProp != null && srcProp.objectReferenceValue == null)
            {
                srcProp.objectReferenceValue = sourceFont;
                fontDirty = true;
                Debug.Log($"[字体修复] 源字体引用已补回：{sourceFont.name}");
            }

            if (fontDirty)
            {
                fontSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // ② 清掉与图集纹理不一致的字形 / 矩形记录（之前卡死的根因就在这里）
            fontAsset.ClearFontAssetData(true);
            Debug.Log($"[字体修复] 旧字形数据已清空，图集准备重建为 {AtlasSize}x{AtlasSize}（点值 {SamplingPointSize}，padding {AtlasPadding}）");

            // ③ 预烘界面会用到的字符。
            //    预烘期间临时关掉"多图集"：万一字符装不下，TMP 只会返回缺失字符 + 打日志，
            //    而不会掉进 TryAddGlyphsToNewAtlasTexture 的 while 死循环里把编辑器卡死。
            var multiSo = new SerializedObject(fontAsset);
            var multiProp = multiSo.FindProperty("m_IsMultiAtlasTexturesEnabled");
            bool multiAtlasWasEnabled = multiProp != null && multiProp.boolValue;
            if (multiAtlasWasEnabled)
            {
                multiProp.boolValue = false;
                multiSo.ApplyModifiedPropertiesWithoutUndo();
            }

            var bakeWatch = Stopwatch.StartNew();
            bool allAdded = fontAsset.TryAddCharacters(PrewarmCharacters, out string missing);
            bakeWatch.Stop();

            if (multiProp != null && multiAtlasWasEnabled)
            {
                multiSo.Update();
                multiProp.boolValue = true;
                multiSo.ApplyModifiedPropertiesWithoutUndo();
            }

            int missingCount = missing == null ? 0 : missing.Length;
            Debug.Log($"[字体修复] 预烘 {PrewarmCharacters.Length} 个字符：{(allAdded ? "全部成功" : "部分缺失")}，" +
                      $"缺失 {missingCount} 个，耗时 {bakeWatch.ElapsedMilliseconds} ms，" +
                      $"图集页数 {(fontAsset.atlasTextures == null ? 0 : fontAsset.atlasTextures.Length)}，" +
                      $"字符数 {fontAsset.characterTable.Count}");

            // ④ 材质与图集纹理登记为子资产并写盘
            PersistSubAssets(fontAsset);

            // TMP Settings：默认字体 + 关闭运行时字体特性解析（CJK 字体下这是已知的性能/卡死风险点）
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsPath);
            if (settings != null)
            {
                var settingsSo = new SerializedObject(settings);
                var featuresProp = settingsSo.FindProperty("m_GetFontFeaturesAtRuntime");
                if (featuresProp != null && featuresProp.boolValue)
                {
                    featuresProp.boolValue = false;
                    settingsSo.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(settings);
                    Debug.Log("[字体修复] 已关闭 TMP 运行时字体特性解析（防卡）");
                }

                var defaultFontProp = settingsSo.FindProperty("m_defaultFontAsset");
                if (defaultFontProp != null && defaultFontProp.objectReferenceValue != fontAsset)
                {
                    defaultFontProp.objectReferenceValue = fontAsset;
                    settingsSo.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(settings);
                    Debug.Log("[字体修复] TMP 默认字体已指向 NotoSansSC SDF");
                }

                AssetDatabase.SaveAssets();
            }

            // 让场景里已打开的文本立刻按新图集重绘
            foreach (var text in UnityEngine.Object.FindObjectsOfType<TMP_Text>(true))
            {
                text.SetAllDirty();
            }

            total.Stop();
            Debug.Log($"[字体修复] 完成，总耗时 {total.ElapsedMilliseconds} ms");
            Report();
        }

        private static bool IsBroken(TMP_FontAsset font)
        {
            if (font == null)
            {
                return true;
            }

            if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            {
                return true;
            }

            if (font.sourceFontFile == null)
            {
                return true;
            }

            if (font.atlasTextures == null || font.atlasTextures.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < font.atlasTextures.Length; i++)
            {
                var tex = font.atlasTextures[i];
                if (tex == null) return true;

                // 图集纹理尺寸必须和字形打包时记录的尺寸一致，否则 TMP 打包循环会卡死
                if (tex.width != font.atlasWidth || tex.height != font.atlasHeight) return true;
            }

            return font.characterTable.Count == 0;
        }

        /// <summary>
        /// 通过 SerializedObject 修改字体资产字段。
        /// TMP 的 atlasWidth / atlasHeight / atlasPadding / atlasRenderMode 都是 internal set，
        /// 脚本里无法直接赋值，只能走序列化通道。
        /// </summary>
        private static bool SetIntIfDifferent(SerializedObject so, string propertyName, int value, string label)
        {
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"[字体修复] 找不到序列化字段 {propertyName}，已跳过");
                return false;
            }

            if (prop.intValue == value)
            {
                return false;
            }

            int oldValue = prop.intValue;
            prop.intValue = value;
            Debug.Log($"[字体修复] {label}：{oldValue} -> {value}");
            return true;
        }

        /// <summary>
        /// 把字体资产的材质与图集纹理登记为子资产并写回引用，
        /// 否则它们不会被序列化进 .asset（就是上次把资产写坏的原因）。
        /// </summary>
        private static void PersistSubAssets(TMP_FontAsset fontAsset)
        {
            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                EditorUtility.SetDirty(fontAsset.material);
            }

            if (fontAsset.atlasTextures != null)
            {
                for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    var tex = fontAsset.atlasTextures[i];
                    if (tex == null) continue;

                    tex.name = fontAsset.name + " Atlas " + i;
                    AssetDatabase.AddObjectToAsset(tex, fontAsset);
                    EditorUtility.SetDirty(tex);
                }
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 修复前把当前资产物理复制到工程外（Assets 之外，不会被导入），便于回滚。
        /// </summary>
        private static void BackupAssetFiles()
        {
            try
            {
                if (!Directory.Exists(BackupFolder))
                {
                    Directory.CreateDirectory(BackupFolder);
                }

                if (!File.Exists(FontAssetPath))
                {
                    return;
                }

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string target = Path.Combine(BackupFolder, $"NotoSansSC SDF.broken.{stamp}.asset");
                File.Copy(FontAssetPath, target, true);
                Debug.Log("[字体修复] 已备份旧资产到：" + target);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[字体修复] 备份旧资产失败（不影响修复）：" + e.Message);
            }
        }
    }
}
