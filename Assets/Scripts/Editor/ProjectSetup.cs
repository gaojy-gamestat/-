using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GameNet;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 一次性工程落地脚本（batchmode -executeMethod 执行）：
    /// 1. TMP 字体资产（中文可渲染）；
    /// 2. Player 网络预设（NetworkObject + NetworkTransform + CharacterController）；
    /// 3. MainMenu 场景：旧版项目可自动生成 UI；若已存在 MainMenuRuntime，则保留运行时安全 UI；
    /// 4. GamePlay 联网场景；
    /// 5. Build Settings 场景注册。
    /// </summary>
    public static class ProjectSetup
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GamePlayScenePath = "Assets/Scenes/GamePlay.unity";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string NetworkPrefabsPath = "Assets/Prefabs/NetworkPrefabsList.asset";
        private const string FontAssetPath = "Assets/TextMesh Pro/Fonts/MSYH SDF.asset";

        public static void RunAll()
        {
            Debug.Log("== ProjectSetup 开始 ==");
            EnsureFolders();
            var font = EnsureCjkFontAsset();
            CreatePlayerPrefab();
            SetupMainMenuScene(font);
            CreateGameplayScene(font);
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("== ProjectSetup 完成 ==");
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------
        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Scripts");
            EnsureFolder("Assets", "Prefabs");
            EnsureFolder("Assets", "TextMesh Pro");
            EnsureFolder("Assets/TextMesh Pro", "Fonts");
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        // ------------------------------------------------------------------
        // TMP 字体：从系统微软雅黑生成动态 SDF 字体资产，保证中文 UI 可渲染。
        // ------------------------------------------------------------------
        private static TMP_FontAsset EnsureCjkFontAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
            {
                ApplyDefaultFont(existing);
                return existing;
            }

            Font osFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 64);
            if (osFont == null)
            {
                string[] candidates = { "C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/msyh.ttf", "C:/Windows/Fonts/simhei.ttf" };
                foreach (string path in candidates)
                {
                    if (File.Exists(path))
                    {
                        osFont = new Font(path);
                        break;
                    }
                }
            }

            if (osFont == null)
            {
                Debug.LogWarning("ProjectSetup: 找不到中文字体，UI 中文可能渲染为方块。");
                osFont = Font.CreateDynamicFontFromOSFont("Arial", 64);
            }

            var fontAsset = TMP_FontAsset.CreateFontAsset(osFont, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic);
            fontAsset.name = "MSYH SDF";
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }
            if (fontAsset.atlasTextures != null)
            {
                foreach (var tex in fontAsset.atlasTextures)
                {
                    if (tex != null) AssetDatabase.AddObjectToAsset(tex, fontAsset);
                }
            }

            AssetDatabase.SaveAssets();
            ApplyDefaultFont(fontAsset);
            Debug.Log($"ProjectSetup: 已生成中文字体资产 {FontAssetPath}");
            return fontAsset;
        }

        private static void ApplyDefaultFont(TMP_FontAsset fontAsset)
        {
            // TMP Settings 资产缺失时先创建，否则 TMP 文本无法渲染。
            var settings = TMP_Settings.instance;
            if (settings != null)
            {
                var serializedSettings = new SerializedObject(settings);
                serializedSettings.FindProperty("m_defaultFontAsset").objectReferenceValue = fontAsset;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>给场景里所有 TMP 文本统一设置中文字体。</summary>
        private static void ApplyFontToAllTexts(UnityEngine.SceneManagement.Scene scene, TMP_FontAsset font)
        {
            foreach (var tmp in scene.GetRootGameObjects())
            {
                foreach (var text in tmp.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.font != font)
                    {
                        text.font = font;
                        EditorUtility.SetDirty(text);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Player 网络预设
        // ------------------------------------------------------------------
        private static void CreatePlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) != null)
            {
                Debug.Log("ProjectSetup: Player 预设已存在，跳过。");
                return;
            }

            var root = new GameObject("Player");
            root.tag = "Player";

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            // 服务器权威同步（Host-authoritative），同步位置与旋转。
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;

            root.AddComponent<PlayerController>();
            root.AddComponent<PlayerIdentity>();

            // 视觉占位：胶囊体（后续替换正式角色模型时替换此子物体即可）。
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var meshFilter = visual.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Capsule.fbx");
            var meshRenderer = visual.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Standard"));
            material.color = new Color(0.7f, 0.7f, 0.75f);
            meshRenderer.sharedMaterial = material;
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log($"ProjectSetup: 已创建 Player 预设 {PlayerPrefabPath}");
            UnityEngine.Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------
        // MainMenu 场景
        // ------------------------------------------------------------------
        private static void SetupMainMenuScene(TMP_FontAsset font)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
            {
                // 基于现有 SampleScene 建立副本，保留全部已有 UI。
                var sample = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
                EditorSceneManager.SaveScene(sample, MainMenuScenePath);
                Debug.Log("ProjectSetup: 已从 SampleScene 复制生成 MainMenu。");
            }

            var scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);

            // 当前工程的 MainMenu 使用 SafeMainMenuRuntime 动态创建轻量 UI，
            // 这是为 Unity 2022.3.62f3 规避旧 UGUI 反序列化崩溃的稳定入口。
            // 旧版自动搭建逻辑不能覆盖它，否则会把工程重新写回易崩的复杂 Canvas。
            if (FindByName(scene, "MainMenuRuntime") != null)
            {
                Debug.Log("ProjectSetup: 检测到 MainMenuRuntime，保留当前安全主菜单，跳过旧版 UGUI 重建。");
                return;
            }

            ApplyFontToAllTexts(scene, font);

            var canvas = FindByName(scene, "Panel");
            if (canvas == null)
            {
                throw new Exception("MainMenu 中找不到 Canvas 根节点 Panel！");
            }

            var buttonCreate = FindByName(scene, "创建游戏");
            var buttonJoin = FindByName(scene, "加入游戏");
            var buttonQuit = FindByName(scene, "退出");

            // ---- NetworkManager 根对象 ----
            GameObject netRoot = FindByName(scene, "NetworkManager_GO");
            if (netRoot == null)
            {
                netRoot = new GameObject("NetworkManager_GO");
            }

            var networkManager = netRoot.GetComponent<NetworkManager>() ?? netRoot.AddComponent<NetworkManager>();
            var transport = netRoot.GetComponent<UnityTransport>() ?? netRoot.AddComponent<UnityTransport>();
            var gameNet = netRoot.GetComponent<GameNetworkManager>() ?? netRoot.AddComponent<GameNetworkManager>();
            var ui = netRoot.GetComponent<MainMenuUIController>() ?? netRoot.AddComponent<MainMenuUIController>();

            transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");

            // ---- NetworkManager 配置（Player Prefab / NetworkPrefabsList / Scene Management）----
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null) throw new Exception("Player 预设缺失！");

            var prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (prefabsList == null)
            {
                prefabsList = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                prefabsList.Add(new NetworkPrefab { Prefab = playerPrefab });
                AssetDatabase.CreateAsset(prefabsList, NetworkPrefabsPath);
                Debug.Log($"ProjectSetup: 已创建 {NetworkPrefabsPath}");
            }
            else if (prefabsList.PrefabList.Count == 0)
            {
                prefabsList.Add(new NetworkPrefab { Prefab = playerPrefab });
                EditorUtility.SetDirty(prefabsList);
            }

            ConfigureNetworkManager(networkManager, transport, playerPrefab, prefabsList);

            // ---- Panel_CreateRoom ----
            var panelCreate = FindByName(scene, "Panel_CreateRoom");
            if (panelCreate == null)
            {
                panelCreate = CreateFullScreenPanel(canvas.transform, "Panel_CreateRoom", new Color(0f, 0f, 0f, 0.72f));

                CreateLabel(panelCreate.transform, "Title_Create", "创建房间", 48, new Vector2(0f, 300f), new Vector2(800f, 80f), font, TextAlignmentOptions.Center);
                var roomCode = CreateLabel(panelCreate.transform, "Txt_RoomCode", "正在创建房间…", 64, new Vector2(0f, 170f), new Vector2(1200f, 110f), font, TextAlignmentOptions.Center, Color.yellow);
                var status = CreateLabel(panelCreate.transform, "RoomStatus", "待机", 36, new Vector2(0f, 60f), new Vector2(1200f, 60f), font, TextAlignmentOptions.Center);
                var count = CreateLabel(panelCreate.transform, "PlayerCount", "0/2 玩家", 36, new Vector2(0f, -10f), new Vector2(1200f, 60f), font, TextAlignmentOptions.Center);
                var startBtn = CreateButton(panelCreate.transform, "Btn_StartHost", "开始游戏", 40, new Vector2(0f, -140f), new Vector2(420f, 90f), font, new Color(0.2f, 0.75f, 0.35f, 1f));
                var backBtn = CreateButton(panelCreate.transform, "Btn_BackCreate", "返回", 32, new Vector2(0f, -260f), new Vector2(260f, 70f), font, new Color(0.45f, 0.45f, 0.5f, 1f));

                // 挂引用（SerializedField 赋值）
                var so = new SerializedObject(ui);
                so.FindProperty("Panel_CreateRoom").objectReferenceValue = panelCreate;
                so.FindProperty("Txt_RoomCode").objectReferenceValue = roomCode.GetComponent<TMP_Text>();
                so.FindProperty("RoomStatus").objectReferenceValue = status.GetComponent<TMP_Text>();
                so.FindProperty("PlayerCount").objectReferenceValue = count.GetComponent<TMP_Text>();
                so.FindProperty("Btn_StartHost").objectReferenceValue = startBtn.GetComponent<Button>();
                so.ApplyModifiedProperties();

                UnityEventTools.AddPersistentListener(startBtn.GetComponent<Button>().onClick, ui.OnClickStartHost);
                // 返回：直接隐藏面板
                UnityEventTools.AddPersistentListener(backBtn.GetComponent<Button>().onClick, ui.OnClickBackToMenu);
            }
            else
            {
                Debug.Log("ProjectSetup: Panel_CreateRoom 已存在，跳过创建。");
            }

            panelCreate.SetActive(false);

            // ---- Panel_JoinRoom ----
            var panelJoin = FindByName(scene, "Panel_JoinRoom");
            if (panelJoin == null)
            {
                panelJoin = CreateFullScreenPanel(canvas.transform, "Panel_JoinRoom", new Color(0f, 0f, 0f, 0.72f));

                CreateLabel(panelJoin.transform, "Title_Join", "加入房间", 48, new Vector2(0f, 300f), new Vector2(800f, 80f), font, TextAlignmentOptions.Center);
                var input = CreateInputField(panelJoin.transform, "Input_RoomCode", "请输入房间码", new Vector2(0f, 140f), new Vector2(640f, 90f), font);
                var error = CreateLabel(panelJoin.transform, "ErrorText", string.Empty, 32, new Vector2(0f, 40f), new Vector2(1200f, 60f), font, TextAlignmentOptions.Center, new Color(1f, 0.35f, 0.3f, 1f));
                var joinBtn = CreateButton(panelJoin.transform, "Btn_JoinClient", "加入", 40, new Vector2(0f, -90f), new Vector2(420f, 90f), font, new Color(0.2f, 0.55f, 0.85f, 1f));
                var backBtn = CreateButton(panelJoin.transform, "Btn_BackJoin", "返回", 32, new Vector2(0f, -210f), new Vector2(260f, 70f), font, new Color(0.45f, 0.45f, 0.5f, 1f));

                var so = new SerializedObject(ui);
                so.FindProperty("Panel_JoinRoom").objectReferenceValue = panelJoin;
                so.FindProperty("Input_RoomCode").objectReferenceValue = input.GetComponent<TMP_InputField>();
                so.FindProperty("ErrorText").objectReferenceValue = error.GetComponent<TMP_Text>();
                so.FindProperty("Btn_JoinClient").objectReferenceValue = joinBtn.GetComponent<Button>();
                so.ApplyModifiedProperties();

                UnityEventTools.AddPersistentListener(joinBtn.GetComponent<Button>().onClick, ui.OnClickJoinClient);
                UnityEventTools.AddPersistentListener(backBtn.GetComponent<Button>().onClick, ui.OnClickBackToMenu);
            }
            else
            {
                Debug.Log("ProjectSetup: Panel_JoinRoom 已存在，跳过创建。");
            }

            panelJoin.SetActive(false);

            // ---- 主菜单按钮接线 ----
            var so2 = new SerializedObject(ui);
            if (buttonCreate != null)
            {
                so2.FindProperty("Btn_CreateGame").objectReferenceValue = buttonCreate.GetComponent<Button>();
                WireClick(buttonCreate.GetComponent<Button>(), ui.OnClickCreateGame);
            }
            if (buttonJoin != null)
            {
                so2.FindProperty("Btn_JoinGame").objectReferenceValue = buttonJoin.GetComponent<Button>();
                WireClick(buttonJoin.GetComponent<Button>(), ui.OnClickJoinGame);
            }
            so2.ApplyModifiedProperties();

            if (buttonQuit != null)
            {
                WireClick(buttonQuit.GetComponent<Button>(), ui.OnClickQuit);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
            Debug.Log("ProjectSetup: MainMenu 场景配置完成并保存。");
        }

        private static void WireClick(Button button, UnityEngine.Events.UnityAction action)
        {
            // 幂等：避免重复接线。
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentTarget(i) is MainMenuUIController && button.onClick.GetPersistentMethodName(i) == action.Method.Name)
                {
                    return;
                }
            }

            UnityEventTools.AddPersistentListener(button.onClick, action);
        }

        private static void ConfigureNetworkManager(NetworkManager networkManager, UnityTransport transport, GameObject playerPrefab, NetworkPrefabsList prefabsList)
        {
            // NGO 1.x / 2.x 均为公共字段，直接赋值（编辑器下持久化到场景）。
            var config = networkManager.NetworkConfig;
            config.PlayerPrefab = playerPrefab;
            config.NetworkTransport = transport;
            config.EnableSceneManagement = true;
            config.ForceSamePrefabs = true;
            config.ConnectionApproval = true; // Host 在审批回调里指定出生点

            if (!config.Prefabs.NetworkPrefabsLists.Contains(prefabsList))
            {
                config.Prefabs.NetworkPrefabsLists.Add(prefabsList);
            }

            EditorUtility.SetDirty(networkManager);
            Debug.Log($"ProjectSetup: NetworkManager 配置完成（PlayerPrefab={playerPrefab.name}, SceneManagement=On, ConnectionApproval=On, PrefabList={prefabsList.name}）");
        }

        // ------------------------------------------------------------------
        // GamePlay 场景
        // ------------------------------------------------------------------
        private static void CreateGameplayScene(TMP_FontAsset font)
        {
            var existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(GamePlayScenePath);
            if (existing != null)
            {
                Debug.Log("ProjectSetup: GamePlay 场景已存在，跳过创建。");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 相机
            var cameraGo = new GameObject("Main Camera");
            var cam = cameraGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 12f, -14f);
            cameraGo.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            cameraGo.AddComponent<AudioListener>();

            // 平行光
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 地面 + 房间围挡（横向剖面占位，后续替换正式房屋）
            CreateBox(scene, "Ground", new Vector3(0f, -0.25f, 0f), new Vector3(30f, 0.5f, 24f), new Color(0.45f, 0.4f, 0.35f));
            CreateBox(scene, "Wall_North", new Vector3(0f, 1.5f, 12f), new Vector3(30f, 3f, 0.5f), new Color(0.6f, 0.55f, 0.5f));
            CreateBox(scene, "Wall_South", new Vector3(0f, 1.5f, -12f), new Vector3(30f, 3f, 0.5f), new Color(0.6f, 0.55f, 0.5f));
            CreateBox(scene, "Wall_West", new Vector3(-15f, 1.5f, 0f), new Vector3(0.5f, 3f, 24f), new Color(0.6f, 0.55f, 0.5f));
            CreateBox(scene, "Wall_East", new Vector3(15f, 1.5f, 0f), new Vector3(0.5f, 3f, 24f), new Color(0.6f, 0.55f, 0.5f));

            // 出生点标记（实际出生位置由 Host 连接审批指定）
            var spawn1 = new GameObject("SpawnPoint_Host");
            spawn1.transform.position = new Vector3(0f, 1.2f, 4f);
            var spawn2 = new GameObject("SpawnPoint_Client");
            spawn2.transform.position = new Vector3(0f, 1.2f, -4f);

            // 引导组件（输出验证日志）
            new GameObject("GameplayBootstrap").AddComponent<GameplayBootstrap>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, GamePlayScenePath);
            Debug.Log("ProjectSetup: GamePlay 场景已创建并保存。");
        }

        private static void CreateBox(UnityEngine.SceneManagement.Scene scene, string name, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = size;
            var renderer = go.GetComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Standard"));
            material.color = color;
            renderer.sharedMaterial = material;
        }

        // ------------------------------------------------------------------
        private static void ConfigureBuildSettings()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(GamePlayScenePath, true),
            };
            EditorBuildSettings.scenes = scenes;
            Debug.Log("ProjectSetup: Build Settings 场景已注册（MainMenu, GamePlay）。");
        }

        // ------------------------------------------------------------------
        // UI 构建工具
        // ------------------------------------------------------------------
        private static GameObject FindByName(UnityEngine.SceneManagement.Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(go => go.name == name) ??
                   scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Transform>(true))
                        .FirstOrDefault(t => t.name == name)?.gameObject;
        }

        private static GameObject CreateFullScreenPanel(Transform parent, string name, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = panel.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = color;
            return panel;
        }

        private static GameObject CreateLabel(Transform parent, string name, string text, float size, Vector2 pos, Vector2 sizeDelta, TMP_FontAsset font, TextAlignmentOptions alignment = TextAlignmentOptions.Center, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = pos;
            rect.sizeDelta = sizeDelta;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.font = font;
            tmp.alignment = alignment;
            tmp.color = color ?? Color.white;
            tmp.raycastTarget = false;
            return go;
        }

        private static GameObject CreateButton(Transform parent, string name, string text, float fontSize, Vector2 pos, Vector2 sizeDelta, TMP_FontAsset font, Color bgColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = pos;
            rect.sizeDelta = sizeDelta;
            go.GetComponent<Image>().color = bgColor;

            var label = CreateLabel(go.transform, "Text_TMP", text, fontSize, Vector2.zero, sizeDelta, font);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            labelRect.anchoredPosition = Vector2.zero;

            return go;
        }

        private static GameObject CreateInputField(Transform parent, string name, string placeholder, Vector2 pos, Vector2 sizeDelta, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = pos;
            rect.sizeDelta = sizeDelta;
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.92f);

            // Text Area
            var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var taRect = textArea.GetComponent<RectTransform>();
            taRect.SetParent(go.transform, false);
            taRect.anchorMin = Vector2.zero;
            taRect.anchorMax = Vector2.one;
            taRect.offsetMin = new Vector2(12f, 6f);
            taRect.offsetMax = new Vector2(-12f, -6f);

            var text = CreateLabel(taRect, "Text", string.Empty, 40, Vector2.zero, sizeDelta, font, TextAlignmentOptions.MidlineLeft, Color.black);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var ph = CreateLabel(taRect, "Placeholder", placeholder, 40, Vector2.zero, sizeDelta, font, TextAlignmentOptions.MidlineLeft, new Color(0f, 0f, 0f, 0.4f));
            var phRect = ph.GetComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = Vector2.zero;
            phRect.offsetMax = Vector2.zero;

            var inputField = go.GetComponent<TMP_InputField>();
            inputField.textComponent = text.GetComponent<TMP_Text>();
            inputField.placeholder = ph.GetComponent<TMP_Text>();
            inputField.textViewport = taRect.GetComponent<RectTransform>();
            inputField.characterLimit = 12;
            inputField.contentType = TMP_InputField.ContentType.Standard;

            return go;
        }
    }
}
