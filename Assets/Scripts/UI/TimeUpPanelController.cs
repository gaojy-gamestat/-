using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using GameNet.Gameplay;

/// <summary>
/// 时间到结算面板：监听 CountdownSystem 的 OnTimeUp 事件，
/// 弹出「时间到！」面板，提供 重新挑战（重载本关） / 返回选关 两个按钮。
///
/// 独立脚本，不修改队友的 CountdownSystem / CountdownUI，避免合并冲突。
/// 用法：挂到关卡场景的 Canvas 上，把面板和两个按钮拖进槽位。
/// Source 留空会自动查找场景里的 CountdownSystem。
/// </summary>
public class TimeUpPanelController : MonoBehaviour
{
    [Header("倒计时来源（留空 = 自动查找场景里的 CountdownSystem）")]
    public CountdownSystem Source;

    [Header("时间到面板（默认隐藏，时间到自动弹出）")]
    public GameObject Panel_TimeUp;
    public Button Btn_Retry;
    public Button Btn_Back;

    [Header("「返回选关」要去的场景名")]
    public string SelectSceneName = "关卡选择";

    [Header("本地测试：进场景后自动开始倒计时（联机时无效，仍由主机控制）")]
    public bool autoStartInLocal = true;

    [Header("本地测试时长（秒），0 = 用系统/关卡配置的时长。测试完记得改回 0")]
    public float localTestDuration = 0f;

    private void Awake()
    {
        if (Panel_TimeUp != null) Panel_TimeUp.SetActive(false);
        if (Btn_Retry != null) Btn_Retry.onClick.AddListener(OnRetryClicked);
        if (Btn_Back != null) Btn_Back.onClick.AddListener(OnBackClicked);
    }

    private void OnEnable()
    {
        if (Source == null) Source = FindObjectOfType<CountdownSystem>();
        if (Source != null) Source.OnTimeUp += HandleTimeUp;
    }

    private void Start()
    {
        // 修复：项目里没有任何地方调用 StartTimer()，本地 Play 时倒计时根本不会走。
        // 这里在本地模式下自动开始；联机时 CountdownSystem 内部会因无权限而忽略，不影响主机逻辑。
        if (Source == null) Source = FindObjectOfType<CountdownSystem>();
        if (Source == null || !autoStartInLocal || Source.IsRunning) return;

        if (localTestDuration > 0f) Source.SetDuration(localTestDuration);
        Source.StartTimer();
    }

    private void OnDisable()
    {
        if (Source != null) Source.OnTimeUp -= HandleTimeUp;
        Time.timeScale = 1f; // 保险：无论怎么退出本组件，都恢复游戏速度
    }

    // ------------------------------------------------------------------
    // 事件
    // ------------------------------------------------------------------

    private void HandleTimeUp()
    {
        if (Panel_TimeUp != null) Panel_TimeUp.SetActive(true);
        Time.timeScale = 0f; // 冻结游戏世界，弹窗期间画面暂停
    }

    public void OnRetryClicked()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name); // 重载当前关卡
    }

    public void OnBackClicked()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SelectSceneName);
    }
}
