using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using GameNet.Gameplay;

namespace GameNet.UI
{
    /// <summary>
    /// 倒计时归零时的「时间到」结算面板。
    ///
    /// 场景里没挂 TimeUpPanelController 时，进关卡会自动创建一个临时面板：
    /// 时间到 → 暂停游戏 → 弹出「重新挑战 / 返回选关」。
    /// 如果以后美术做了正式面板，把 TimeUpPanelController 挂进场景，这里就会自动让位。
    /// </summary>
    public sealed class TimeUpPanelRuntime : MonoBehaviour
    {
        private const string SelectSceneName = "关卡选择";

        private CountdownSystem m_Source;
        private GameObject m_Panel;
        private bool m_Shown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryInstall();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryInstall();
        }

        private static void TryInstall()
        {
            // 已经有人手工接好正式面板了，就不用临时的。
            if (Object.FindObjectOfType<TimeUpPanelController>() != null) return;

            CountdownSystem source = Object.FindObjectOfType<CountdownSystem>(true);
            if (source == null) return;

            GameObject host = new GameObject("[TimeUpPanelRuntime]");
            TimeUpPanelRuntime runtime = host.AddComponent<TimeUpPanelRuntime>();
            runtime.Bind(source);
        }

        private void Bind(CountdownSystem source)
        {
            m_Source = source;
            m_Source.OnTimeUp += HandleTimeUp;
        }

        private void OnDestroy()
        {
            if (m_Source != null) m_Source.OnTimeUp -= HandleTimeUp;
            Time.timeScale = 1f;
        }

        private void HandleTimeUp()
        {
            if (m_Shown) return;
            m_Shown = true;

            if (m_Panel == null) m_Panel = BuildPanel();
            m_Panel.SetActive(true);
            Time.timeScale = 0f;
        }

        // ------------------------------------------------------------------
        // 临时面板（没有美术资源，用 TMP 默认字体搭一个能点的版本）
        // ------------------------------------------------------------------
        private GameObject BuildPanel()
        {
            GameObject canvasGo = new GameObject("TimeUpCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();

            // 半透明遮罩：挡住下面的点击
            GameObject dim = CreateUiObject("Dim", canvasGo.transform);
            Stretch(dim);
            Image dimImage = dim.AddComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0.65f);

            GameObject window = CreateUiObject("Window", canvasGo.transform);
            RectTransform windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);
            windowRect.sizeDelta = new Vector2(900f, 460f);
            windowRect.anchoredPosition = Vector2.zero;
            Image windowImage = window.AddComponent<Image>();
            windowImage.color = new Color(0.12f, 0.07f, 0.05f, 0.96f);

            TextMeshProUGUI title = CreateLabel(window.transform, "时间到！", 96f);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -70f);
            title.rectTransform.sizeDelta = new Vector2(-80f, 140f);

            TextMeshProUGUI hint = CreateLabel(window.transform, "这次没能在时间内完成，再来一次？", 36f);
            hint.color = new Color(0.85f, 0.82f, 0.78f, 1f);
            hint.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            hint.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 10f);
            hint.rectTransform.sizeDelta = new Vector2(-80f, 60f);

            CreateButton(window.transform, "重新挑战", new Vector2(-180f, -120f), OnRetry);
            CreateButton(window.transform, "返回选关", new Vector2(180f, -120f), OnBack);

            canvasGo.SetActive(false);
            return canvasGo;
        }

        private void OnRetry()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void OnBack()
        {
            Time.timeScale = 1f;
            if (Application.CanStreamedLevelBeLoaded(SelectSceneName)) SceneManager.LoadScene(SelectSceneName);
            else SceneManager.LoadScene(0);
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------
        private static GameObject CreateUiObject(string objectName, Transform parent)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(GameObject go)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize)
        {
            GameObject go = CreateUiObject("Label", parent);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private static void CreateButton(Transform parent, string text, Vector2 position, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = CreateUiObject("Btn_" + text, parent);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(300f, 92f);
            rect.anchoredPosition = position;

            Image image = go.AddComponent<Image>();
            image.color = new Color(0.95f, 0.6f, 0.15f, 1f);

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            TextMeshProUGUI label = CreateLabel(go.transform, text, 44f);
            Stretch(label.gameObject);
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }
}
