using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GameNet;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 批处理验证：-executeMethod GameNet.EditorTools.BatchValidate.Validate
    /// 检查场景/Prefab/NetworkManager/UI 接线完整性，失败时返回退出码 1。
    /// </summary>
    public static class BatchValidate
    {
        private static int m_Failures;

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                Debug.Log($"[Validate] PASS {label}");
            }
            else
            {
                m_Failures++;
                Debug.LogError($"[Validate] FAIL {label}");
            }
        }

        public static void Validate()
        {
            m_Failures = 0;

            // ---- Build Settings ----
            var scenes = EditorBuildSettings.scenes.Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)).ToArray();
            Check(scenes.Contains("GamePlay") && scenes.Contains("主菜单"), $"BuildSettings 场景注册：[{string.Join(", ", scenes)}]");

            // ---- 主菜单（美术入口：创建游戏 / 加入游戏 / 退出等按钮）----
            var menu = EditorSceneManager.OpenScene("Assets/Scenes/\u4E3B\u83DC\u5355.unity", OpenSceneMode.Single);
            Check(menu.IsValid(), "主菜单场景加载");

            var netGo = GameObject.Find("NetworkManager_GO");
            Check(netGo != null, "NetworkManager_GO 存在");

            var nm = netGo != null ? netGo.GetComponent<NetworkManager>() : null;
            Check(nm != null, "NetworkManager 组件存在");
            Check(nm != null && nm.GetComponent<UnityTransport>() != null, "UnityTransport 组件存在");
            Check(nm != null && nm.GetComponent<GameNetworkManager>() != null, "GameNetworkManager 组件存在");

            Check(nm != null && nm.NetworkConfig.PlayerPrefab != null, $"NetworkConfig.PlayerPrefab = {(nm != null && nm.NetworkConfig.PlayerPrefab != null ? nm.NetworkConfig.PlayerPrefab.name : "null")}");
            Check(nm != null && nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Count > 0, "NetworkPrefabsList 已注册");
            Check(nm != null && nm.NetworkConfig.EnableSceneManagement, "EnableSceneManagement = true");
            Check(nm != null && nm.NetworkConfig.ConnectionApproval, "ConnectionApproval = true");
            Check(nm != null && nm.NetworkConfig.ForceSamePrefabs, "ForceSamePrefabs = true");

            var playerObj = nm != null && nm.NetworkConfig.PlayerPrefab != null ? nm.NetworkConfig.PlayerPrefab : null;
            Check(playerObj != null && playerObj.GetComponent<NetworkObject>() != null, "Player Prefab 带 NetworkObject");
            Check(playerObj != null && playerObj.GetComponent<NetworkTransform>() != null, "Player Prefab 带 NetworkTransform");
            Check(playerObj != null && playerObj.GetComponent<CharacterController>() != null, "Player Prefab 带 CharacterController");
            Check(playerObj != null && playerObj.GetComponent<PlayerController>() != null, "Player Prefab 带 PlayerController");
            Check(playerObj != null && playerObj.GetComponent<PlayerIdentity>() != null, "Player Prefab 带 PlayerIdentity");

            // 主菜单 UI 由挂在 Canvas 上的 SampleSceneMenuRuntime 运行时创建安全联机弹窗，
            // 创建游戏 / 加入游戏 按钮必须绑定其公开入口。
            var menuCanvas = GameObject.Find("Canvas");
            var menuUi = menuCanvas != null ? menuCanvas.GetComponent<SampleSceneMenuRuntime>() : null;
            Check(menuUi != null, "主菜单存档/联机 UI 控制器存在");
            var menuCreate = GameObject.Find("创建游戏")?.GetComponent<Button>();
            var menuJoin = GameObject.Find("加入游戏")?.GetComponent<Button>();
            Check(menuCreate != null && HasClickCall(menuCreate, "OpenCreateFromOriginalButton"), "主菜单 创建游戏按钮已绑定存档选择");
            Check(menuJoin != null && HasClickCall(menuJoin, "OpenJoinFromOriginalButton"), "主菜单 加入游戏按钮已绑定房间码入口");

            // ---- GamePlay ----
            var play = EditorSceneManager.OpenScene("Assets/Scenes/GamePlay.unity", OpenSceneMode.Single);
            Check(play.IsValid(), "GamePlay 场景加载");
            Check(Camera.main != null, "GamePlay 有主相机");
            Check(GameObject.Find("GameplayBootstrap") != null, "GameplayBootstrap 存在");
            Check(GameObject.Find("SpawnPoint_Host") != null && GameObject.Find("SpawnPoint_Client") != null, "出生点存在");
            Check(GameObject.Find("Ground") != null, "地面存在");

            Debug.Log(m_Failures == 0 ? "[Validate] 全部通过" : $"[Validate] 共 {m_Failures} 项失败");
            EditorApplication.Exit(m_Failures == 0 ? 0 : 1);
        }

        private static bool HasClickCall(Button button, string method)
        {
            if (button == null)
            {
                return false;
            }

            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) == method)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 生成 TMP 设置 + 中文字体资产（在正常打开的编辑器中执行一次即可）。
        /// </summary>
        [MenuItem("Tools/联机工程/生成 TMP 中文字体设置")]
        public static void GenerateTmpFontMenu()
        {
            GenerateTmpFont();
        }

        [MenuItem("Tools/联机工程/验证工程配置（批处理）")]
        public static void ValidateMenu()
        {
            Validate();
        }

        /// <summary>
        /// 构建用于双实例 E2E 测试的 Windows 客户端。
        /// -executeMethod GameNet.EditorTools.BatchValidate.BuildWindowsPlayer
        /// </summary>
        public static void BuildWindowsPlayer()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Build/e2e/E2EClient.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Build] result={report.summary.result} size={report.summary.totalSize} errors={report.summary.totalErrors}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded && report.summary.totalErrors == 0 ? 0 : 1);
        }

        /// <summary>
        /// 生成 TMP 设置 + 中文字体资产（Noto Sans SC 动态字体，OFL 开源可再分发）。
        /// 在可用编辑器中执行一次：-executeMethod GameNet.EditorTools.BatchValidate.GenerateTmpFont
        /// </summary>
        public static void GenerateTmpFont()
        {
            try
            {
                const string resDir = "Assets/TextMesh Pro/Resources";
                const string settingsPath = resDir + "/TMP Settings.asset";
                const string fontAssetPath = resDir + "/Fonts & Materials/NotoSansSC SDF.asset";
                const string projectFontPath = "Assets/Fonts/NotoSansSC-Regular.ttf";

                Directory.CreateDirectory(resDir + "/Fonts & Materials");

                var settings = AssetDatabase.LoadAssetAtPath<TMPro.TMP_Settings>(settingsPath);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<TMPro.TMP_Settings>();
                    AssetDatabase.CreateAsset(settings, settingsPath);
                }

                var fontAsset = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(fontAssetPath);
                // 资产存在但材质引用丢失（历史脚本生成不完整）时删除重建
                if (fontAsset != null && fontAsset.material == null)
                {
                    AssetDatabase.DeleteAsset(fontAssetPath);
                    fontAsset = null;
                }
                if (fontAsset == null)
                {
                    // 使用仓库内自带的 Noto Sans SC（OFL 开源），保证任何机器上都能渲染中文。
                    Font projectFont = AssetDatabase.LoadAssetAtPath<Font>(projectFontPath);
                    if (projectFont == null)
                    {
                        Debug.LogError($"找不到项目字体：{projectFontPath}");
                        EditorApplication.Exit(1);
                        return;
                    }

                    fontAsset = TMPro.TMP_FontAsset.CreateFontAsset(projectFont, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, TMPro.AtlasPopulationMode.Dynamic);
                    fontAsset.name = "NotoSansSC SDF";
                    AssetDatabase.CreateAsset(fontAsset, fontAssetPath);
                    if (fontAsset.material != null)
                    {
                        fontAsset.material.name = "NotoSansSC SDF Material";
                        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                    }
                    if (fontAsset.atlasTextures != null)
                    {
                        foreach (var tex in fontAsset.atlasTextures)
                        {
                            if (tex != null)
                            {
                                AssetDatabase.AddObjectToAsset(tex, fontAsset);
                            }
                        }
                    }

                    // 关键：把新加入的子对象（材质/图集）的引用写回字体资产，否则 material 会保存为 fileID:0
                    EditorUtility.SetDirty(fontAsset);
                    AssetDatabase.SaveAssets();

                    Debug.Log("字体资产已创建：" + fontAssetPath);
                }

                var serializedSettings = new SerializedObject(settings);
                serializedSettings.FindProperty("m_defaultFontAsset").objectReferenceValue = fontAsset;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Debug.Log("TMP Settings 默认字体已设置为 NotoSansSC SDF");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("GenerateTmpFont 失败：" + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// 预烘焙中文字体资产：扫描工程内所有文本资源，收集实际用到的字符（ASCII + 中文标点 +
        /// 汉字 + 全角符号），一次性写进字体图集并保存。资产仍为 Dynamic + 多图集，
        /// 运行期遇到未收录的字符（如玩家输入的生僻字）仍可自动补进图集。
        /// 批处理：-executeMethod GameNet.EditorTools.BatchValidate.GenerateBakedTmpFont
        /// </summary>
        [MenuItem("Tools/联机工程/生成中文常用字字体资产（预烘焙）")]
        public static void GenerateBakedTmpFontMenu()
        {
            GenerateBakedTmpFont();
        }

        public static void GenerateBakedTmpFont()
        {
            bool batchMode = Application.isBatchMode;
            int exitCode = 0;

            try
            {
                const string resDir = "Assets/TextMesh Pro/Resources";
                const string settingsPath = resDir + "/TMP Settings.asset";
                const string fontAssetPath = resDir + "/Fonts & Materials/NotoSansSC SDF.asset";
                const string projectFontPath = "Assets/Fonts/NotoSansSC-Regular.ttf";
                const int samplingPointSize = 90;
                const int atlasPadding = 9;

                Directory.CreateDirectory(resDir + "/Fonts & Materials");

                var settings = AssetDatabase.LoadAssetAtPath<TMPro.TMP_Settings>(settingsPath);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<TMPro.TMP_Settings>();
                    AssetDatabase.CreateAsset(settings, settingsPath);
                }

                var projectFont = AssetDatabase.LoadAssetAtPath<Font>(projectFontPath);
                if (projectFont == null)
                {
                    Debug.LogError($"找不到项目字体：{projectFontPath}");
                    if (batchMode)
                    {
                        EditorApplication.Exit(1);
                    }
                    return;
                }

                string characters = CollectProjectCharacters();
                int atlasSize = ChooseAtlasSize(characters.Length, samplingPointSize, atlasPadding);
                Debug.Log($"[Font] 收集到 {characters.Length} 个字符，图集尺寸 {atlasSize}x{atlasSize}，开始烘焙");

                // 整体重建：旧资产（含历史遗留的空材质/空图集引用）直接删掉重来
                if (AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(fontAssetPath) != null)
                {
                    AssetDatabase.DeleteAsset(fontAssetPath);
                }

                var fontAsset = TMPro.TMP_FontAsset.CreateFontAsset(projectFont, samplingPointSize,
                    atlasPadding, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                    atlasSize, atlasSize, TMPro.AtlasPopulationMode.Dynamic, true);
                fontAsset.name = "NotoSansSC SDF";

                if (fontAsset.TryAddCharacters(characters, out string missingCharacters))
                {
                    Debug.Log("[Font] 全部字符已烘焙进图集");
                }
                else
                {
                    Debug.LogWarning($"[Font] 有 {missingCharacters?.Length ?? 0} 个字符未写入图集：" +
                                     $"{(string.IsNullOrEmpty(missingCharacters) ? "-" : missingCharacters)}");
                }

                AssetDatabase.CreateAsset(fontAsset, fontAssetPath);
                PersistFontAssetSubAssets(fontAsset);

                var serializedSettings = new SerializedObject(settings);
                serializedSettings.FindProperty("m_defaultFontAsset").objectReferenceValue = fontAsset;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                Debug.Log($"字体资产已烘焙：{fontAssetPath}（图集张数 {fontAsset.atlasTextures?.Length ?? 0}，" +
                          $"字符数 {fontAsset.characterTable.Count}）");
            }
            catch (Exception e)
            {
                Debug.LogError("GenerateBakedTmpFont 失败：" + e);
                exitCode = 1;
            }

            if (batchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        /// <summary>
        /// 把字体资产的材质与图集纹理登记为子资产并写回引用，否则 material 会保存成 fileID:0。
        /// </summary>
        private static void PersistFontAssetSubAssets(TMPro.TMP_FontAsset fontAsset)
        {
            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            if (fontAsset.atlasTextures != null)
            {
                foreach (var tex in fontAsset.atlasTextures)
                {
                    if (tex != null)
                    {
                        AssetDatabase.AddObjectToAsset(tex, fontAsset);
                        EditorUtility.SetDirty(tex);
                    }
                }
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 按字符数量估算合适的初始图集尺寸，避免图集过小频繁开新页或过大浪费包体。
        /// </summary>
        private static int ChooseAtlasSize(int characterCount, int samplingPointSize, int atlasPadding)
        {
            int glyphSize = Mathf.Max(1, samplingPointSize + atlasPadding * 2);
            int size = 1024;
            while (size < 4096)
            {
                int perRow = Mathf.Max(1, size / glyphSize);
                int capacity = Mathf.Max(1, (perRow * perRow * 4) / 5); // 预留 20% packing 损耗
                if (capacity >= characterCount)
                {
                    break;
                }

                size *= 2;
            }

            return size;
        }

        private static readonly string[] k_TextFileExtensions =
        {
            ".unity", ".prefab", ".asset", ".cs", ".txt", ".json", ".csv", ".xml", ".html", ".md"
        };

        /// <summary>
        /// 扫描 Assets 下的文本资源，收集工程实际用到的可排版字符。
        /// </summary>
        private static string CollectProjectCharacters()
        {
            var set = new SortedSet<char>();
            if (!Directory.Exists("Assets"))
            {
                return string.Empty;
            }

            foreach (var path in Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories))
            {
                string normalized = path.Replace('\\', '/');
                // 跳过 TMP 自带资源与第三方插件，避免把无关字符烤进图集
                if (normalized.Contains("/TextMesh Pro/") || normalized.Contains("/Plugins/"))
                {
                    continue;
                }

                if (Array.IndexOf(k_TextFileExtensions, Path.GetExtension(path).ToLowerInvariant()) < 0)
                {
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(path, Encoding.UTF8);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (char c in text)
                {
                    if (IsBakeable(c))
                    {
                        set.Add(c);
                    }
                }

                CollectUnicodeEscapes(text, set);
            }

            return new string(set.ToArray());
        }

        private static bool IsBakeable(char c)
        {
            if (c >= 0x20 && c <= 0x7E) return true;     // ASCII 可见字符
            if (c >= 0x2000 && c <= 0x206F) return true; // 通用标点（引号/破折号/省略号）
            if (c >= 0x3000 && c <= 0x303F) return true; // 中文标点
            if (c >= 0x4E00 && c <= 0x9FFF) return true; // CJK 基本汉字
            if (c >= 0xF900 && c <= 0xFAFF) return true; // CJK 兼容汉字
            if (c >= 0xFF00 && c <= 0xFFEF) return true; // 全角符号
            return false;
        }

        /// <summary>
        /// 兼容 Unity YAML / C# 源码中以 \uXXXX 转义形式存储的字符。
        /// </summary>
        private static void CollectUnicodeEscapes(string text, ISet<char> set)
        {
            for (int i = 0; i + 6 <= text.Length; i++)
            {
                if (text[i] != '\\' || text[i + 1] != 'u')
                {
                    continue;
                }

                int code = 0;
                bool valid = true;
                for (int k = 0; k < 4; k++)
                {
                    int value = HexValue(text[i + 2 + k]);
                    if (value < 0)
                    {
                        valid = false;
                        break;
                    }

                    code = (code << 4) | value;
                }

                if (valid && IsBakeable((char)code))
                {
                    set.Add((char)code);
                }
            }
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
