using UnityEngine;
using UnityEngine.UI;

namespace GameNet.UI
{
    /// <summary>
    /// 挂到共享设置面板实例上（由 SettingsPanelLauncher 自动添加）。
    /// 负责把面板里的三根音量条接到 GameAudioSettings 上：
    /// 在哪个场景打开这个面板，改的都是同一份音量。
    /// </summary>
    public sealed class SettingsPanelRuntime : MonoBehaviour
    {
        private Slider m_Master;
        private Slider m_Music;
        private Slider m_Sfx;
        private bool m_Bound;

        /// <summary>接线。返回 false 表示这个面板缺少音量条（调用方会换用简易面板）。</summary>
        public bool Bind()
        {
            if (m_Bound) return m_Master != null && m_Music != null && m_Sfx != null;
            m_Bound = true;

            m_Master = FindSlider("Slider_Master");
            m_Music = FindSlider("Slider_Music");
            m_Sfx = FindSlider("Slider_SFX");

            if (m_Master != null) m_Master.onValueChanged.AddListener(GameAudioSettings.SetMaster);
            if (m_Music != null) m_Music.onValueChanged.AddListener(GameAudioSettings.SetMusic);
            if (m_Sfx != null) m_Sfx.onValueChanged.AddListener(GameAudioSettings.SetSfx);

            Button close = FindButton("Btn_Close");
            if (close != null) close.onClick.AddListener(Hide);

            // 文字层（标题、三行标签）默认 raycastTarget=1，会盖在滑条上抢走拖动事件。
            // 只关掉文字的射线目标；图片（滑条轨道/把手、按钮底图、面板遮罩）全部保留。
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (!(graphic is Image)) graphic.raycastTarget = false;
            }
            if (m_Master != null) m_Master.interactable = true;
            if (m_Music != null) m_Music.interactable = true;
            if (m_Sfx != null) m_Sfx.interactable = true;

            GameAudioSettings.Changed += Refresh;
            Refresh();

            return m_Master != null && m_Music != null && m_Sfx != null;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();

            // 不冻结 Time.timeScale：结算面板本来就会把它设为 0，
            // 这里再改一次会把「恢复时间」的值记错，导致回到关卡后画面一直卡住。
            // 调音量时是否暂停由游戏本身决定，设置面板不插手。
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Hide();
        }

        private void OnDestroy()
        {
            GameAudioSettings.Changed -= Refresh;
        }

        private void Refresh()
        {
            // SetValueWithoutNotify：避免回写时把监听器再触发一遍造成抖动
            if (m_Master != null) m_Master.SetValueWithoutNotify(GameAudioSettings.Master);
            if (m_Music != null) m_Music.SetValueWithoutNotify(GameAudioSettings.Music);
            if (m_Sfx != null) m_Sfx.SetValueWithoutNotify(GameAudioSettings.Sfx);
        }

        private Slider FindSlider(string objectName)
        {
            foreach (Slider slider in GetComponentsInChildren<Slider>(true))
            {
                if (slider.name == objectName) return slider;
            }

            Debug.LogWarning("[Settings] 面板里找不到 " + objectName);
            return null;
        }

        private Button FindButton(string objectName)
        {
            foreach (Button button in GetComponentsInChildren<Button>(true))
            {
                if (button.name == objectName) return button;
            }

            return null;
        }
    }
}
