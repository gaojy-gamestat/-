using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameNet.UI
{
    /// <summary>
    /// 让每个场景里的「设置」按钮都弹出同一个共享设置面板。
    ///
    /// 做法：进入场景后自动找到名字里带「设置 / Settings」的按钮，接上同一条回调；
    /// 面板本身来自 Resources/UI/SettingsPanel.prefab（从主菜单那个面板原样抽出来的），
    /// 全游戏只保留一份 UI，音量数值也统一走 GameAudioSettings。
    ///
    /// 场景里不需要额外挂任何脚本，也不用改按钮的 OnClick。
    /// 主菜单场景自己已经有 MenuPanelsController，会自动跳过，不会绑两次。
    /// </summary>
    public static class SettingsPanelLauncher
    {
        private const string PrefabPath = "UI/SettingsPanel";

        private static readonly HashSet<int> s_BoundButtons = new HashSet<int>();
        private static SettingsPanelRuntime s_Panel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            GameAudioSettings.EnsureLoaded();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            BindScene(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BindScene(scene);
        }

        private static void BindScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;

            // 主菜单有自己的整套设置面板控制器，跳过，避免两套面板打架。
            if (Object.FindObjectOfType<MenuPanelsController>() != null) return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!IsSettingsObject(child.name)) continue;

                    // 场景里的「设置」有时候只是一张图（没有 Button 组件），这里补一个。
                    Button button = child.GetComponent<Button>();
                    if (button == null)
                    {
                        Graphic graphic = child.GetComponent<Graphic>();
                        if (graphic == null) continue;

                        graphic.raycastTarget = true;
                        button = child.gameObject.AddComponent<Button>();
                        button.targetGraphic = graphic;
                    }
                    else if (button.targetGraphic != null)
                    {
                        button.targetGraphic.raycastTarget = true;
                    }

                    if (!s_BoundButtons.Add(button.GetInstanceID())) continue;
                    button.onClick.AddListener(Open);

                    // 这个图标在场景里被倒计时面板盖住了，提到最前面才看得见、点得到。
                    child.SetAsLastSibling();
                    RectTransform rect = child as RectTransform;
                    Debug.Log("[Settings] 已把「" + child.name + "」接到设置面板；屏幕位置=" +
                              (rect != null ? rect.position.ToString() : "?") +
                              " 屏幕=" + Screen.width + "x" + Screen.height);
                }
            }
        }

        private static bool IsSettingsObject(string objectName)
        {
            return objectName.Contains("设置") || objectName.Contains("Settings") || objectName.Contains("settings");
        }

        /// <summary>打开共享设置面板（按钮回调，也可以被别的脚本直接调用）。</summary>
        public static void Open()
        {
            SettingsPanelRuntime panel = EnsurePanel();
            if (panel == null) return;
            panel.Show();
        }

        private static SettingsPanelRuntime EnsurePanel()
        {
            if (s_Panel != null) return s_Panel;

            Transform parent = EnsureCanvas();
            if (parent == null) return null;

            GameObject prefab = Resources.Load<GameObject>(PrefabPath);
            GameObject instance = null;

            if (prefab != null)
            {
                instance = Object.Instantiate(prefab, parent, false);
                instance.name = "SettingsPanel";
                instance.transform.SetAsLastSibling();
                instance.SetActive(true);

                SettingsPanelRuntime runtime = instance.GetComponent<SettingsPanelRuntime>();
                if (runtime == null) runtime = instance.AddComponent<SettingsPanelRuntime>();

                if (runtime.Bind())
                {
                    s_Panel = runtime;
                    return s_Panel;
                }

                Debug.LogWarning("[Settings] 美术设置面板里没有找到三根音量条，改用简易面板。");
                Object.Destroy(instance);
                instance = null;
            }
            else
            {
                Debug.LogWarning("[Settings] 没找到 Resources/" + PrefabPath +
                                 ".prefab，先用代码生成的简易设置面板顶上。");
            }

            if (instance == null)
            {
                instance = BuildFallbackPanel(parent);
                instance.name = "SettingsPanel";
                instance.transform.SetAsLastSibling();
                instance.SetActive(true);
            }

            s_Panel = instance.GetComponent<SettingsPanelRuntime>();
            if (s_Panel == null) s_Panel = instance.AddComponent<SettingsPanelRuntime>();
            s_Panel.Bind();
            return s_Panel;
        }

        /// <summary>美术面板缺失时的兜底：一个能用的极简设置面板（三根音量条）。</summary>
        private static GameObject BuildFallbackPanel(Transform parent)
        {
            GameObject root = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            Stretch(root);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            GameObject window = new GameObject("Window", typeof(RectTransform), typeof(Image));
            window.transform.SetParent(root.transform, false);
            RectTransform windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.sizeDelta = new Vector2(720f, 560f);
            windowRect.anchoredPosition = Vector2.zero;
            window.GetComponent<Image>().color = new Color(0.15f, 0.09f, 0.06f, 0.96f);

            TextMeshProUGUI title = CreateLabel(window.transform, "设 置", 60f);
            title.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -70f);
            title.rectTransform.sizeDelta = new Vector2(400f, 80f);

            string[] names = { "Slider_Master", "Slider_Music", "Slider_SFX" };
            string[] labels = { "主音量", "音乐", "音效" };
            for (int i = 0; i < names.Length; i++)
            {
                float y = 60f - i * 110f;

                TextMeshProUGUI label = CreateLabel(window.transform, labels[i], 36f);
                label.alignment = TextAlignmentOptions.Left;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                label.rectTransform.anchoredPosition = new Vector2(-230f, y);
                label.rectTransform.sizeDelta = new Vector2(180f, 50f);

                CreateSlider(window.transform, names[i], new Vector2(60f, y));
            }

            GameObject close = new GameObject("Btn_Close", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(window.transform, false);
            RectTransform closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-40f, -40f);
            closeRect.sizeDelta = new Vector2(64f, 64f);
            close.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f);

            TextMeshProUGUI cross = CreateLabel(close.transform, "×", 48f);
            Stretch(cross.gameObject);

            return root;
        }

        private static Slider CreateSlider(Transform parent, string objectName, Vector2 position)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(420f, 30f);
            rect.anchoredPosition = position;

            GameObject background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(go.transform, false);
            Stretch(background);
            background.GetComponent<Image>().color = new Color(0.28f, 0.22f, 0.19f, 1f);

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            Stretch(fillArea);

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            Stretch(fill);
            Image fillImage = fill.GetComponent<Image>();
            fillImage.color = new Color(0.95f, 0.6f, 0.15f, 1f);

            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(go.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(28f, 44f);
            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = Color.white;

            Slider slider = go.AddComponent<Slider>();
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            return slider;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private static void Stretch(GameObject go)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Transform EnsureCanvas()
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                EnsureEventSystem();
                return canvas.transform;
            }

            GameObject go = new GameObject("SettingsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();
            return go.transform;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }
}
