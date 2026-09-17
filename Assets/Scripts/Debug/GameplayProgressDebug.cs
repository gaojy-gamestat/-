using UnityEngine;
using GameNet.Gameplay;

namespace GameNet.Debugging
{
    /// <summary>仅开发测试用：ContextMenu 或键盘 1/2/3/4/5/6 触发正式系统接口。</summary>
    public sealed class GameplayProgressDebug : MonoBehaviour
    {
        // Development-only hooks for validating the real gameplay systems.
        [SerializeField] private HomeownerAngerSystem anger;
        [SerializeField] private LevelTaskSystem tasks;
        [SerializeField] private string task1Id = "find_key";
        [SerializeField] private string task2Id = "prank";
        [SerializeField] private bool enableKeyboardShortcuts = true;

        private void Awake()
        {
            anger = anger != null ? anger : FindObjectOfType<HomeownerAngerSystem>();
            tasks = tasks != null ? tasks : FindObjectOfType<LevelTaskSystem>();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            if (!enableKeyboardShortcuts) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Add10Anger();
            if (Input.GetKeyDown(KeyCode.Alpha2)) Add25Anger();
            if (Input.GetKeyDown(KeyCode.Alpha3)) Remove10Anger();
            if (Input.GetKeyDown(KeyCode.Alpha4)) CompleteTask1();
            if (Input.GetKeyDown(KeyCode.Alpha5)) AddTask2Progress();
            if (Input.GetKeyDown(KeyCode.Alpha6)) ResetProgress();
        }
#endif

        [ContextMenu("+10 Anger")] public void Add10Anger() => anger?.AddAnger(10f);
        [ContextMenu("+25 Anger")] public void Add25Anger() => anger?.AddAnger(25f);
        [ContextMenu("-10 Anger")] public void Remove10Anger() => anger?.RemoveAnger(10f);
        [ContextMenu("Complete Task 1")] public void CompleteTask1() => tasks?.CompleteTask(task1Id);
        [ContextMenu("Add Progress Task 2")] public void AddTask2Progress() => tasks?.AddTaskProgress(task2Id);
        [ContextMenu("Reset Tasks + Anger")] public void ResetProgress()
        {
            anger?.ResetAnger();
            tasks?.ResetTasks();
        }
    }
}
