using System;
using System.Collections.Generic;
using GameNet.Gameplay;
using GameNet.UI;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameNet.EditorTools
{
    /// <summary>把第一关的联机数值系统和现有 HUD 素材安装到可运行场景。</summary>
    public static class GameplayProgressSetup
    {
        private const string GamePlayPath = "Assets/Scenes/GamePlay.unity";
        private const string FirstLevelPath = "Assets/Scenes/第一关.unity";
        private const string ConfigFolder = "Assets/Configs/Levels";
        private const string Level01Path = ConfigFolder + "/Level01Config.asset";
        private const string Level02Path = ConfigFolder + "/Level02Config.asset";
        private const string HudFolder = "Assets/Art/Level01HUD";

        public static void InstallAndValidate()
        {
            EnsureFolder("Assets", "Configs");
            EnsureFolder("Assets/Configs", "Levels");
            EnsureFolder("Assets", "Art");
            EnsureFolder("Assets/Art", "Level01HUD");

            var level01 = CreateLevel01();
            var level02 = CreateLevel02();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureHudAssets();

            InstallIntoScene(GamePlayPath, level01, true);
            InstallIntoScene(FirstLevelPath, level01, true);
            EnsureFirstLevelInBuildSettings();
            RunSelfTest(level01, level02);

            AssetDatabase.SaveAssets();
            Debug.Log("[GameplayProgress] 安装与自检全部通过。");
            EditorApplication.Exit(0);
        }

        private static LevelConfig CreateLevel01()
        {
            var config = LoadOrCreate(Level01Path);
            config.levelId = 1;
            config.levelDisplayName = "第一关";
            config.timeLimitSeconds = 600f;
            config.tasks = new List<LevelTaskData>
            {
                Task("find_key", "找到钥匙", 1),
                Task("find_target", "找到目标物", 1),
                Task("escape", "双人撤离", 1)
            };
            EditorUtility.SetDirty(config);
            return config;
        }

        private static LevelConfig CreateLevel02()
        {
            var config = LoadOrCreate(Level02Path);
            config.levelId = 2;
            config.levelDisplayName = "第二关";
            config.timeLimitSeconds = 480f;
            config.tasks = new List<LevelTaskData>
            {
                Task("find_pet", "找到宠物", 1),
                Task("prank", "完成恶作剧", 3),
                Task("escape_backdoor", "从后门撤离", 1)
            };
            EditorUtility.SetDirty(config);
            return config;
        }

        private static LevelTaskData Task(string id, string description, int target)
        {
            return new LevelTaskData { taskId = id, description = description, targetProgress = target, currentProgress = 0 };
        }

        private static LevelConfig LoadOrCreate(string path)
        {
            var config = AssetDatabase.LoadAssetAtPath<LevelConfig>(path);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<LevelConfig>();
            AssetDatabase.CreateAsset(config, path);
            return config;
        }

        private static void InstallIntoScene(string scenePath, LevelConfig level01, bool buildHud)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) throw new InvalidOperationException(scenePath + " 场景加载失败。");

            var root = GameObject.Find("GameplayProgressSystems") ?? new GameObject("GameplayProgressSystems");
            SceneManager.MoveGameObjectToScene(root, scene);

            var networkObject = root.GetComponent<NetworkObject>() ?? root.AddComponent<NetworkObject>();
            var progressRoot = root.GetComponent<GameplayProgressRoot>() ?? root.AddComponent<GameplayProgressRoot>();
            var anger = root.GetComponent<HomeownerAngerSystem>() ?? root.AddComponent<HomeownerAngerSystem>();
            var tasks = root.GetComponent<LevelTaskSystem>() ?? root.AddComponent<LevelTaskSystem>();
            var countdown = root.GetComponent<CountdownSystem>() ?? root.AddComponent<CountdownSystem>();
            var debug = AddOptionalDebugComponent(root);

            progressRoot.Configure(level01);
            tasks.SetLevelConfig(level01);
            countdown.SetDuration(level01.timeLimitSeconds);
            EditorUtility.SetDirty(networkObject);
            EditorUtility.SetDirty(progressRoot);
            EditorUtility.SetDirty(anger);
            EditorUtility.SetDirty(tasks);
            EditorUtility.SetDirty(countdown);
            if (debug != null) EditorUtility.SetDirty(debug);

            if (buildHud) BuildLevel01Hud(scene, root, anger, tasks, countdown);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[GameplayProgress] PASS 已安装 {scenePath}，NetworkObject + 三个 NetworkBehaviour 就绪。");
        }

        private static Component AddOptionalDebugComponent(GameObject root)
        {
            const string debugTypeName = "GameNet.Debugging.GameplayProgressDebug, Assembly-CSharp";
            var debugType = Type.GetType(debugTypeName);
            if (debugType == null) return null;
            return root.GetComponent(debugType) ?? root.AddComponent(debugType);
        }

        private static void BuildLevel01Hud(Scene scene, GameObject systemsRoot, HomeownerAngerSystem anger,
            LevelTaskSystem tasks, CountdownSystem countdown)
        {
            var canvas = GameObject.Find("Level01HUD");
            if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
            canvas = new GameObject("Level01HUD", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(canvas, scene);
            ClearChildren(canvas.transform);

            var canvasComponent = canvas.GetComponent<Canvas>();
            if (canvasComponent == null) canvasComponent = canvas.AddComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasComponent.sortingOrder = 20;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.AddComponent<GraphicRaycaster>();
            var canvasRect = canvas.GetComponent<RectTransform>();
            Undo.RecordObject(canvasRect, "Reset level HUD root transform");
            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one;
            EditorUtility.SetDirty(canvasRect);
            var serializedCanvasRect = new SerializedObject(canvasRect);
            serializedCanvasRect.FindProperty("m_LocalScale").vector3Value = Vector3.one;
            serializedCanvasRect.ApplyModifiedPropertiesWithoutUndo();

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/msyh SDF.asset")
                       ?? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/NotoSansSC SDF.asset");

            BuildMissionBoard(canvas.transform, font, tasks);
            BuildCountdown(canvas.transform, font, countdown);
            BuildControlHelp(canvas.transform);
            BuildPlayerPanels(canvas.transform);
            BuildBackpack(canvas.transform);
            BuildAngerBar(canvas.transform, font, anger);
            BuildControlButtons(canvas.transform);
            EditorUtility.SetDirty(canvas);
        }

        private static void BuildMissionBoard(Transform parent, TMP_FontAsset font, LevelTaskSystem tasks)
        {
            var board = CreateImage("MissionBoard", parent, SpriteAt("d551eff802c4e47eaf0c7943180bfece.png"));
            SetRect(board.gameObject, new Vector2(285f, 885f), new Vector2(535f, 270f));
            var title = CreateText("LevelTitle", board.transform, font, "Duke家一楼客厅", 25, TextAlignmentOptions.Left);
            title.color = new Color(0.16f, 0.09f, 0.04f);
            title.fontStyle = FontStyles.Bold;
            SetRect(title.gameObject, new Vector2(-35f, 78f), new Vector2(385f, 38f));
            var objective = CreateText("Objective", board.transform, font,
                "本关目标：\n在Duke回来前，布置陷阱并\n触发至少 6 次整蛊", 19, TextAlignmentOptions.Left);
            objective.color = new Color(0.16f, 0.09f, 0.04f);
            objective.enableWordWrapping = true;
            objective.lineSpacing = -8f;
            SetRect(objective.gameObject, new Vector2(5f, -5f), new Vector2(430f, 118f));
            var descriptions = new TMP_Text[3];
            var progress = new TMP_Text[3];
            var statuses = new TMP_Text[3];
            var labels = new[] { "找到钥匙", "找到目标物", "双人撤离" };
            for (int i = 0; i < 3; i++)
            {
                var row = CreateText("Task" + (i + 1), board.transform, font, labels[i], 24, TextAlignmentOptions.Left);
                row.gameObject.SetActive(false);
                SetRect(row.gameObject, new Vector2(-100f, 65f - i * 58f), new Vector2(280f, 42f));
                descriptions[i] = row;
                var count = CreateText("Progress" + (i + 1), board.transform, font, "0 / 1", 22, TextAlignmentOptions.Right);
                count.gameObject.SetActive(false);
                SetRect(count.gameObject, new Vector2(120f, 65f - i * 58f), new Vector2(85f, 42f));
                progress[i] = count;
                var status = CreateText("Status" + (i + 1), board.transform, font, "", 28, TextAlignmentOptions.Center);
                status.gameObject.SetActive(false);
                SetRect(status.gameObject, new Vector2(-205f, 65f - i * 58f), new Vector2(42f, 42f));
                statuses[i] = status;
            }

            var ui = board.gameObject.AddComponent<TaskProgressUI>();
            ui.Configure(tasks, null, null, null, descriptions, progress, statuses);
        }

        private static void BuildCountdown(Transform parent, TMP_FontAsset font, CountdownSystem countdown)
        {
            var panel = CreateImage("CountdownPanel", parent, SpriteAt("cd9e3fd1410ef30d1e5f9a9a290ed1ff.png"));
            SetRect(panel.gameObject, new Vector2(960f, 930f), new Vector2(590f, 190f));
            var clock = CreateImage("Clock", panel.transform, SpriteAt("b089da71e3db017752b37158c4e9d32e.png"));
            SetRect(clock.gameObject, new Vector2(-190f, -5f), new Vector2(125f, 125f));
            var time = CreateText("TimeText", panel.transform, font, "10:00", 42, TextAlignmentOptions.Center);
            SetRect(time.gameObject, new Vector2(105f, -5f), new Vector2(210f, 64f));
            var ui = panel.gameObject.AddComponent<CountdownUI>();
            ui.Configure(countdown, time, null);
        }

        private static void BuildControlHelp(Transform parent)
        {
            var help = CreateImage("ControlHelp", parent, SpriteAt("89827f70c2513ce3090fd6fb92ab4713.png"));
            SetRect(help.gameObject, new Vector2(1510f, 755f), new Vector2(230f, 285f));
        }

        private static void BuildPlayerPanels(Transform parent)
        {
            var selector = CreateImage("CharacterSelector", parent, SpriteAt("6fc7fc6e0a959e7b9dc0c7814789b868.png"));
            SetRect(selector.gameObject, new Vector2(960f, 165f), new Vector2(430f, 230f));

            var felixPortrait = CreateImage("FelixPortrait", parent, SpriteAt("8bcf2273c8d7f00996b43cc4a29a40fc.png"));
            SetRect(felixPortrait.gameObject, new Vector2(115f, 155f), new Vector2(125f, 125f));
            var felixButton = CreateImage("FelixButton", parent, SpriteAt("99e9139737128f1dad9e7594efca89cd.png"));
            SetRect(felixButton.gameObject, new Vector2(115f, 65f), new Vector2(180f, 62f));
        }

        private static void BuildBackpack(Transform parent)
        {
            var backpack = CreateImage("Backpack", parent, SpriteAt("0279c9ca8fb19c361fc1ea9d0bbe38dc.png"));
            SetRect(backpack.gameObject, new Vector2(1760f, 655f), new Vector2(245f, 610f));
            var items = new[]
            {
                "ef4ef1e44d04c0de589030943ed860c0.png",
                "68b9241b1a98c7e437c479b5dbdb8708.png",
                "7b3e3cb3b96e979afe5967fb922d3d24.png",
                "fa9d740f35fefba423a6a6786c2a2b29.png",
                "e0ac2d064b4fb346d5749f080aeebe0f.png"
            };
            for (int i = 0; i < items.Length; i++)
            {
                var item = CreateImage("Item" + (i + 1), backpack.transform, SpriteAt(items[i]));
                SetRect(item.gameObject, new Vector2(0f, 205f - i * 95f), new Vector2(105f, 74f));
            }
        }

        private static void BuildAngerBar(Transform parent, TMP_FontAsset font, HomeownerAngerSystem anger)
        {
            var bar = CreateImage("AngerBar", parent, SpriteAt("1a7faf8d0ba61dfffecb0502e2896240.png"));
            SetRect(bar.gameObject, new Vector2(950f, 115f), new Vector2(1280f, 185f));
            var fillObject = new GameObject("AngerFill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(bar.transform, false);
            var fill = fillObject.GetComponent<Image>();
            fill.color = new Color(1f, 0.28f, 0.04f, 0.38f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            SetRect(fillObject, new Vector2(190f, 28f), new Vector2(785f, 47f));
            var pointer = CreateImage("AngerPointer", bar.transform, SpriteAt("8f859a6ccdeb9510d5d70cf51c95b59c.png"));
            SetRect(pointer.gameObject, new Vector2(-185f, -2f), new Vector2(55f, 55f));
            var value = CreateText("AngerValue", bar.transform, font, "0 / 100", 22, TextAlignmentOptions.Center);
            SetRect(value.gameObject, new Vector2(190f, -30f), new Vector2(210f, 32f));
            var portrait = CreateImage("DukeStagePortrait", parent, SpriteAt("42ea7807b0e38c4dcc0ff0e48e07fa05.png"));
            SetRect(portrait.gameObject, new Vector2(1760f, 150f), new Vector2(180f, 180f));
            var stage = CreateText("AngerStage", parent, font, "烦躁 (0-30%)", 22, TextAlignmentOptions.Center);
            stage.color = new Color(0.16f, 0.09f, 0.04f);
            SetRect(stage.gameObject, new Vector2(1760f, 42f), new Vector2(230f, 38f));
            var ui = bar.gameObject.AddComponent<HomeownerAngerUI>();
            ui.ConfigureVisuals(anger, null, fill, value, portrait, stage,
                SpriteAt("42ea7807b0e38c4dcc0ff0e48e07fa05.png"),
                SpriteAt("959fdfa322ef9dc7c374b740fde27cd7.png"),
                SpriteAt("bc40ca55787f8e580025691b1a32fa8d.png"),
                SpriteAt("1fe00f5233ec529b7a1b9aeeba230171.png"));
        }

        private static void BuildControlButtons(Transform parent)
        {
            var play = CreateImage("PlayButton", parent, SpriteAt("f330d0c93a98e3c15d47973e0932c13d.png"));
            SetRect(play.gameObject, new Vector2(1510f, 1010f), new Vector2(62f, 62f));
            var settings = CreateImage("SettingsButton", parent, SpriteAt("02fdc73f38ad6735b2b031982bfeebbb.png"));
            SetRect(settings.gameObject, new Vector2(1585f, 1010f), new Vector2(62f, 62f));
            var sound = CreateImage("SoundButton", parent, SpriteAt("282d0e27e4522d58865975c53e77986e.png"));
            SetRect(sound.gameObject, new Vector2(1660f, 1010f), new Vector2(62f, 62f));
            var pause = CreateImage("PauseButton", parent, SpriteAt("16572d0127d446d9b80bbd9c98bdfe05.png"));
            SetRect(pause.gameObject, new Vector2(1735f, 1010f), new Vector2(62f, 62f));
            var exit = CreateImage("ExitButton", parent, SpriteAt("599ac49273521ed78729859cf23c42d7.png"));
            SetRect(exit.gameObject, new Vector2(1810f, 1010f), new Vector2(62f, 62f));
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font, string text,
            float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            return label;
        }

        private static void SetRect(GameObject go, Vector2 position, Vector2 size)
        {
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static Sprite SpriteAt(string fileName)
        {
            string path = HudFolder + "/" + fileName;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning("[GameplayProgress] HUD 素材未找到: " + path);
            return sprite;
        }

        private static void ConfigureHudAssets()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { HudFolder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                bool changed = importer.textureType != TextureImporterType.Sprite
                               || importer.spriteImportMode != SpriteImportMode.Single
                               || !importer.alphaIsTransparency;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                if (changed) importer.SaveAndReimport();
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void EnsureFirstLevelInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(item => item.path == FirstLevelPath))
            {
                scenes.Add(new EditorBuildSettingsScene(FirstLevelPath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }

        private static void RunSelfTest(LevelConfig level01, LevelConfig level02)
        {
            if (level01.tasks.Count != 3 || level01.timeLimitSeconds != 600f) Fail("Level01 配置错误");
            if (level02.tasks.Count != 3 || level02.timeLimitSeconds != 480f) Fail("Level02 配置错误");

            var go = new GameObject("GameplayProgressSelfTest");
            var anger = go.AddComponent<HomeownerAngerSystem>();
            anger.SetMaxAnger(100f);
            anger.ResetAnger();
            anger.AddAnger(30f);
            Check(Mathf.Approximately(anger.GetAnger(), 30f) && anger.GetCurrentStage() == AngerStage.Calm, "Anger 30 / Calm");
            anger.AddAnger(1f);
            Check(anger.GetCurrentStage() == AngerStage.Annoyed, "Anger 31 / Annoyed");
            anger.AddAnger(29f);
            Check(anger.GetCurrentStage() == AngerStage.Annoyed, "Anger 60 / Annoyed");
            anger.AddAnger(1f);
            Check(anger.GetCurrentStage() == AngerStage.Angry, "Anger 61 / Angry");
            anger.AddAnger(29f);
            Check(anger.GetCurrentStage() == AngerStage.Angry, "Anger 90 / Angry");
            anger.AddAnger(1f);
            Check(anger.GetCurrentStage() == AngerStage.Furious, "Anger 91 / Furious");
            anger.AddAnger(100f);
            Check(Mathf.Approximately(anger.GetAnger(), 100f), "Anger 上限 100");
            anger.RemoveAnger(200f);
            Check(Mathf.Approximately(anger.GetAnger(), 0f) && !float.IsNaN(anger.GetAnger()), "Anger 下限 0");

            var tasks = go.AddComponent<LevelTaskSystem>();
            tasks.InitializeTasks(new[]
            {
                Task("a", "Task A", 1), Task("b", "Task B", 3), Task("c", "Task C", 1)
            });
            tasks.CompleteTask("a");
            tasks.AddTaskProgress("b");
            tasks.AddTaskProgress("b", 2);
            tasks.CompleteTask("c");
            Check(tasks.GetCompletedTaskCount() == 3 && tasks.GetTotalTaskCount() == 3, "任务完成数量 3 / 3");
            Check(tasks.AreAllTasksCompleted() && Mathf.Approximately(tasks.GetOverallProgress(), 1f), "任务总体进度 1.0");

            var countdown = go.AddComponent<CountdownSystem>();
            countdown.SetDuration(level02.timeLimitSeconds);
            Check(Mathf.Approximately(countdown.DurationSeconds, 480f) && Mathf.Approximately(countdown.RemainingSeconds, 480f), "倒计时切换 Level02 = 480 秒");
            UnityEngine.Object.DestroyImmediate(go);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) Fail(message);
            Debug.Log("[GameplayProgress] PASS " + message);
        }

        private static void Fail(string message)
        {
            throw new InvalidOperationException("[GameplayProgress] FAIL " + message);
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
