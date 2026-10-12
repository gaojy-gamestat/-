using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 屏幕适配：强制游戏画面保持 16:9 + 让所有 UI 随屏幕等比缩放（相对位置不变）。
    ///
    /// ============================ 挂在哪里 ============================
    /// 挂在 <b>Main Camera</b> 上（一个场景挂一个就够）。
    ///
    /// ============================ 什么时候生效 ============================
    /// <b>只在运行时生效</b>（OnEnable / Update）。
    /// 它不会修改场景文件，退出播放会把相机视口还原成原值，
    /// 所以 Editor 的 Scene 视图和 Inspector 里的数值都不会被它改掉。
    ///
    /// ============================ 它做两件事 ============================
    /// 1) <b>视口裁成 16:9（黑边）</b>
    ///      窗口比 16:9 更“方”（例如 4:3）  → 上下加黑边；
    ///      窗口比 16:9 更宽（例如 21:9）    → 左右加黑边。
    ///    这样 3D 场景（地面碰撞体、角色）永远按 16:9 取景，构图 / 相对位置不变。
    ///
    /// 2) <b>UI 等比缩放</b>
    ///    把所有 Canvas 的 CanvasScaler 设成
    ///      Scale With Screen Size + 参考分辨率 1920×1080 + Match 0.5，
    ///    于是所有 UI 元素随屏幕整体缩放，锚点（相对位置）不变。
    ///
    /// ============================ 和其它脚本的关系 ============================
    /// * 别墅图是 Screen Space - Overlay 的 UI（本脚本<b>不会</b>去改渲染模式），
    ///   它靠 Image 的 Preserve Aspect 自己保持比例 —— 详见 VillaGroundPlane 的说明；
    /// * 行走地面是「按相机射线反算」出来的（VillaGroundPlane），
    ///   所以无论屏幕是 16:9 还是别的比例，地面碰撞体和图片里的地板永远对得上。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class VillaScreenAdapter : MonoBehaviour
    {
        [Header("目标宽高比")]
        [Tooltip("16:9 = 16f/9f = 1.7778。改这个值可以换成别的比例。")]
        public float targetAspect = 16f / 9f;

        [Header("视口裁切（黑边）")]
        [Tooltip("勾上：把相机渲染区压成目标比例，多出来的部分留黑边。")]
        public bool letterbox = true;

        [Header("UI 等比缩放")]
        [Tooltip("勾上：运行时把所有 Canvas 设成 Scale With Screen Size，UI 随屏幕缩放、相对位置不变。")]
        public bool scaleCanvasWithScreen = true;
        [Tooltip("UI 设计分辨率：所有 UI 都是按这个尺寸摆的（1920×1080）。")]
        public Vector2 referenceResolution = new Vector2(1920f, 1080f);
        [Tooltip("0=只按宽度缩放，1=只按高度缩放，0.5=折中。")]
        [Range(0f, 1f)] public float matchWidthOrHeight = 0.5f;

        // ------------------------------------------------------------------
        // 运行时缓存
        // ------------------------------------------------------------------
        private Camera _cam;
        private Rect _originalRect = new Rect(0f, 0f, 1f, 1f);
        private bool _rectSaved;
        private int _lastScreenWidth = -1;
        private int _lastScreenHeight = -1;

        /// <summary>当前渲染区（0~1 视口）。UI 想和 3D 画面严格对齐时会用到。</summary>
        public Rect CurrentViewport { get { return _cam != null ? _cam.rect : new Rect(0f, 0f, 1f, 1f); } }

        private void Awake()
        {
            EnsureCamera();
        }

        private void OnEnable()
        {
            EnsureCamera();
            ApplyViewport();     // 裁 16:9
            ApplyCanvasScaler(); // UI 等比缩放
        }

        private void OnDisable()
        {
            // 退出播放 / 组件关闭时把视口还回去，绝不把运行时的值留在场景里
            RestoreViewport();
        }

        private void Update()
        {
            // 窗口尺寸变了（拖窗口、切分辨率、换显示器）→ 重新裁一次
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                ApplyViewport();
            }
        }

        private void EnsureCamera()
        {
            if (_cam == null)
            {
                _cam = GetComponent<Camera>();
            }
        }

        // ------------------------------------------------------------------
        // 1) 视口裁成目标比例
        // ------------------------------------------------------------------
        private void ApplyViewport()
        {
            EnsureCamera();
            if (_cam == null)
            {
                return;
            }

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            if (!letterbox || targetAspect <= 0.0001f)
            {
                RestoreViewport(); // 关掉黑边就还原
                return;
            }

            if (!_rectSaved)
            {
                _originalRect = _cam.rect;
                _rectSaved = true;
            }

            float windowAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float ratio = windowAspect / targetAspect; // <1:窗口更方(上下留边)  >1:窗口更宽(左右留边)

            if (ratio < 1f)
            {
                // 上下黑边：视口高度缩小，垂直居中
                _cam.rect = new Rect(0f, (1f - ratio) * 0.5f, 1f, ratio);
            }
            else if (ratio > 1f)
            {
                // 左右黑边：视口宽度缩小，水平居中
                float w = 1f / ratio;
                _cam.rect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            else
            {
                _cam.rect = new Rect(0f, 0f, 1f, 1f); // 正好 16:9
            }
        }

        private void RestoreViewport()
        {
            if (_cam != null && _rectSaved)
            {
                _cam.rect = _originalRect;
                _rectSaved = false;
            }
        }

        // ------------------------------------------------------------------
        // 2) UI 等比缩放
        // ------------------------------------------------------------------
        private void ApplyCanvasScaler()
        {
            if (!scaleCanvasWithScreen)
            {
                return;
            }

            // includeInactive=true：没激活的界面也一起设好，避免以后打开时比例不对
            var scalers = FindObjectsOfType<CanvasScaler>(true);
            for (int i = 0; i < scalers.Length; i++)
            {
                CanvasScaler s = scalers[i];
                if (s == null)
                {
                    continue;
                }

                s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                s.referenceResolution = referenceResolution;
                s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                s.matchWidthOrHeight = matchWidthOrHeight;
                s.referencePixelsPerUnit = 100f;
            }
        }
    }
}
