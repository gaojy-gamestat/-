using System;
using System.IO;
using System.Linq;
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
    }
}
