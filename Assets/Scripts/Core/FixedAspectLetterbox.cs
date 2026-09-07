using UnityEngine;

/// <summary>
/// 让游戏画面在任意屏幕宽高比下都保持 16:9 居中显示，
/// 画面没铺满的其余区域用纯黑填充（Letterbox 黑边模式），不会裁切、不会拉伸变形。
///
/// 用法：
///  1. 把这个组件挂到“Main Camera”（游戏主相机）上即可；
///  2. 运行时它会自动创建一个全屏黑底相机垫底（不污染场景层级）；
///  3. 之后若加 HUD/UI：把 Canvas 的 Render Mode 改为 Screen Space - Camera，
///     Render Camera 指定为主相机，HUD 就会跟着 16:9 画面走，不会跑到黑边外面。
///
/// 测试：进入 Play 后，用 Game 视图左上角下拉框切换 Free Aspect / 16:9 / 4:3 等，
///       可立即看到黑边自动出现在左右或上下。
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class FixedAspectLetterbox : MonoBehaviour
{
    [Tooltip("目标宽高比。16:9 即 16f / 9f，可改成其它比例")]
    [SerializeField]
    private float targetAspect = 16f / 9f;

    private Camera m_Camera;
    private Camera m_BlackFillCamera;

    private void Awake()
    {
        m_Camera = GetComponent<Camera>();
        CreateBlackFillCamera();
        ApplyAspect();
    }

    private void Update()
    {
        // 运行中窗口可能被拖拽改变大小，逐帧按当前屏幕比例重新计算取景
        ApplyAspect();
    }

    private void OnDestroy()
    {
        if (m_BlackFillCamera != null)
        {
            Destroy(m_BlackFillCamera.gameObject);
        }
    }

    /// <summary>
    /// 让主相机只在屏幕的某个矩形区域内取景，该矩形恰为 16:9 且居中；
    /// 矩形之外的屏幕区域由下面的黑底相机填充。
    /// </summary>
    private void ApplyAspect()
    {
        if (m_Camera == null || Screen.height <= 0)
        {
            return;
        }

        float screenAspect = Screen.width / (float)Screen.height;
        Rect rect;

        if (screenAspect > targetAspect)
        {
            // 屏幕比 16:9 更“宽”（例如 21:9 超宽屏）：高度铺满，左右留黑边
            float w = targetAspect / screenAspect;
            rect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }
        else
        {
            // 屏幕比 16:9 更“高/方”（例如 4:3、iPad、手机竖屏）：宽度铺满，上下留黑边
            float h = screenAspect / targetAspect;
            rect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }

        m_Camera.rect = rect;
    }

    /// <summary>
    /// 创建一个全屏纯黑相机，保证先刷一层黑色打底，主相机的黑边区域不会露出底色。
    /// 它不渲染任何物体（cullingMask = 0），只负责把整个屏幕涂成黑色。
    /// </summary>
    private void CreateBlackFillCamera()
    {
        var go = new GameObject("BlackFillCamera");
        go.hideFlags = HideFlags.HideAndDontSave; // 不显示在层级里，也不会被误保存进场景

        var fill = go.AddComponent<Camera>();
        fill.clearFlags = CameraClearFlags.SolidColor;
        fill.backgroundColor = Color.black;
        fill.cullingMask = 0;                    // 什么都不渲染，只填充颜色
        fill.orthographic = true;
        fill.depth = m_Camera.depth - 1;         // 确保在主相机之前先刷黑整屏
        fill.rect = new Rect(0f, 0f, 1f, 1f);

        m_BlackFillCamera = fill;
    }
}
