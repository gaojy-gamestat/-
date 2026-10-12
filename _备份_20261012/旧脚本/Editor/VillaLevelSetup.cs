using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 一键把 VillaLevelBootstrap 挂进第一关场景并保存。
    ///
    /// 用法：菜单栏 Tools/第一关/搭建正交斜俯视场景（自动打开第一关、
    /// 给 Main Camera 挂上 Bootstrap、保存场景）。之后进入 Play 即生效：
    /// 相机、别墅图平面、行走地面、Oscar 玩家、HUD 适配都会在运行时自动搭好。
    ///
    /// 该操作只往场景里加一个组件，其余一切由运行时脚本完成，
    /// 不污染场景文件、可随时移除该组件还原。
    /// </summary>
    public static class VillaLevelSetup
    {
        private const string SceneName = "第一关";

        [MenuItem("Tools/第一关/搭建正交斜俯视场景", false, 20)]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[VillaSetup] 播放模式下不能改场景，请先退出播放模式再执行。");
                return;
            }

            string scenePath = FindScenePath();
            if (string.IsNullOrEmpty(scenePath))
            {
                Debug.LogError($"[VillaSetup] 找不到场景 {SceneName}。");
                return;
            }

            var active = EditorSceneManager.GetActiveScene();
            if (active.path != scenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }

                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            // Bootstrap 挂在 Main Camera 上（相机常驻、生命周期合适）
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            if (cam.GetComponent<GameNet.Gameplay.VillaLevelBootstrap>() == null)
            {
                cam.gameObject.AddComponent<GameNet.Gameplay.VillaLevelBootstrap>();
            }
            else
            {
                Debug.Log("[VillaSetup] 第一关已挂载 VillaLevelBootstrap，无需重复添加。");
            }

            Selection.activeGameObject = cam.gameObject;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[VillaSetup] 完成：Bootstrap 已挂到第一关的 Main Camera 并保存。\n" +
                      "进入 Play 后自动生成：斜俯视正交相机(16:9)、别墅图平面、行走地面、WASD 操控的 Oscar。\n" +
                      "地板对齐微调：Play 中选中「行走地面」调整 floorRect。");
        }

        // ------------------------------------------------------------------
        // 参数写回：Play 中微调「行走地面」floorRect / Oscar 移速等，
        // 运行时物体退出 Play 就销毁，值会丢；本菜单把值捕获下来，
        // 退出 Play 回到编辑模式后自动写进 Main Camera 上持久化的 Bootstrap 组件并保存场景。
        // ------------------------------------------------------------------
        private const string CaptureKey = "Villa.LevelParamCapture";

        [MenuItem("Tools/第一关/把Play中调好的参数写回场景", false, 21)]
        public static void CaptureRuntimeParams()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[VillaSetup] 请先 Play，选中「行走地面」/「Oscar_玩家」把参数调好，再执行本菜单。");
                return;
            }

            var ground = Object.FindObjectOfType<GameNet.Gameplay.VillaGround>();
            var player = Object.FindObjectOfType<GameNet.Gameplay.OscarPlayerController>();
            if (ground == null || player == null)
            {
                Debug.LogWarning("[VillaSetup] 场景里没有运行中的 行走地面/Oscar_玩家（确定是在第一关 Play？）。");
                return;
            }

            // 顺序：floorRect(x,y,w,h) | moveSpeed | scale | spawn(x,y) | animationSpeed
            string data = string.Join(",",
                ground.FloorRect.x.ToString(CultureInfo.InvariantCulture),
                ground.FloorRect.y.ToString(CultureInfo.InvariantCulture),
                ground.FloorRect.width.ToString(CultureInfo.InvariantCulture),
                ground.FloorRect.height.ToString(CultureInfo.InvariantCulture),
                player.MoveSpeed.ToString(CultureInfo.InvariantCulture),
                player.PlayerScale.ToString(CultureInfo.InvariantCulture),
                player.SpawnImagePixel.x.ToString(CultureInfo.InvariantCulture),
                player.SpawnImagePixel.y.ToString(CultureInfo.InvariantCulture),
                player.AnimationSpeed.ToString(CultureInfo.InvariantCulture));
            EditorPrefs.SetString(CaptureKey, data);

            EditorApplication.playModeStateChanged -= ApplyCapturedOnExit;
            EditorApplication.playModeStateChanged += ApplyCapturedOnExit;

            Debug.Log("[VillaSetup] 已捕获当前运行参数。\n" +
                      "现在退出 Play——参数会自动写回 Main Camera 上的 VillaLevelBootstrap 并保存场景。");
        }

        /// <summary>退出 Play、回到编辑模式后执行：把捕获值写进真正的场景组件（持久化）</summary>
        private static void ApplyCapturedOnExit(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= ApplyCapturedOnExit;

            string data = EditorPrefs.GetString(CaptureKey, "");
            if (string.IsNullOrEmpty(data))
            {
                return;
            }

            string[] parts = data.Split(',');
            if (parts.Length != 9)
            {
                return;
            }

            var values = new float[9];
            for (int i = 0; i < 9; i++)
            {
                values[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
            }

            var boot = Object.FindObjectOfType<GameNet.Gameplay.VillaLevelBootstrap>();
            if (boot == null)
            {
                Debug.LogWarning("[VillaSetup] 当前场景里没有 VillaLevelBootstrap，写回中止（请停在第一关场景）。");
                return;
            }

            var so = new SerializedObject(boot);
            so.FindProperty("groundFloorRect").rectValue = new Rect(values[0], values[1], values[2], values[3]);
            so.FindProperty("playerMoveSpeed").floatValue = values[4];
            so.FindProperty("playerScale").floatValue = values[5];
            so.FindProperty("playerSpawnPixel").vector2Value = new Vector2(values[6], values[7]);
            so.FindProperty("playerAnimationSpeed").floatValue = values[8];
            so.ApplyModifiedProperties();

            var scene = boot.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorPrefs.DeleteKey(CaptureKey);

            Debug.Log("[VillaSetup] 已写回并保存场景：地板范围 / 移动速度 / 体型 / 出生点 / 动画速度 均已持久化。");
        }

        // ------------------------------------------------------------------
        // 挂载 VillaSceneCamera：只挂组件，【不动你的相机】。
        // 相机的位置/旋转/正交视域只会在进入 Play 后由该组件配置，
        // 编辑模式里你在 Inspector 摆的相机不会被任何脚本覆盖。
        // 别墅本体不动（仍在 Canvas 下），Play 时由 Bootstrap 临时搭建世界平面。
        // ------------------------------------------------------------------

        [MenuItem("Tools/第一关/挂载 VillaSceneCamera（仅 Play 时配置相机）", false, 22)]
        public static void MountSceneCamera()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[VillaSetup] 播放模式下不能改场景，请先退出播放模式再执行。");
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[VillaSetup] 场景里找不到 Main Camera。");
                return;
            }

            if (cam.GetComponent<GameNet.Gameplay.VillaSceneCamera>() != null)
            {
                Debug.Log("[VillaSetup] Main Camera 已挂载 VillaSceneCamera，无需重复添加。");
                return;
            }

            // 只挂组件：编辑模式不会触碰相机，进入 Play 后才由它接管姿态与正交视域
            Undo.AddComponent<GameNet.Gameplay.VillaSceneCamera>(cam.gameObject);

            EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);

            Debug.Log("[VillaSetup] 完成：VillaSceneCamera 已挂到 Main Camera（记得 Ctrl+S 保存场景）。\n" +
                      "编辑模式不会改动你的相机；进入 Play 时才由它把相机配置成斜俯视" +
                      "（正交、俯角30°、看向地面原点）。\n" +
                      "选中「行走区域」时，Scene 视图会对到相机视角，并直接显示你 Canvas 下的「别墅」图，" +
                      "辅助线就画在它上面。");
        }

        private static string FindScenePath()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/" + SceneName + ".unity"))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
