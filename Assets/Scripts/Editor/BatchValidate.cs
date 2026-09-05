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
            Check(scenes.Contains("MainMenu") && scenes.Contains("GamePlay") && scenes.Contains("SampleScene"), $"BuildSettings 场景注册：[{string.Join(", ", scenes)}]");

            // ---- MainMenu ----
            var menu = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
            Check(menu.IsValid(), "MainMenu 场景加载");

            var netGo = GameObject.Find("NetworkManager_GO");
            Check(netGo != null, "NetworkManager_GO 存在");

            var nm = netGo != null ? netGo.GetComponent<NetworkManager>() : null;
            Check(nm != null, "NetworkManager 组件存在");
            Check(nm != null && nm.GetComponent<UnityTransport>() != null, "UnityTransport 组件存在");
            Check(nm != null && nm.GetComponent<GameNetworkManager>() != null, "GameNetworkManager 组件存在");

            // MainMenu 现在默认使用 SafeMainMenuRuntime 在运行时创建 UI，
            // 旧版 MainMenuUIController 仍保留给已有场景/测试兼容。
            var legacyUi = netGo != null ? netGo.GetComponent<MainMenuUIController>() : null;
            var runtimeUi = GameObject.Find("MainMenuRuntime")?.GetComponent<SafeMainMenuRuntime>();
            bool hasSupportedMenuUi = legacyUi != null || runtimeUi != null;
            Check(hasSupportedMenuUi, "MainMenu UI 控制器存在（旧版或运行时安全 UI）");

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

            if (legacyUi != null)
            {
                Check(legacyUi.Btn_CreateGame != null, "UI 引用 Btn_CreateGame");
                Check(legacyUi.Btn_JoinGame != null, "UI 引用 Btn_JoinGame");
                Check(legacyUi.Panel_CreateRoom != null && !legacyUi.Panel_CreateRoom.activeSelf, "Panel_CreateRoom 存在且默认隐藏");
                Check(legacyUi.Panel_JoinRoom != null && !legacyUi.Panel_JoinRoom.activeSelf, "Panel_JoinRoom 存在且默认隐藏");
                Check(legacyUi.Txt_RoomCode != null, "UI 引用 Txt_RoomCode");
                Check(legacyUi.RoomStatus != null, "UI 引用 RoomStatus");
                Check(legacyUi.PlayerCount != null, "UI 引用 PlayerCount");
                Check(legacyUi.Btn_StartHost != null, "UI 引用 Btn_StartHost");
                Check(legacyUi.Input_RoomCode != null, "UI 引用 Input_RoomCode");
                Check(legacyUi.ErrorText != null, "UI 引用 ErrorText");
                Check(legacyUi.Btn_JoinClient != null, "UI 引用 Btn_JoinClient");

                Check(HasClickCall(legacyUi.Btn_CreateGame, "OnClickCreateGame"), "创建游戏按钮 OnClick → OnClickCreateGame");
                Check(HasClickCall(legacyUi.Btn_JoinGame, "OnClickJoinGame"), "加入游戏按钮 OnClick → OnClickJoinGame");
                Check(HasClickCall(legacyUi.Btn_StartHost, "OnClickStartHost"), "开始游戏按钮 OnClick → OnClickStartHost");
                Check(HasClickCall(legacyUi.Btn_JoinClient, "OnClickJoinClient"), "加入按钮 OnClick → OnClickJoinClient");
            }
            else
            {
                Check(runtimeUi != null, "SafeMainMenuRuntime 运行时 UI 存在");
                Debug.Log("[Validate] 检测到运行时安全 UI，跳过旧版序列化按钮引用检查");
            }

            // ---- SampleScene 原始界面 ----
            var sample = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            Check(sample.IsValid(), "SampleScene 场景加载");
            var sampleCanvas = GameObject.Find("Canvas");
            var sampleUi = sampleCanvas != null ? sampleCanvas.GetComponent<SampleSceneMenuRuntime>() : null;
            var sampleCreate = GameObject.Find("创建游戏")?.GetComponent<Button>();
            var sampleJoin = GameObject.Find("加入游戏")?.GetComponent<Button>();
            var sampleNetGo = GameObject.Find("NetworkManager_GO");
            var sampleNm = sampleNetGo != null ? sampleNetGo.GetComponent<NetworkManager>() : null;
            Check(sampleUi != null, "SampleScene 存档/联机 UI 控制器存在");
            Check(sampleCreate != null && HasClickCall(sampleCreate, "OpenCreateFromOriginalButton"), "SampleScene 创建游戏按钮已绑定存档选择");
            Check(sampleJoin != null && HasClickCall(sampleJoin, "OpenJoinFromOriginalButton"), "SampleScene 加入游戏按钮已绑定房间码入口");
            Check(sampleNm != null && sampleNm.GetComponent<UnityTransport>() != null && sampleNm.GetComponent<GameNetworkManager>() != null, "SampleScene NetworkManager/Transport/GameNetworkManager 完整");

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
