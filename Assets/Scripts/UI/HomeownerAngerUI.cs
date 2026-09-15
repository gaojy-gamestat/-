using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameNet.Gameplay;

namespace GameNet.UI
{
    /// <summary>怒气表现适配器：只监听系统事件，不持有怒气数据。</summary>
    public sealed class HomeownerAngerUI : MonoBehaviour
    {
        [SerializeField] private HomeownerAngerSystem source;
        [SerializeField] private Slider slider;
        [SerializeField] private Image fillImage;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private Image stageImage;
        [SerializeField] private TMP_Text stageText;
        [SerializeField] private Sprite calmSprite;
        [SerializeField] private Sprite annoyedSprite;
        [SerializeField] private Sprite angrySprite;
        [SerializeField] private Sprite furiousSprite;

        public void ConfigureVisuals(HomeownerAngerSystem angerSource, Slider angerSlider, Image angerFill,
            TMP_Text angerValueText, Image portrait, TMP_Text label, Sprite calm, Sprite annoyed,
            Sprite angry, Sprite furious)
        {
            source = angerSource;
            slider = angerSlider;
            fillImage = angerFill;
            valueText = angerValueText;
            stageImage = portrait;
            stageText = label;
            calmSprite = calm;
            annoyedSprite = annoyed;
            angrySprite = angry;
            furiousSprite = furious;
        }

        private void OnEnable()
        {
            if (source == null) source = FindObjectOfType<HomeownerAngerSystem>();
            if (source == null) return;
            source.OnAngerChanged += HandleAngerChanged;
            source.OnAngerStageChanged += HandleAngerStageChanged;
            HandleAngerChanged(source.CurrentAnger, source.MaxAnger, source.GetNormalizedAnger());
        }

        private void OnDisable()
        {
            if (source == null) return;
            source.OnAngerChanged -= HandleAngerChanged;
            source.OnAngerStageChanged -= HandleAngerStageChanged;
        }

        private void HandleAngerChanged(float current, float max, float normalized)
        {
            if (slider != null) slider.value = normalized;
            if (fillImage != null) fillImage.fillAmount = normalized;
            if (valueText != null) valueText.text = $"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}";
            ApplyStage(source != null ? source.GetCurrentStage() : AngerStage.Calm);
        }

        private void HandleAngerStageChanged(AngerStage previous, AngerStage current)
        {
            ApplyStage(current);
        }

        private void ApplyStage(AngerStage stage)
        {
            if (stageImage != null)
            {
                stageImage.sprite = stage switch
                {
                    AngerStage.Annoyed => annoyedSprite,
                    AngerStage.Angry => angrySprite,
                    AngerStage.Furious => furiousSprite,
                    _ => calmSprite
                };
                stageImage.preserveAspect = true;
            }

            if (stageText != null)
            {
                stageText.text = stage switch
                {
                    AngerStage.Annoyed => "愤怒 (31-60%)",
                    AngerStage.Angry => "暴怒 (61-90%)",
                    AngerStage.Furious => "癫狂 (91-100%)",
                    _ => "烦躁 (0-30%)"
                };
            }
        }
    }
}
