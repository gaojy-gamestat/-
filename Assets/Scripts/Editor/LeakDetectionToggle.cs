using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 一键开关 Unity 的 Native 泄漏追踪（Full StackTraces）。
    ///
    /// 默认只报告"A Native Collection has not been disposed"却不说是谁分配的，没法定位。
    /// 切到 EnabledWithStackTrace 后，警告会附带分配处的完整堆栈，才能看出是哪个子系统漏的。
    ///
    /// 之所以做成菜单而不是常开：堆栈追踪有额外开销，平时没必要背着。
    /// 选择状态存在 EditorPrefs 里，重启编辑器后仍然生效。
    /// </summary>
    [InitializeOnLoad]
    public static class LeakDetectionToggle
    {
        private const string MenuPath = "Tools/诊断/Native 泄漏追踪（Full StackTraces）";
        private const string EnabledKey = "GameNet.NativeLeakStackTrace";
        private const string StatusPath = "Tools/诊断/打印当前泄漏追踪状态";

        static LeakDetectionToggle()
        {
            // 域重载后 NativeLeakDetection.Mode 会重置，这里恢复上次的选择。
            SetEnabled(EditorPrefs.GetBool(EnabledKey, false), silent: true);
        }

        [MenuItem(MenuPath, false, 100)]
        private static void Toggle()
        {
            SetEnabled(!EditorPrefs.GetBool(EnabledKey, false), silent: false);
        }

        [MenuItem(MenuPath, true, 100)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(EnabledKey, false));
            return true;
        }

        [MenuItem(StatusPath, false, 101)]
        private static void PrintStatus()
        {
            Debug.Log($"[诊断] NativeLeakDetection.Mode = {NativeLeakDetection.Mode}（Full StackTraces = {EditorPrefs.GetBool(EnabledKey, false)}）");
        }

        private static void SetEnabled(bool enabled, bool silent)
        {
            NativeLeakDetection.Mode = enabled
                ? NativeLeakDetectionMode.EnabledWithStackTrace
                : NativeLeakDetectionMode.Enabled;

            EditorPrefs.SetBool(EnabledKey, enabled);

            if (silent)
            {
                return;
            }

            Debug.Log(enabled
                ? "[诊断] 已开启 Native 泄漏追踪（Full StackTraces）。现在退出播放模式，泄漏警告会带上分配堆栈，把那段堆栈发来即可定位。"
                : "[诊断] 已关闭堆栈追踪，恢复为仅报告泄漏。");
        }
    }
}
