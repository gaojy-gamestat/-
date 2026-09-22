using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 游戏关卡内的小 UI 控制器：右上角 设置 / 离开 两个按钮。
/// 独立脚本，不改动主菜单的 MenuPanelsController，避免合并冲突。
///
/// 功能：
/// 1. 设置按钮：弹出设置面板（三个音量条）。
///    音量数值存 PlayerPrefs（与主菜单共用同一组 key），
///    每个场景只要挂了本脚本（或 MenuPanelsController），启动时都会读回并生效，
///    因此在任意场景调过的音量，切场景后依然保持，直到下次调整。
/// 2. 离开按钮：弹出确认面板（确认退出此关卡？），
///    点「确认」加载 ExitSceneName 指定的场景（默认 关卡选择），点「取消」关闭面板。
/// 3. 自动补齐 Canvas 缺失的 GraphicRaycaster / CanvasScaler / EventSystem（第一关场景缺这些）。
///
/// 用法：挂到关卡场景的 Canvas 上，把按钮和面板拖进槽位。
/// </summary>
public class GameplayMenuController : MonoBehaviour
{
    [Header("右上角两个小按钮")]
    public Button Btn_Settings;
    public Button Btn_Exit;

    [Header("设置面板 + 三个音量条（可从主菜单场景复制过来）")]
    public GameObject Panel_Settings;
    public Slider Slider_Master;
    public Slider Slider_Music;
    public Slider Slider_SFX;

    [Header("退出确认面板")]
    public GameObject Panel_QuitConfirm;
    public Button Btn_Confirm;
    public Button Btn_Cancel;

    [Header("点「确认」后要回去的场景名")]
    public string ExitSceneName = "关卡选择";

    [Header("可选：AudioMixer（团队还没做就留空）")]
    public AudioMixer Mixer;
    public string MusicParam = "MusicVol";
    public string SFXParam = "SFXVol";

    // 与 MenuPanelsController 完全相同的存档 key，保证两个场景读写同一份数据
    private const string KeyMaster = "Vol_Master";
    private const string KeyMusic = "Vol_Music";
    private const string KeySFX = "Vol_SFX";

    private void Awake()
    {
        EnsureCanvasWorks();

        if (Btn_Settings != null) Btn_Settings.onClick.AddListener(OnClickSettings);
        if (Btn_Exit != null) Btn_Exit.onClick.AddListener(OnClickExit);
        if (Btn_Confirm != null) Btn_Confirm.onClick.AddListener(OnClickConfirmExit);
        if (Btn_Cancel != null) Btn_Cancel.onClick.AddListener(OnClickCancelExit);

        EnsureCloseButton(Panel_Settings);

        // 读回上次保存的音量并立刻生效 —— 这就是「跨场景保持」的关键
        BindSlider(Slider_Master, KeyMaster, OnMasterChanged);
        BindSlider(Slider_Music, KeyMusic, OnMusicChanged);
        BindSlider(Slider_SFX, KeySFX, OnSFXChanged);

        if (Panel_Settings != null) Panel_Settings.SetActive(false);
        if (Panel_QuitConfirm != null) Panel_QuitConfirm.SetActive(false);
    }

    // ------------------------------------------------------------------
    // 按钮行为
    // ------------------------------------------------------------------
    public void OnClickSettings()
    {
        if (Panel_Settings != null) Panel_Settings.SetActive(true);
    }

    public void OnClickExit()
    {
        if (Panel_QuitConfirm != null) Panel_QuitConfirm.SetActive(true);
    }

    public void OnClickConfirmExit()
    {
        Debug.Log("[UI] 确认退出关卡，返回 " + ExitSceneName);
        if (!SceneManager.GetSceneByName(ExitSceneName).isLoaded
            && SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/" + ExitSceneName + ".unity") < 0)
        {
            Debug.LogError("[UI] 场景 " + ExitSceneName + " 不在 Build Settings 里，跳转失败！" +
                           "请按 File → Build Settings → Add Open Scenes 添加。");
            return;
        }
        SceneManager.LoadScene(ExitSceneName);
    }

    public void OnClickCancelExit()
    {
        if (Panel_QuitConfirm != null) Panel_QuitConfirm.SetActive(false);
    }

    // ------------------------------------------------------------------
    // 自动补齐 Canvas / 事件系统（第一关场景缺 Raycaster 和 Scaler）
    // ------------------------------------------------------------------
    private void EnsureCanvasWorks()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        // 没有GraphicRaycaster，所有按钮都点不动
        if (canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        // 根画布补缩放器：按 1920x1080 设计稿等比缩放
        if (canvas.transform.parent == null && canvas.GetComponent<CanvasScaler>() == null)
        {
            CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.matchWidthOrHeight = 0.5f;
        }

        // 没有EventSystem，按钮同样点不动
        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    // ------------------------------------------------------------------
    // 设置面板右上角黑叉（与主菜单同款：自带 Btn_Close 则优先用）
    // ------------------------------------------------------------------
    private void EnsureCloseButton(GameObject panel)
    {
        if (panel == null) return;

        Transform holder = panel.transform.Find("Window");
        if (holder == null) holder = panel.transform;

        Transform existing = holder.Find("Btn_Close");
        Button btn = existing != null ? existing.GetComponent<Button>() : CreateCloseButton(holder);

        if (btn != null)
        {
            GameObject target = panel;
            btn.onClick.AddListener(() =>
            {
                if (target != null) target.SetActive(false);
            });
        }
    }

    private static Button CreateCloseButton(Transform parent)
    {
        GameObject root = new GameObject("Btn_Close", typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rt = root.GetComponent<RectTransform>();

        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-30f, -30f);
        rt.sizeDelta = new Vector2(48f, 48f);

        Image bg = root.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.92f);

        MakeCrossBar(rt, 45f);
        MakeCrossBar(rt, -45f);

        return root.GetComponent<Button>();
    }

    private static void MakeCrossBar(Transform parent, float angle)
    {
        GameObject bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        RectTransform rt = bar.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        rt.sizeDelta = new Vector2(30f, 4f);
        rt.localScale = Vector3.one;

        Image img = bar.GetComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
    }

    // ------------------------------------------------------------------
    // 音量：读写与主菜单同一组 PlayerPrefs
    // ------------------------------------------------------------------
    private void BindSlider(Slider slider, string key, UnityAction<float> callback)
    {
        if (slider == null) return;
        slider.value = PlayerPrefs.GetFloat(key, 1f);
        slider.onValueChanged.AddListener(callback);
        callback(slider.value);
    }

    private void OnMasterChanged(float value)
    {
        PlayerPrefs.SetFloat(KeyMaster, value);
        AudioListener.volume = value;
    }

    private void OnMusicChanged(float value)
    {
        PlayerPrefs.SetFloat(KeyMusic, value);
        if (Mixer != null) Mixer.SetFloat(MusicParam, ToDecibel(value));
    }

    private void OnSFXChanged(float value)
    {
        PlayerPrefs.SetFloat(KeySFX, value);
        if (Mixer != null) Mixer.SetFloat(SFXParam, ToDecibel(value));
    }

    private static float ToDecibel(float value)
    {
        return value <= 0.0001f ? -80f : Mathf.Log10(value) * 20f;
    }
}
