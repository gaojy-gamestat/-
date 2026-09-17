using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameNet.Gameplay;

namespace GameNet.UI
{
    /// <summary>倒计时表现适配器，只接收 CountdownSystem 的事件。</summary>
    public sealed class CountdownUI : MonoBehaviour
    {
        [SerializeField] private CountdownSystem source;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private Slider slider;

        public void Configure(CountdownSystem countdownSource, TMP_Text displayText, Slider progressSlider)
        {
            source = countdownSource;
            timeText = displayText;
            slider = progressSlider;
        }

        private void OnEnable()
        {
            if (source == null) source = FindObjectOfType<CountdownSystem>();
            if (source == null) return;
            source.OnTimeChanged += HandleTimeChanged;
            HandleTimeChanged(source.RemainingSeconds);
        }

        private void OnDisable()
        {
            if (source != null) source.OnTimeChanged -= HandleTimeChanged;
        }

        private void HandleTimeChanged(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            if (timeText != null) timeText.text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
            if (slider != null && source != null)
                slider.value = source.DurationSeconds <= 0f ? 0f : Mathf.Clamp01(seconds / source.DurationSeconds);
        }
    }
}
