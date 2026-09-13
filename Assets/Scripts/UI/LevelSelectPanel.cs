using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 关卡选择面板控制器：9 个关卡按钮共用一个面板，点谁就把谁的数据填进来。
/// 不需要为每个关卡各做一个面板 —— 面板只有一份，内容靠 Levels 数据切换。
///
/// 使用步骤：
///   1. 把本脚本挂在 Canvas/Panel 上（Panel = 那个公共面板，里面有 左页/右页/开始/关闭/简介）。
///   2. Panel Root 留空即可，默认就是脚本所在节点。
///   3. Level Buttons 可以手动拖入 9 个关卡按钮（顺序 = 第 1~9 关）；
///      留空时会自动从「关卡图」节点下按名字里的数字（1、2、3…）收集。
///   4. 开始 → Enter Button，关闭/返回 → Exit Button，左页/右页 → Prev/Next Button。
///   5. Levels 里依次填 9 组数据：关卡名 / 简介 / 预览图 / 要加载的场景名 sceneName
///      （sceneName 必须是 Build Settings 里已加入的场景名）。
///
/// 面板初始保持激活即可，Awake 会自动把它藏起来，点按钮才打开。
/// </summary>
public class LevelSelectPanel : MonoBehaviour
{
    [Header("关卡数据（下标 0 = 第 1 个按钮）")]
    [SerializeField]
    private LevelData[] levels = new LevelData[9];

    [Header("选关界面上的 9 个关卡按钮（按顺序拖入）")]
    [SerializeField]
    private Button[] levelButtons = new Button[9];

    [Header("面板根节点（留空则使用脚本所在节点）")]
    [SerializeField]
    private GameObject panelRoot;

    [Header("面板内容（旧版 UGUI Text 和 TextMeshPro 都可以拖）")]
    [SerializeField] private Graphic levelIndexText;     // Level 1
    [SerializeField] private Graphic englishTitleText;   // 英文关卡名
    [SerializeField] private Graphic chineseTitleText;   // 中文关卡名
    [SerializeField] private Image previewImage;         // 关卡预览图
    [SerializeField] private Graphic descriptionText;    // 关卡简介

    [Header("面板按钮")]
    [SerializeField] private Button enterButton;  // 开始：进入当前关卡
    [SerializeField] private Button exitButton;   // 返回：关闭面板
    [SerializeField] private Button prevButton;   // 左页：上一个关卡（可留空）
    [SerializeField] private Button nextButton;   // 右页：下一个关卡（可留空）

    [Header("选项")]
    [SerializeField] private bool closeWithEscape = true;

    private bool m_Initialized;
    private int m_CurrentIndex = -1;

    /// <summary>当前预览的关卡下标，-1 表示面板未打开。</summary>
    public int CurrentIndex => m_CurrentIndex;

    private void Awake()
    {
        Initialize();
    }

    private void Update()
    {
        if (!closeWithEscape || m_CurrentIndex < 0)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
        }
    }

    // ------------------------------------------------------------------
    // 对外接口（也用于按钮的 onClick 直接绑定）
    // ------------------------------------------------------------------

    /// <summary>打开面板并显示第 levelIndex 个关卡的简介（0 起）。</summary>
    public void Open(int levelIndex)
    {
        // 面板初始为 inactive 时 Awake 还没执行，这里补一次初始化。
        Initialize();

        if (levels == null || levelIndex < 0 || levelIndex >= levels.Length || levels[levelIndex] == null)
        {
            Debug.LogWarning($"[LevelSelect] 第 {levelIndex + 1} 个关卡还没有配置 LevelData，已忽略这次点击。");
            return;
        }

        m_CurrentIndex = levelIndex;
        ApplyLevelData(levelIndex, levels[levelIndex]);

        panelRoot.SetActive(true);
        Debug.Log($"[LevelSelect] 打开第 {levelIndex + 1} 关简介：{levels[levelIndex].chineseTitle}");
    }

    /// <summary>关闭简介面板，回到选关界面。</summary>
    public void Close()
    {
        m_CurrentIndex = -1;
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    /// <summary>进入当前预览的关卡场景。</summary>
    public void OnClickEnter()
    {
        if (levels == null || m_CurrentIndex < 0 || m_CurrentIndex >= levels.Length)
        {
            return;
        }

        LevelData data = levels[m_CurrentIndex];
        if (data == null)
        {
            return;
        }

        // 优先用场景名；没有填场景名时退回 Build Settings 里的序号。
        if (!string.IsNullOrEmpty(data.sceneName))
        {
            if (Application.CanStreamedLevelBeLoaded(data.sceneName))
            {
                Debug.Log($"[LevelSelect] 进入关卡场景：{data.sceneName}");
                SceneManager.LoadScene(data.sceneName);
            }
            else
            {
                Debug.LogError($"[LevelSelect] 场景 “{data.sceneName}” 没有加入 Build Settings，无法加载。" +
                               "请到 File → Build Settings 把该场景拖进列表。");
            }

            return;
        }

        int buildIndex = data.sceneBuildIndex;
        if (buildIndex > 0 && buildIndex < SceneManager.sceneCountInBuildSettings)
        {
            Debug.Log($"[LevelSelect] 进入关卡场景（Build Index {buildIndex}）");
            SceneManager.LoadScene(buildIndex);
        }
        else
        {
            Debug.LogError($"[LevelSelect] 第 {m_CurrentIndex + 1} 关既没有填 sceneName，" +
                           $"sceneBuildIndex={buildIndex} 也不是有效的 Build Settings 序号。" +
                           "请在面板上填好场景名，或把关卡场景加入 Build Settings 后填写序号。");
        }
    }

    /// <summary>左页：切到上一个关卡。</summary>
    public void OnClickPrev()
    {
        StepLevel(-1);
    }

    /// <summary>右页：切到下一个关卡。</summary>
    public void OnClickNext()
    {
        StepLevel(1);
    }

    private void StepLevel(int delta)
    {
        if (m_CurrentIndex < 0 || levels == null || levels.Length == 0)
        {
            return;
        }

        int count = levels.Length;
        int target = m_CurrentIndex;

        // 跳过没有配置数据的关卡，避免翻到空白页
        for (int i = 0; i < count; i++)
        {
            target = (target + delta + count) % count;
            if (levels[target] != null)
            {
                break;
            }
        }

        Open(target);
    }

    // ------------------------------------------------------------------
    // 内部实现
    // ------------------------------------------------------------------

    /// <summary>
    /// 9 个关卡按钮没在 Inspector 里手动拖时，自动从「关卡图」节点下按名字里的数字收集（1、2、3…）。
    /// 只要手动拖了任意一个，就以手动配置为准。
    /// </summary>
    private void EnsureLevelButtons()
    {
        if (levelButtons != null)
        {
            for (int i = 0; i < levelButtons.Length; i++)
            {
                if (levelButtons[i] != null)
                {
                    return;
                }
            }
        }

        Transform root = FindLevelButtonsRoot();
        if (root == null)
        {
            Debug.LogWarning("[LevelSelect] 没有手动拖入关卡按钮，也没找到名字叫「关卡图」的按钮容器。" +
                             "请在 Inspector 的 Level Buttons 里拖入 9 个关卡按钮。");
            return;
        }

        Button[] found = root.GetComponentsInChildren<Button>(true);
        if (found.Length == 0)
        {
            Debug.LogWarning($"[LevelSelect]「{root.name}」下面没有找到任何按钮。");
            return;
        }

        Array.Sort(found, (a, b) => LeadingNumber(a.name).CompareTo(LeadingNumber(b.name)));
        levelButtons = found;
        Debug.Log($"[LevelSelect] 已自动收集「{root.name}」下的 {found.Length} 个关卡按钮：" +
                  string.Join("、", Array.ConvertAll(found, b => b.name)));
    }

    /// <summary>在自身、父节点（Canvas）及其子节点里找"放关卡按钮的容器"。</summary>
    private Transform FindLevelButtonsRoot()
    {
        string[] candidates = { "关卡图", "关卡按钮", "LevelButtons", "Levels", "Buttons" };

        foreach (string name in candidates)
        {
            Transform child = transform.Find(name);
            if (child != null)
            {
                return child;
            }
        }

        Transform parent = transform.parent;
        if (parent != null)
        {
            foreach (string name in candidates)
            {
                Transform sibling = parent.Find(name);
                if (sibling != null)
                {
                    return sibling;
                }
            }
        }

        return null;
    }

    /// <summary>取名字开头的数字（"3" → 3）；名字里没有数字的排到最后。</summary>
    private static int LeadingNumber(string name)
    {
        int value = 0;
        int digits = 0;

        for (int i = 0; i < name.Length && char.IsDigit(name[i]); i++)
        {
            value = value * 10 + (name[i] - '0');
            digits++;
        }

        return digits > 0 ? value : int.MaxValue;
    }

    private void Initialize()
    {
        if (m_Initialized)
        {
            return;
        }

        m_Initialized = true;

        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }

        // 9 个关卡按钮统一在这里挂回调，不需要在 Inspector 里逐个配 onClick。
        EnsureLevelButtons();

        if (levelButtons != null)
        {
            for (int i = 0; i < levelButtons.Length; i++)
            {
                if (levelButtons[i] == null)
                {
                    continue;
                }

                int index = i;
                levelButtons[i].onClick.AddListener(() => Open(index));
            }
        }
        else
        {
            Debug.LogWarning("[LevelSelect] 没有配置关卡按钮，面板无法打开。");
        }

        if (enterButton != null)
        {
            enterButton.onClick.AddListener(OnClickEnter);
        }

        if (exitButton != null)
        {
            exitButton.onClick.AddListener(Close);
        }

        if (prevButton != null)
        {
            prevButton.onClick.AddListener(OnClickPrev);
        }

        if (nextButton != null)
        {
            nextButton.onClick.AddListener(OnClickNext);
        }

        // 初始隐藏面板；如果 Open 先被调用（面板原本就是 inactive），这里不会把它重新关掉。
        if (m_CurrentIndex < 0)
        {
            panelRoot.SetActive(false);
        }
    }

    private void ApplyLevelData(int levelIndex, LevelData data)
    {
        SetText(levelIndexText, string.IsNullOrEmpty(data.levelIndexText)
            ? $"LEVEL {levelIndex + 1}"
            : data.levelIndexText);
        SetText(englishTitleText, data.englishTitle);
        SetText(chineseTitleText, data.chineseTitle);
        SetText(descriptionText, data.description);

        if (previewImage != null)
        {
            previewImage.sprite = data.previewSprite;
            // 没配预览图时隐藏图片，避免显示成一块白方块。
            previewImage.enabled = data.previewSprite != null;
        }
    }

    /// <summary>写文本：自动识别是旧版 UGUI Text 还是 TextMeshPro。</summary>
    private static void SetText(Graphic label, string value)
    {
        if (label == null)
        {
            return;
        }

        switch (label)
        {
            case TMP_Text tmpText:
                tmpText.text = value;
                break;
            case Text uiText:
                uiText.text = value;
                break;
            default:
                Debug.LogWarning($"[LevelSelect] {label.name} 既不是 Text 也不是 TextMeshPro，无法写入文本。", label);
                break;
        }
    }
}

/// <summary>
/// 单个关卡的数据。写在同一个文件里，避免像之前那样因为别的文件被删掉而找不到类型。
/// </summary>
[Serializable]
public class LevelData
{
    public string levelIndexText;    // 面板左上角的 "LEVEL 1"（留空自动用下标）
    public string englishTitle;      // 英文关卡名
    public string chineseTitle;      // 中文关卡名
    public Sprite previewSprite;     // 关卡预览图
    public string description;       // 关卡简介
    public string sceneName;         // 点"开始"要加载的场景名（需已加入 Build Settings，优先使用）
    public int sceneBuildIndex;      // 没填 sceneName 时用的 Build Settings 序号
}
