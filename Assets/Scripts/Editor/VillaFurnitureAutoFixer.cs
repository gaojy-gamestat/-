using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameNet.Gameplay.EditorTools
{
    /// <summary>
    /// 家具自动修复守护。
    ///
    /// 为什么需要它：
    ///   家具流程里最容易踩的坑有三个，都会导致“明明做了却没效果”：
    ///     1) 忘了点菜单；
    ///     2) 点完忘了 Ctrl+S；
    ///     3) 在 Play 模式里操作 —— Unity 退出 Play 时会把改动全部还原，等于白做。
    ///   这个守护把这三步全自动化：脚本编译完 / 打开场景时，自动检查并修正家具，
    ///   修完自动保存场景。整个过程不用你动任何手。
    ///
    /// 安全设计：
    ///   只在家具「尺寸或位置明显不对」时才动手。如果你已经手动把碰撞框拖好了，
    ///   说明家具本来就正常，守护会直接跳过，绝不会覆盖你的手工调整。
    /// </summary>
    [InitializeOnLoad]
    public static class VillaFurnitureAutoFixer
    {
        private const string PrefKey = "GameNet.Villa.AutoFix.Enabled";

        static VillaFurnitureAutoFixer()
        {
            // delayCall：等 Unity 编译完脚本、场景就绪后再跑。
            // 不能在静态构造函数里直接访问场景 API，那时 Unity 还没准备好。
            EditorApplication.delayCall += TryRun;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            EditorApplication.delayCall += TryRun;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // 刚退出 Play 模式：Unity 已经把运行期间的改动全部还原了，
            // 这时候再自动修一次，正好补上「在 Play 模式里点工具 = 白做」的情况。
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += TryRun;
            }
        }

        private static void TryRun()
        {
            if (!Enabled) return;

            // Play 模式里改场景是白做（退出时会被还原），等退出后自动再修一次
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid()) return;

            // 静默执行：家具正常就啥也不做，不正常才修 + 自动保存
            VillaFurnitureSetupTool.AllInOneAutoSilent();
        }

        private static bool Enabled
        {
            get { return EditorPrefs.GetBool(PrefKey, true); }
            set { EditorPrefs.SetBool(PrefKey, value); }
        }

        // ------------------------------------------------------------------
        // 菜单：手动开关 / 立即跑一次
        // ------------------------------------------------------------------

        private const string MenuRoot = "Tools/心灵驾到/";

        [MenuItem(MenuRoot + "家具：⑦ 自动修复守护（开启/关闭）", false, 20)]
        public static void Toggle()
        {
            Enabled = !Enabled;
            Debug.Log("[家具工具] 自动修复守护已" + (Enabled ? "开启" : "关闭") +
                      "。开启时：每次脚本编译完 / 打开场景，会自动检查家具并修正、自动保存。");
        }

        [MenuItem(MenuRoot + "家具：⑦ 自动修复守护（开启/关闭）", true)]
        public static bool ToggleValidate()
        {
            // 让菜单项显示当前状态
            Menu.SetChecked(MenuRoot + "家具：⑦ 自动修复守护（开启/关闭）", Enabled);
            return true;
        }

        [MenuItem(MenuRoot + "家具：⑧ 立即检查并修复（不弹窗）", false, 21)]
        public static void RunNow()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[家具工具] 当前处于 Play 模式，改动会被还原。请先停止运行再执行。");
                return;
            }

            VillaFurnitureSetupTool.AllInOneAutoSilent();
        }
    }
}
