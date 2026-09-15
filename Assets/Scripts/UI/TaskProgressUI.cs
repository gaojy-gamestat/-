using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameNet.Gameplay;

namespace GameNet.UI
{
    /// <summary>任务表现适配器。任务行引用可为空，HUD 尚未完成时不会抛空引用。</summary>
    public sealed class TaskProgressUI : MonoBehaviour
    {
        [SerializeField] private LevelTaskSystem source;
        [SerializeField] private Slider overallSlider;
        [SerializeField] private Image overallFillImage;
        [SerializeField] private TMP_Text overallText;
        [SerializeField] private TMP_Text[] taskDescriptionTexts;
        [SerializeField] private TMP_Text[] taskProgressTexts;
        [SerializeField] private TMP_Text[] taskStatusTexts;

        public void Configure(LevelTaskSystem taskSource, Slider progressSlider, Image progressFill,
            TMP_Text progressText, TMP_Text[] descriptions, TMP_Text[] progressTexts, TMP_Text[] statuses)
        {
            source = taskSource;
            overallSlider = progressSlider;
            overallFillImage = progressFill;
            overallText = progressText;
            taskDescriptionTexts = descriptions;
            taskProgressTexts = progressTexts;
            taskStatusTexts = statuses;
        }

        private void OnEnable()
        {
            if (source == null) source = FindObjectOfType<LevelTaskSystem>();
            if (source == null) return;
            source.OnTaskProgressChanged += HandleTaskChanged;
            source.OnOverallProgressChanged += HandleOverallChanged;
            RefreshAll();
        }

        private void OnDisable()
        {
            if (source == null) return;
            source.OnTaskProgressChanged -= HandleTaskChanged;
            source.OnOverallProgressChanged -= HandleOverallChanged;
        }

        private void RefreshAll()
        {
            HandleOverallChanged(source.GetOverallProgress());
            var tasks = source.GetAllTasks();
            for (int i = 0; i < tasks.Count; i++) HandleTaskChanged(tasks[i]);
        }

        private void HandleOverallChanged(float normalized)
        {
            int completed = source != null ? source.GetCompletedTaskCount() : 0;
            int total = source != null ? source.GetTotalTaskCount() : 0;
            if (overallSlider != null) overallSlider.value = normalized;
            if (overallFillImage != null) overallFillImage.fillAmount = normalized;
            if (overallText != null) overallText.text = $"{completed} / {total}";
        }

        private void HandleTaskChanged(LevelTaskData task)
        {
            if (source == null || task == null) return;
            int index = source.GetAllTasks().FindIndex(item => item.taskId == task.taskId);
            if (index < 0) return;
            if (taskDescriptionTexts != null && index < taskDescriptionTexts.Length && taskDescriptionTexts[index] != null)
                taskDescriptionTexts[index].text = task.description;
            if (taskProgressTexts != null && index < taskProgressTexts.Length && taskProgressTexts[index] != null)
                taskProgressTexts[index].text = $"{task.currentProgress} / {task.targetProgress}";
            if (taskStatusTexts != null && index < taskStatusTexts.Length && taskStatusTexts[index] != null)
                taskStatusTexts[index].text = task.IsCompleted ? "✓" : "";
        }
    }
}
