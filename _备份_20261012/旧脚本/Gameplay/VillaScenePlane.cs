using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 场景平面（第一关的“别墅”整图）载体：
    /// 一个【世界空间 Canvas】+ 子节点 UI Image 承载别墅图片。
    ///
    /// 设计要点：
    ///  1. 按需求“场景画面用 Image 承载图片”——别墅图仍是 uGUI Image，
    ///     只是挂在 World Space 画布下，由 3D 主相机直接渲染（游戏画面=相机输出）；
    ///  2. 画布平面每帧对齐相机：旋转与相机一致（图面正对相机视线），
    ///     位置在相机正前方 planeDistance 处 —— 正交相机下图面不变形、不透视；
    ///  3. 画布“1 个单位 = 1 个图片像素”，整体缩放 unitsPerPixel(默认0.01)，
    ///     于是别墅图(1677×938px)的世界尺寸为 16.77×9.38；
    ///  4. 提供像素↔地面的坐标换算，供地面碰撞体与角色对齐使用：
    ///     换算依据是 VillaSceneCamera 把「图片中心」投影到地面原点 (0,0,0)。
    ///
    /// 摆放位置：不需要手动摆！本组件由 VillaLevelBootstrap 在运行时自动创建；
    /// 若手动摆放：把它放在任意位置即可，LateUpdate 会强制对齐到相机前方。
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [ExecuteAlways] // 编辑模式也生效：固化搭建后，编辑器里别墅图就摆在相机前方，可直接对照摆放行走区域
    [DefaultExecutionOrder(-150)]
    public class VillaScenePlane : MonoBehaviour
    {
        [Tooltip("1 个图片像素对应的世界单位数。0.01 即 100 像素/单位（与美术 PPU 一致）")]
        [SerializeField] private float unitsPerPixel = 0.01f;

        private Canvas m_Canvas;
        private Image m_Image;

        /// <summary>承载的别墅 Image（子节点）</summary>
        public Image SceneImage => m_Image;

        /// <summary>图片原始像素尺寸</summary>
        public Vector2 ImagePixelSize { get; private set; }

        /// <summary>图片对应的世界尺寸（宽, 高）</summary>
        public Vector2 WorldSize => ImagePixelSize * unitsPerPixel;

        /// <summary>1 像素 = 多少世界单位</summary>
        public float UnitsPerPixel => unitsPerPixel;

        /// <summary>渲染该平面的相机（不指定则自动取 Camera.main）</summary>
        public Camera RenderCamera { get; set; }

        private void Awake()
        {
            m_Canvas = GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.WorldSpace; // 世界空间画布，由主相机渲染
            m_Canvas.sortingOrder = 0;                    // 垫在最底层（角色=5，HUD=10）

            InitializeFromImage();
        }

        private void Start()
        {
            InitializeFromImage();
            AlignToCamera();
        }

        private void Update()
        {
            // ExecuteAlways：编辑模式下 LateUpdate 不会被调用，
            // 用 Update 保持画布对齐相机（固化搭建后编辑器里也所见即所得）
            if (!Application.isPlaying)
            {
                AlignToCamera();
            }
        }

        private void LateUpdate()
        {
            // 每帧对齐相机（相机参数可能在运行中被调整；开销极小）
            AlignToCamera();
        }

        /// <summary>找到子节点 Image，按精灵原始尺寸铺满画布，并把画布缩放统一成 unitsPerPixel</summary>
        private void InitializeFromImage()
        {
            // 注意：不能写成“m_Image != null 就 return”——
            // InitializeForBootstrap 会先把引用赋进来再调本方法，
            // 那样会误判为“已初始化”而跳过全部布局（黑屏事故的根源）。
            // 布局操作本身就是幂等的（重复赋同样的值），多跑一次无害。
            if (m_Image == null)
            {
                m_Image = GetComponentInChildren<Image>();
            }

            if (m_Image == null)
            {
                Debug.LogWarning("[VillaScenePlane] 子节点里没有找到 Image（别墅图），布局未初始化。");
                return;
            }

            Sprite sprite = m_Image.sprite;
            ImagePixelSize = sprite != null
                ? new Vector2(sprite.rect.width, sprite.rect.height)
                : m_Image.rectTransform.rect.size;

            // Image 使用精灵原始尺寸（画布单位下 1 单位 = 1 像素）
            m_Image.SetNativeSize();

            var imageRt = m_Image.rectTransform;
            imageRt.anchorMin = imageRt.anchorMax = imageRt.pivot = new Vector2(0.5f, 0.5f);
            imageRt.anchoredPosition = Vector2.zero;
            imageRt.localScale = Vector3.one;

            // 画布根节点：尺寸=图片像素数，缩放=unitsPerPixel → 世界尺寸=像素数×unitsPerPixel
            var rt = (RectTransform)transform;
            rt.sizeDelta = ImagePixelSize;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one * unitsPerPixel;

            m_Image.raycastTarget = false;
        }

        /// <summary>外部接线后立即应用布局（Bootstrap 会调用）</summary>
        public void InitializeForBootstrap(Image image)
        {
            m_Image = image;
            InitializeFromImage();
            AlignToCamera();
        }

        /// <summary>
        /// 重设像素→世界比例并立即重铺布局（Bootstrap 调用）。
        /// 用途：换了更高分辨率的图（如 1677×938 → 2160×1208）后，
        /// 按“目标世界宽度 ÷ 新图像素宽”重算，保证游戏世界尺寸不变，
        /// 相机视域、地面碰撞体、角色比例全部自动适配（WorldSize 是动态属性）。
        /// </summary>
        public void SetUnitsPerPixel(float value)
        {
            unitsPerPixel = value;
            if (m_Image != null)
            {
                ((RectTransform)transform).localScale = Vector3.one * unitsPerPixel;
            }
        }

        /// <summary>画布平面旋转/位置对齐相机：正对视线、位于相机前方 planeDistance 处</summary>
        private void AlignToCamera()
        {
            Camera cam = RenderCamera != null ? RenderCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            // 防御性接线：世界空间画布一般不依赖 worldCamera 渲染，
            // 但显式绑定可以覆盖个别版本里按相机裁剪 UI 的行为，也方便事件射线
            if (m_Canvas.worldCamera == null || m_Canvas.worldCamera != cam)
            {
                m_Canvas.worldCamera = cam;
            }

            float distance = 50f;
            var vsc = cam.GetComponent<VillaSceneCamera>();
            if (vsc != null)
            {
                distance = vsc.PlaneDistance;
            }

            var rt = (RectTransform)transform;
            rt.rotation = cam.transform.rotation;                                  // 图面垂直于视线
            rt.position = cam.transform.position + cam.transform.forward * distance; // 相机正前方
        }

        // ---------------- 坐标换算工具（供地面/角色使用） ----------------

        /// <summary>
        /// 把地面上的点投影到“相机正前方 distance 处的图平面”上的世界坐标。
        /// 正交相机下，同一条视线上的点屏幕位置相同，
        /// 因此返回值就是该地面点在画面上“应该画在哪里”。
        /// </summary>
        public static Vector3 ProjectGroundToPlane(Camera cam, float distance, Vector3 groundPoint)
        {
            Transform t = cam.transform;
            Vector3 local = t.InverseTransformPoint(groundPoint); // 相机本地坐标
            return t.position + t.right * local.x + t.up * local.y + t.forward * distance;
        }

        /// <summary>
        /// 图片像素坐标 → 地面世界坐标（y=0）。
        /// 前提：VillaSceneCamera 使图片中心投影到地面原点。
        /// 像素 x 向右对应世界 +X；像素 y 向上对应“远离相机”的 +Z，
        /// 且深度方向被俯角压缩，1 像素深度 = unitsPerPixel / sin(俯角)。
        /// </summary>
        public Vector3 ImagePixelToGround(VillaSceneCamera sceneCamera, Vector2 pixel)
        {
            float sinPitch = sceneCamera != null ? Mathf.Sin(sceneCamera.PitchRadians) : 0.5f;
            if (sinPitch < 0.01f)
            {
                sinPitch = 0.01f;
            }

            float dx = (pixel.x - ImagePixelSize.x * 0.5f) * unitsPerPixel;
            float dz = (pixel.y - ImagePixelSize.y * 0.5f) * unitsPerPixel / sinPitch;
            return new Vector3(dx, 0f, dz);
        }
    }
}
