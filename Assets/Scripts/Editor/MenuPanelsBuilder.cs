using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 主菜单面板搭建工具（只在 Unity 编辑器里生效，不会影响打包后的游戏）。
///
/// 菜单栏 → 主菜单面板：
///   ① 一键生成 设置 + 制作人员 面板   —— 自动建好两个面板、三个音量条、右上角黑叉，并自动接线到 Canvas
///   ② 让选中对象铺满全屏              —— 修复锚点（Anchors 全屏拉伸）
///   ③ 检查面板接线状态                —— 在 Console 打印各槽位是否已赋值
///
/// 说明：生成的 Btn_Close（黑叉）由 MenuPanelsController 在运行时自动绑定关闭事件。
/// </summary>
public static class MenuPanelsBuilder
{
    private const string MenuRoot = "主菜单面板/";

    private static readonly Color MaskColor = new Color(0f, 0f, 0f, 0.63f);
    private static readonly Color WindowColor = new Color(0.07f, 0.08f, 0.11f, 0.98f);
    private static readonly Color TitleColor = new Color(0.95f, 0.96f, 1f, 1f);
    private static readonly Color LabelColor = new Color(0.82f, 0.86f, 0.95f, 1f);
    private static readonly Color HintColor = new Color(0.58f, 0.62f, 0.72f, 1f);
    private static readonly Color SliderBgColor = new Color(0.16f, 0.17f, 0.22f, 1f);
    private static readonly Color SliderFillColor = new Color(0.30f, 0.62f, 1f, 1f);
    private static readonly Color HandleColor = new Color(1f, 1f, 1f, 1f);
    private static readonly Color CloseBgColor = new Color(1f, 1f, 1f, 0.92f);
    private static readonly Color CrossColor = new Color(0.05f, 0.05f, 0.05f, 1f);

    // ==================================================================
    // ① 一键生成
    // ==================================================================
    [MenuItem(MenuRoot + "① 一键生成 设置 + 制作人员 面板", false, 1)]
    public static void BuildAll()
    {
        Canvas canvas = FindCanvas();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("没找到 Canvas",
                "当前场景里没有 Canvas。\n\n请先打开主菜单场景（Assets/Scenes/主菜单.unity）再运行本工具。", "好");
            return;
        }

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("生成主菜单面板");

        EnsureCanvasScaler(canvas);

        GameObject settings = EnsurePanel(canvas.transform, "Panel_Settings");
        Slider master, music, sfx;
        BuildSettingsContent(settings, out master, out music, out sfx);

        GameObject credits = EnsurePanel(canvas.transform, "Panel_Credits");
        BuildCreditsContent(credits);

        MenuPanelsController controller = EnsureController(canvas.gameObject);
        WireReferences(controller, settings, credits, master, music, sfx);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveScene(canvas.gameObject.scene);
        Undo.CollapseUndoOperations(group);

        Debug.Log("[主菜单面板] 生成完成：Panel_Settings / Panel_Credits 已建好并自动接线到 Canvas 上的 MenuPanelsController。");
        EditorUtility.DisplayDialog("完成",
            "两个面板已生成，并自动接到 Canvas 上的 MenuPanelsController。\n\n" +
            "接下来直接按 Play：\n" +
            "· 设置 → 弹出三个音量条 + 右上角黑叉\n" +
            "· 制作人员 → 弹出名单 + 右上角黑叉\n" +
            "· 退出游戏 → 直接退出\n\n" +
            "制作人员名单请改 Panel_Credits → Window → Txt_Names 里的文字。", "好");
    }

    // ==================================================================
    // ② 铺满全屏
    // ==================================================================
    [MenuItem(MenuRoot + "② 让选中对象铺满全屏（修复锚点）", false, 20)]
    public static void StretchSelection()
    {
        Transform[] selection = Selection.transforms;
        int count = 0;

        foreach (Transform t in selection)
        {
            RectTransform rt = t as RectTransform;
            if (rt == null) continue;
            Undo.RecordObject(rt, "铺满全屏");
            ApplyStretch(rt);
            EditorUtility.SetDirty(rt);
            count++;
        }

        if (count == 0)
        {
            EditorUtility.DisplayDialog("没有选中 UI 对象",
                "请先在 Hierarchy 里点选要铺满全屏的那个对象（比如 Panel_Settings），再执行本菜单。", "好");
            return;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[主菜单面板] 已把 {count} 个对象设为全屏拉伸（Anchors 0,0 → 1,1，Pos 与宽高归零）。");
    }

    [MenuItem(MenuRoot + "② 让选中对象铺满全屏（修复锚点）", true)]
    private static bool ValidateStretch() => Selection.transforms.Length > 0;

    // ==================================================================
    // ③ 接线检查
    // ==================================================================
    [MenuItem(MenuRoot + "③ 检查面板接线状态", false, 40)]
    public static void CheckWiring()
    {
        Canvas canvas = FindCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("[主菜单面板] 当前场景没有 Canvas。");
            return;
        }

        MenuPanelsController c = canvas.GetComponent<MenuPanelsController>();
        if (c == null)
        {
            Debug.LogWarning("[主菜单面板] Canvas 上还没有挂 MenuPanelsController，请先执行“① 一键生成”。");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("[主菜单面板] 接线检查：");
        sb.AppendLine("Btn_Settings   : " + Describe(c.Btn_Settings));
        sb.AppendLine("Btn_Credits    : " + Describe(c.Btn_Credits));
        sb.AppendLine("Btn_Quit       : " + Describe(c.Btn_Quit));
        sb.AppendLine("Panel_Settings : " + Describe(c.Panel_Settings));
        sb.AppendLine("Panel_Credits  : " + Describe(c.Panel_Credits));
        sb.AppendLine("Slider_Master  : " + Describe(c.Slider_Master));
        sb.AppendLine("Slider_Music   : " + Describe(c.Slider_Music));
        sb.AppendLine("Slider_SFX     : " + Describe(c.Slider_SFX));
        sb.AppendLine("（Null = 还没拖/没生成，其余为已接好的对象名）");
        Debug.Log(sb.ToString());
    }

    // ==================================================================
    // Canvas 缩放修复：场景 Canvas 是 Screen Space - Camera 且没有 Scaler，
    // UI 会按真实像素排布，窗口比 1920 小时按钮就被切出屏幕。
    // 这里补上和 SafeMainMenuRuntime 一致的 1920x1080 ScaleWithScreenSize。
    // ==================================================================
    private static void EnsureCanvasScaler(Canvas canvas)
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = Undo.AddComponent<CanvasScaler>(canvas.gameObject);
        }

        Undo.RecordObject(scaler, "配置 Canvas 缩放");
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.matchWidthOrHeight = 0.5f;
        EditorUtility.SetDirty(scaler);
        Debug.Log("[主菜单面板] 已给 Canvas 补上 CanvasScaler（1920x1080，Expand），按钮不会再被屏幕边缘切掉。");
    }

    // ==================================================================
    // 面板骨架
    // ==================================================================
    private static GameObject EnsurePanel(Transform canvas, string panelName)
    {
        // 优先复用你手动建过的同名面板（哪怕它被放在别的父节点下）
        Transform existing = FindDeep(canvas, panelName);
        Image img;

        if (existing != null)
        {
            img = existing.GetComponent<Image>();
            if (img == null) img = Undo.AddComponent<Image>(existing.gameObject);
            img.transform.SetAsLastSibling(); // 保证画在主菜单按钮之上
        }
        else
        {
            img = EnsureImage(canvas, panelName);
            img.transform.SetAsLastSibling();
        }

        Undo.RecordObject(img, "配置面板");
        img.color = MaskColor;
        img.raycastTarget = true;
        ApplyStretch(img.rectTransform);
        EditorUtility.SetDirty(img);
        return img.gameObject;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeep(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static RectTransform EnsureWindow(Transform panel, string title, out Text titleText)
    {
        Image win = EnsureImage(panel, "Window");
        Undo.RecordObject(win, "配置窗口");
        win.color = WindowColor;
        win.raycastTarget = true;
        ApplyCentered(win.rectTransform, new Vector2(820f, 560f), Vector2.zero);
        EditorUtility.SetDirty(win);

        titleText = EnsureText(win.transform, "Txt_Title");
        ConfigureText(titleText, title, 44, TitleColor, TextAnchor.MiddleCenter);
        ApplyCentered(titleText.rectTransform, new Vector2(680f, 64f), new Vector2(0f, 200f));

        EnsureCloseButton(win.transform);
        return win.rectTransform;
    }

    private static void BuildSettingsContent(GameObject panel, out Slider master, out Slider music, out Slider sfx)
    {
        Text title;
        RectTransform win = EnsureWindow(panel.transform, "设置", out title);

        string[] labels = { "全局音量", "音乐音量", "音效音量" };
        string[] names = { "Slider_Master", "Slider_Music", "Slider_SFX" };
        string[] txtNames = { "Txt_Master", "Txt_Music", "Txt_SFX" };
        float[] ys = { 110f, 30f, -50f };

        Slider[] sliders = new Slider[3];
        for (int i = 0; i < 3; i++)
        {
            Text lab = EnsureText(win, txtNames[i]);
            ConfigureText(lab, labels[i], 28, LabelColor, TextAnchor.MiddleLeft);
            ApplyCentered(lab.rectTransform, new Vector2(300f, 40f), new Vector2(-230f, ys[i]));

            sliders[i] = EnsureSlider(win, names[i], new Vector2(120f, ys[i]));
        }

        Text hint = EnsureText(win, "Txt_Hint");
        ConfigureText(hint, "提示：音乐 / 音效需要接入 AudioMixer 才能分别控制，全局音量立即生效", 19, HintColor, TextAnchor.MiddleCenter);
        ApplyCentered(hint.rectTransform, new Vector2(740f, 40f), new Vector2(0f, -170f));

        master = sliders[0];
        music = sliders[1];
        sfx = sliders[2];
    }

    private static void BuildCreditsContent(GameObject panel)
    {
        Text title;
        RectTransform win = EnsureWindow(panel.transform, "制作人员", out title);

        Text names = EnsureText(win, "Txt_Names");
        ConfigureText(names,
            "—— 制作人员 ——\n\n" +
            "策划：xxx\n" +
            "程序：xxx\n" +
            "美术：xxx\n" +
            "音乐：xxx\n\n" +
            "感谢游玩！",
            26, LabelColor, TextAnchor.UpperCenter);
        names.lineSpacing = 1.15f;
        ApplyCentered(names.rectTransform, new Vector2(700f, 400f), new Vector2(0f, -40f));
    }

    // ==================================================================
    // 右上角黑叉
    // ==================================================================
    private static void EnsureCloseButton(Transform window)
    {
        GameObject go = EnsureChild(window, "Btn_Close");
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-46f, -46f);
        rt.sizeDelta = new Vector2(52f, 52f);
        rt.localScale = Vector3.one;

        Image bg = go.GetComponent<Image>();
        if (bg == null) bg = Undo.AddComponent<Image>(go);
        Undo.RecordObject(bg, "配置黑叉");
        bg.color = CloseBgColor;
        bg.raycastTarget = true;

        Button btn = go.GetComponent<Button>();
        if (btn == null) btn = Undo.AddComponent<Button>(go);
        btn.targetGraphic = bg;
        EditorUtility.SetDirty(btn);

        MakeBar(go.transform, "Bar1", 45f);
        MakeBar(go.transform, "Bar2", -45f);
        go.transform.SetAsLastSibling();
    }

    private static void MakeBar(Transform parent, string name, float angle)
    {
        Image bar = EnsureImage(parent, name);
        Undo.RecordObject(bar, "配置叉条");
        bar.color = CrossColor;
        bar.raycastTarget = false;

        RectTransform rt = bar.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(28f, 5f);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localEulerAngles = new Vector3(0f, 0f, angle);
    }

    // ==================================================================
    // 音量条
    // ==================================================================
    private static Slider EnsureSlider(Transform parent, string name, Vector2 pos)
    {
        GameObject go = EnsureChild(parent, name);
        Slider slider = go.GetComponent<Slider>();
        if (slider == null) slider = Undo.AddComponent<Slider>(go);
        ApplyCentered(go.GetComponent<RectTransform>(), new Vector2(420f, 40f), pos);

        // 背景槽
        Image bg = EnsureImage(go.transform, "Background");
        Undo.RecordObject(bg, "配置滑条");
        bg.color = SliderBgColor;
        RectTransform bgRt = bg.rectTransform;
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(1f, 0.5f);
        bgRt.pivot = new Vector2(0.5f, 0.5f);
        bgRt.sizeDelta = new Vector2(-20f, 16f);
        bgRt.anchoredPosition = Vector2.zero;

        // 已填充区域（Slider 会动态改它的 anchorMax.x）
        Image fillArea = EnsureImage(go.transform, "Fill Area");
        Undo.RecordObject(fillArea, "配置滑条");
        fillArea.color = new Color(0f, 0f, 0f, 0f);
        fillArea.raycastTarget = false;
        RectTransform faRt = fillArea.rectTransform;
        faRt.anchorMin = new Vector2(0f, 0.5f);
        faRt.anchorMax = new Vector2(1f, 0.5f);
        faRt.pivot = new Vector2(0.5f, 0.5f);
        faRt.sizeDelta = new Vector2(-20f, 16f);
        faRt.anchoredPosition = Vector2.zero;

        Image fill = EnsureImage(fillArea.transform, "Fill");
        Undo.RecordObject(fill, "配置滑条");
        fill.color = SliderFillColor;
        fill.raycastTarget = false;
        RectTransform fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.pivot = new Vector2(0.5f, 0.5f);
        fillRt.sizeDelta = Vector2.zero;
        fillRt.anchoredPosition = Vector2.zero;

        // 拖动手柄区域
        Image handleArea = EnsureImage(go.transform, "Handle Slide Area");
        Undo.RecordObject(handleArea, "配置滑条");
        handleArea.color = new Color(0f, 0f, 0f, 0f);
        handleArea.raycastTarget = false;
        RectTransform haRt = handleArea.rectTransform;
        haRt.anchorMin = new Vector2(0f, 0.5f);
        haRt.anchorMax = new Vector2(1f, 0.5f);
        haRt.pivot = new Vector2(0.5f, 0.5f);
        haRt.sizeDelta = new Vector2(-20f, 40f);
        haRt.anchoredPosition = Vector2.zero;

        Image handle = EnsureImage(handleArea.transform, "Handle");
        Undo.RecordObject(handle, "配置滑条");
        handle.color = HandleColor;
        handle.raycastTarget = true;
        RectTransform hRt = handle.rectTransform;
        hRt.anchorMin = new Vector2(0f, 0f);
        hRt.anchorMax = new Vector2(0f, 1f);
        hRt.pivot = new Vector2(0.5f, 0.5f);
        hRt.sizeDelta = new Vector2(28f, 0f);
        hRt.anchoredPosition = Vector2.zero;

        bg.transform.SetAsFirstSibling();

        slider.fillRect = fillRt;
        slider.handleRect = hRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.transition = Selectable.Transition.ColorTint;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = 1f;
        EditorUtility.SetDirty(slider);

        return slider;
    }

    // ==================================================================
    // 控制器接线
    // ==================================================================
    private static MenuPanelsController EnsureController(GameObject canvasObject)
    {
        MenuPanelsController c = canvasObject.GetComponent<MenuPanelsController>();
        if (c == null)
        {
            c = Undo.AddComponent<MenuPanelsController>(canvasObject);
        }
        return c;
    }

    private static void WireReferences(MenuPanelsController c, GameObject settings, GameObject credits,
        Slider master, Slider music, Slider sfx)
    {
        var so = new SerializedObject(c);
        SetRef(so, "Panel_Settings", settings);
        SetRef(so, "Panel_Credits", credits);
        SetRef(so, "Slider_Master", master);
        SetRef(so, "Slider_Music", music);
        SetRef(so, "Slider_SFX", sfx);

        Transform root = c.transform;
        SetRef(so, "Btn_Settings", FindButton(root, "设置", "Setting"));
        SetRef(so, "Btn_Credits", FindButton(root, "制作人员", "制作", "Credit"));
        SetRef(so, "Btn_Quit", FindButton(root, "退出", "Quit", "Exit"));

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(c);
    }

    private static Button FindButton(Transform root, params string[] keywords)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button b in buttons)
        {
            string n = b.gameObject.name;
            foreach (string k in keywords)
            {
                if (n.Contains(k)) return b;
            }
        }
        return null;
    }

    private static void SetRef(SerializedObject so, string propertyName, Object value)
    {
        SerializedProperty p = so.FindProperty(propertyName);
        if (p != null) p.objectReferenceValue = value;
    }

    private static string Describe(Object o)
    {
        return o == null ? "Null（未赋值）" : o.name;
    }

    // ==================================================================
    // 通用小工具
    // ==================================================================
    private static Canvas FindCanvas()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return null;

        Canvas fallback = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
            foreach (Canvas c in canvases)
            {
                if (c.isRootCanvas && c.renderMode != RenderMode.WorldSpace && c.gameObject.name == "Canvas")
                {
                    return c;
                }

                if (fallback == null && c.isRootCanvas && c.renderMode != RenderMode.WorldSpace)
                {
                    fallback = c;
                }
            }
        }
        return fallback;
    }

    private static GameObject EnsureChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "创建 " + name);
        go.layer = 5; // UI 层
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image EnsureImage(Transform parent, string name)
    {
        GameObject go = EnsureChild(parent, name);
        Image img = go.GetComponent<Image>();
        if (img == null) img = Undo.AddComponent<Image>(go);
        return img;
    }

    private static Text EnsureText(Transform parent, string name)
    {
        GameObject go = EnsureChild(parent, name);
        Text txt = go.GetComponent<Text>();
        if (txt == null) txt = Undo.AddComponent<Text>(go);
        return txt;
    }

    private static void ConfigureText(Text txt, string content, int size, Color color, TextAnchor anchor)
    {
        Undo.RecordObject(txt, "配置文字");
        txt.font = UiFont();
        txt.text = content;
        txt.fontSize = size;
        txt.color = color;
        txt.alignment = anchor;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.raycastTarget = false;
        EditorUtility.SetDirty(txt);
    }

    // 思源黑体（Noto Sans SC，SIL OFL 1.1，免费可商用、可随游戏发布）
    private const string CjkFontPath = "Assets/Fonts/NotoSansSC-Regular.ttf";

    private static Font UiFont()
    {
        // 优先用项目里的中文字体；内置 Arial / LegacyRuntime 不含汉字，会显示成方框。
        Font font = AssetDatabase.LoadAssetAtPath<Font>(CjkFontPath);
        if (font != null)
        {
            return font;
        }

        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { }
        if (font == null)
        {
            try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { }
        }
        return font;
    }

    private static void ApplyStretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    private static void ApplyCentered(RectTransform rt, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        rt.localScale = Vector3.one;
    }
}
