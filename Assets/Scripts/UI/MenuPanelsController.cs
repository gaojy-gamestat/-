using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 主菜单扩展面板控制器：设置（三个音量条）/ 制作人员 / 退出游戏。
/// 独立脚本，不改动队友的 MainMenuUIController，避免合并冲突。
///
/// 用法：
/// 1. 挂到主菜单 Canvas 上；
/// 2. 把 设置 / 制作人员 / 退出游戏 三个按钮和两个面板拖进对应槽位；
/// 3. 右上角的黑叉关闭按钮会在运行时自动生成；
///    如果想用自己的美术叉，在 Window 下建一个名为 Btn_Close 的按钮即可，脚本会优先使用它。
/// </summary>
public class MenuPanelsController : MonoBehaviour
{
    [Header("主菜单按钮")]
    public Button Btn_Settings;
    public Button Btn_Credits;
    public Button Btn_Quit;

    [Header("面板（整个面板拖进来）")]
    public GameObject Panel_Settings;
    public GameObject Panel_Credits;

    [Header("设置面板的三个音量条")]
    public Slider Slider_Master;
    public Slider Slider_Music;
    public Slider Slider_SFX;

    [Header("可选：AudioMixer（现在没有可以不填）")]
    public AudioMixer Mixer;
    public string MusicParam = "MusicVol";
    public string SFXParam = "SFXVol";

    private const string KeyMaster = "Vol_Master";
    private const string KeyMusic = "Vol_Music";
    private const string KeySFX = "Vol_SFX";

    private void Awake()
    {
        // 绑定三个主菜单按钮
        if (Btn_Settings != null) Btn_Settings.onClick.AddListener(OnClickSettings);
        if (Btn_Credits != null) Btn_Credits.onClick.AddListener(OnClickCredits);
        if (Btn_Quit != null) Btn_Quit.onClick.AddListener(OnClickQuit);

        // 两个面板的右上角黑叉（自动生成或使用自带的 Btn_Close）
        EnsureCloseButton(Panel_Settings);
        EnsureCloseButton(Panel_Credits);

        // 三个音量条：读取上次保存的值并生效
        BindSlider(Slider_Master, KeyMaster, OnMasterChanged);
        BindSlider(Slider_Music, KeyMusic, OnMusicChanged);
        BindSlider(Slider_SFX, KeySFX, OnSFXChanged);

        // 默认隐藏两个面板
        if (Panel_Settings != null) Panel_Settings.SetActive(false);
        if (Panel_Credits != null) Panel_Credits.SetActive(false);
    }

    // ------------------------------------------------------------------
    // 面板开关
    // ------------------------------------------------------------------
    public void OnClickSettings()
    {
        if (Panel_Credits != null) Panel_Credits.SetActive(false);
        if (Panel_Settings != null) Panel_Settings.SetActive(true);
    }

    public void OnClickCredits()
    {
        if (Panel_Settings != null) Panel_Settings.SetActive(false);
        if (Panel_Credits != null) Panel_Credits.SetActive(true);
    }

    public void OnClickQuit()
    {
        Debug.Log("[UI] 点击 退出游戏");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ------------------------------------------------------------------
    // 右上角黑叉关闭按钮
    // ------------------------------------------------------------------
    private void EnsureCloseButton(GameObject panel)
    {
        if (panel == null) return;

        // 优先挂在 Window（小窗口）上；没有 Window 就挂在整个面板右上角
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

        // 浅色底：面板窗口多是深色，浅底才能让黑色叉号看得清
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.92f);

        // 黑叉 = 两根旋转 45 度的黑色横条（不依赖字体）
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
    // 音量调节
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
        AudioListener.volume = value; // 全局音量：立刻生效
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
