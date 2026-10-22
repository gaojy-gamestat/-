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
        private float m_PreviousTimeScale = 1f;

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

            GameAudioSettings.Changed += Refresh;
            Refresh();

            return m_Master != null && m_Music != null && m_Sfx != null;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();

            // 打开面板时暂停游戏世界，避免一边调音量一边还能操作角色；
            // 关掉面板时恢复原来的时间流速（比如「时间到」面板本来就是 0）。
            m_PreviousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            Time.timeScale = m_PreviousTimeScale;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Hide();
        }

        private void OnDestroy()
        {
            GameAudioSettings.Changed -= Refresh;

            // 只有面板正开着的时候才是我们暂停的，销毁时要把时间流速还回去。
            if (gameObject.activeSelf) Time.timeScale = m_PreviousTimeScale;
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
