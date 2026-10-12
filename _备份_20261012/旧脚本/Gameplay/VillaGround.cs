using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 行走地面：一个水平的 3D 碰撞体（BoxCollider，等价于一块薄“平面”），
    /// 放在 y=0 的 XZ 平面上，供 Oscar 用 WASD 行走与边界限制。
    ///
    /// 【视觉对齐原理】（为什么相机俯视下碰撞体能和 2D 图里的地板重合）：
    ///  斜俯视相机下，地面深度方向(Z)在画面上被压缩 sin(俯角) 倍——
    ///  30° 俯角时地面 1 米深在图上只占 0.5“米高”。
    ///  因此想让碰撞体的画面投影正好盖住别墅图里的地板区域，就必须：
    ///    碰撞体宽 = 地板像素宽 × unitsPerPixel（水平方向不压缩）
    ///    碰撞体深 = 地板像素高 × unitsPerPixel ÷ sin(俯角)（把被压缩的量补回来）
    ///  本组件按 floorRect（图片归一化坐标里的地板范围）自动完成该计算，
    ///  并在 Scene 视图画出“地面框 + 它在图平面上的投影框”，方便微调对齐。
    ///
    /// 摆放位置：由 VillaLevelBootstrap 自动创建在场景根目录、名为“行走地面”；
    /// 手动摆放时放在世界原点即可（坐标会被脚本按 floorRect 重算覆盖）。
    /// floorRect 微调方法：进入 Play，选中“行走地面”，在 Inspector 里拖动
    /// floorRect 的四个值，Scene 视图里绿色投影框会实时贴着地板区域移动。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class VillaGround : MonoBehaviour
    {
        [Header("对齐数据源（自动接线，也可手动拖）")]
        [SerializeField] private VillaScenePlane scenePlane;
        [SerializeField] private VillaSceneCamera sceneCamera;

        [Header("地板区域（图片归一化坐标：x,y=左下角；w,h=宽高，范围0~1）")]
        [Tooltip("按别墅图里地板的实际范围填写。默认值接近全图，请按美术图微调")]
        [SerializeField] private Rect floorRect = new Rect(0.08f, 0.10f, 0.84f, 0.80f);

        [Tooltip("碰撞体厚度（纯数值用，避免零厚度）。顶面固定在 y=0")]
        [SerializeField] private float thickness = 0.2f;

        private BoxCollider m_Box;

        /// <summary>3D 平面碰撞体（顶面 = 行走面 y=0）</summary>
        public BoxCollider Box => m_Box;

        /// <summary>当前地板范围（图片归一化坐标）。Play 中可直接读取，供编辑器写回</summary>
        public Rect FloorRect => floorRect;

        /// <summary>
        /// 应用新的地板范围并立即重算碰撞体（Bootstrap 启动时/编辑器写回时调用）。
        /// </summary>
        public void ApplyFloorRect(Rect rect)
        {
            floorRect = rect;
            EnsureReferences();
            EnsureCollider();
            ApplyLayout();
        }

        private void OnValidate()
        {
            // Play 模式下拖 Inspector 里的 floorRect 时实时生效：
            // Scene 视图绿色投影框会跟着动，直接贴着别墅图里的地板微调即可
            if (Application.isPlaying)
            {
                EnsureReferences();
                EnsureCollider();
                ApplyLayout();
            }
        }

        /// <summary>行走范围（世界 XZ 包围盒，供角色限制移动）</summary>
        public Bounds WalkBounds => m_Box != null ? m_Box.bounds : new Bounds(Vector3.zero, Vector3.zero);

        private void Awake()
        {
            EnsureReferences();
            EnsureCollider();
            ApplyLayout();
        }

        private void Start()
        {
            // 相机/画布可能在 Awake 之后才就绪，这里再校正一次
            EnsureReferences();
            ApplyLayout();
        }

        /// <summary>外部接线（Bootstrap 调用）</summary>
        public void Wire(VillaScenePlane plane, VillaSceneCamera camera)
        {
            scenePlane = plane;
            sceneCamera = camera;
        }

        /// <summary>外部接线后立即应用布局（Bootstrap 会调用）</summary>
        public void ConfigureNow()
        {
            EnsureReferences();
            EnsureCollider();
            ApplyLayout();
        }

        private void EnsureReferences()
        {
            if (sceneCamera == null)
            {
                sceneCamera = FindObjectOfType<VillaSceneCamera>();
            }

            if (scenePlane == null)
            {
                scenePlane = FindObjectOfType<VillaScenePlane>();
            }
        }

        private void EnsureCollider()
        {
            if (m_Box == null)
            {
                m_Box = GetComponent<BoxCollider>();
                if (m_Box == null)
                {
                    m_Box = gameObject.AddComponent<BoxCollider>();
                }
            }
        }

        /// <summary>
        /// 根据 floorRect（图片像素范围）计算碰撞体的尺寸与中心：
        ///  - 水平：1 像素 = unitsPerPixel 世界单位，不压缩；
        ///  - 深度：1 像素 = unitsPerPixel / sin(俯角) 世界单位，补偿斜视压缩；
        ///  - 中心：图片中心对应地面原点，地板区域中心相对图片中心的像素偏移
        ///          换算成 XZ 偏移；顶面保持 y=0（中心 y = -thickness/2）。
        /// </summary>
        private void ApplyLayout()
        {
            if (scenePlane == null || scenePlane.ImagePixelSize.x <= 0f)
            {
                return;
            }

            Vector2 px = scenePlane.ImagePixelSize;                 // 图片像素尺寸
            float s = scenePlane.UnitsPerPixel;                     // 像素→世界比例
            float sinPitch = sceneCamera != null ? Mathf.Sin(sceneCamera.PitchRadians) : 0.5f;
            if (sinPitch < 0.01f)
            {
                sinPitch = 0.01f;
            }

            // floorRect（归一化）→ 像素
            float wPx = floorRect.width * px.x;
            float hPx = floorRect.height * px.y;
            // 地板中心相对图片中心的像素偏移（图片坐标 y 向上）
            float cxPx = (floorRect.x + floorRect.width * 0.5f - 0.5f) * px.x;
            float cyPx = (floorRect.y + floorRect.height * 0.5f - 0.5f) * px.y;

            // 世界尺寸：深度方向除以 sin(俯角) 补偿斜俯视的压缩
            Vector3 size = new Vector3(
                wPx * s,
                thickness,
                hPx * s / sinPitch);

            Vector3 center = new Vector3(
                cxPx * s,
                -thickness * 0.5f,   // 顶面贴着 y=0 行走面
                cyPx * s / sinPitch);

            m_Box.size = size;
            m_Box.center = center;
        }

        /// <summary>图片像素坐标 → 地面行走面上的世界坐标（供出生点/交互点摆放）</summary>
        public Vector3 ImagePixelToGround(Vector2 pixel)
        {
            return scenePlane.ImagePixelToGround(sceneCamera, pixel);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (m_Box == null)
            {
                return;
            }

            // 1) 画地面碰撞体（黄色框）
            Gizmos.color = Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(m_Box.center, m_Box.size);
            Gizmos.matrix = Matrix4x4.identity;

            // 2) 画“碰撞体四角投影到图平面”的矩形（绿色）——
            //    Play 时它应当与别墅图里的地板区域完全重合，即为视觉对齐的直观校验
            if (scenePlane == null || sceneCamera == null)
            {
                return;
            }

            Camera cam = scenePlane.RenderCamera != null ? scenePlane.RenderCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            var vsc = sceneCamera.GetComponent<VillaSceneCamera>();
            float distance = vsc != null ? vsc.PlaneDistance : 50f;

            Bounds b = m_Box.bounds;
            Vector3[] corners =
            {
                new Vector3(b.min.x, 0f, b.min.z),
                new Vector3(b.max.x, 0f, b.min.z),
                new Vector3(b.max.x, 0f, b.max.z),
                new Vector3(b.min.x, 0f, b.max.z),
            };

            Gizmos.color = Color.green;
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = VillaScenePlane.ProjectGroundToPlane(cam, distance, corners[i]);
                Vector3 c = VillaScenePlane.ProjectGroundToPlane(cam, distance, corners[(i + 1) % 4]);
                Gizmos.DrawLine(a, c);
            }
        }
#endif
    }
}
