using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 正交斜俯视相机（第一关主相机专用）。
    ///
    /// 职责：
    ///  1. 把主相机设置为「正交 Projection + 固定俯角（斜俯视）」；
    ///  2. 相机始终注视世界原点（0,0,0），场景平面图的【中心】恰好投影在地
    ///     面原点正上方——这样“图片中心”和“地面原点”就是同一个屏幕位置，
    ///     <see cref="VillaGround"/>、<see cref="OscarPlayerController"/>
    ///     都据此做像素↔地面的坐标换算；
    ///  3. 根据 <see cref="VillaScenePlane"/> 上别墅图的实际尺寸，动态计算
    ///     orthographicSize，保证【任意窗口宽高比下视域都完整覆盖整张图】
    ///     （高不够就按高算、宽不够就按宽算，取两者较大值）；
    ///  4. 自动给相机挂上项目里已有的 <see cref="FixedAspectLetterbox"/>
    ///     （16:9 黑边适配，已有实现，这里只做复用、不重复造轮子）。
    ///
    /// 相机摆放参数（都是本组件的序列化字段，可在 Inspector 调整）：
    ///  - pitchDegrees  俯角，默认 30°。值越大越“俯”、越垂直；越小越“平视”。
    ///                  俯角同时决定了地面深度被压缩的比例 sin(俯角)。
    ///  - cameraHeight  相机离行走地面（y=0 平面）的高度，默认 12。
    ///                  相机位置会被强制摆到 (0, cameraHeight, -cameraHeight/tan(俯角))，
    ///                  即“沿视线方向看向地面原点”，图片中心正好压在原点上。
    ///  - planeDistance 别墅图平面离相机的正前方距离，默认 20。
    ///                  图平面永远垂直于相机视线（正对相机），因此图片不变形；
    ///                  要求 cameraHeight / sin(俯角) > planeDistance，保证图平面在地面之上。
    ///  - viewMargin    视域四周留白比例，默认 0.02（2%），防止贴边裁切。
    ///
    /// 注意：本组件【只在运行（Play）时】配置相机——编辑模式下完全不碰主相机，
    /// 你在 Inspector 里摆好的位置/旋转/投影/正交尺寸不会被覆盖，也不会把场景改脏。
    /// 编辑「行走区域」时的对照预览由编辑器脚本 VillaEditView 负责：它把 Canvas 上的
    /// 别墅图显示在相机正前方，所以相机怎么摆都不影响编辑。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(-200)]
    public class VillaSceneCamera : MonoBehaviour
    {
        [Header("斜俯视参数")]
        [Tooltip("相机俯角（度）。30° 为典型斜俯视；90° 则变成正俯视")]
        [SerializeField] private float pitchDegrees = 30f;

        [Tooltip("相机离行走地面(y=0)的高度。相机注视点固定为世界原点")]
        [SerializeField] private float cameraHeight = 12f;

        [Tooltip("别墅图平面离相机的距离（图平面正对相机、不产生透视变形）")]
        [SerializeField] private float planeDistance = 20f;

        [Tooltip("视域相对场景图四周的留白比例（0 = 图片正好铺满视域）")]
        [SerializeField, Range(0f, 0.25f)] private float viewMargin = 0.02f;

        [Header("自动接线（由 VillaLevelBootstrap 自动填写，也可手动拖）")]
        [SerializeField] private VillaScenePlane scenePlane;

        private Camera m_Camera;
        private float m_LastAspect = -1f;

        /// <summary>俯角（弧度）。地面深度压缩比例 = Sin(俯角)</summary>
        public float PitchRadians => pitchDegrees * Mathf.Deg2Rad;

        /// <summary>别墅图平面离相机的距离，供平面/角色对齐使用</summary>
        public float PlaneDistance => planeDistance;

        private void Awake()
        {
            m_Camera = GetComponent<Camera>();

            // 16:9 适配直接复用项目已有的黑边脚本（若相机上还没有则自动补一个）
            if (GetComponent<FixedAspectLetterbox>() == null)
            {
                gameObject.AddComponent<FixedAspectLetterbox>();
            }

            ConfigureCamera();
        }

        private void LateUpdate()
        {
            // 窗口尺寸 / 黑边区域变化时（FixedAspectLetterbox 在 Update 里改 camera.rect），
            // 重新计算正交尺寸，保证视域始终完整覆盖整张场景图
            if (!Mathf.Approximately(m_Camera.aspect, m_LastAspect))
            {
                ApplyOrthographicSize();
            }
        }

        /// <summary>外部（如 Bootstrap）接线完成后调用，立即应用一次全部参数</summary>
        public void ConfigureNow()
        {
            ConfigureCamera();
        }

        /// <summary>绑定承载别墅图的世界空间画布，用于读取图片世界尺寸</summary>
        public void BindPlane(VillaScenePlane plane)
        {
            scenePlane = plane;
            ApplyOrthographicSize();
        }

        /// <summary>
        /// 相机姿态与位置：
        /// 旋转 = (俯角, 0, 0)，即视线朝 +Z 前方且向下俯视；
        /// 位置 = (0, 高度, -高度/tan(俯角))，使视线恰好穿过地面原点 (0,0,0)。
        /// 于是「别墅图中心」的投影点 == 地面原点，两边坐标系可以互相换算。
        /// </summary>
        private void ConfigureCamera()
        {
            transform.rotation = Quaternion.Euler(pitchDegrees, 0f, 0f);

            float tan = Mathf.Tan(PitchRadians);
            if (tan < 0.01f)
            {
                // 俯角接近 90° 时退化为正俯视：直接架在原点正上方
                transform.position = new Vector3(0f, cameraHeight, 0f);
            }
            else
            {
                transform.position = new Vector3(0f, cameraHeight, -cameraHeight / tan);
            }

            m_Camera.orthographic = true;                       // 正交投影
            m_Camera.clearFlags = CameraClearFlags.SolidColor;  // 纯色背景
            m_Camera.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
            m_Camera.nearClipPlane = 0.3f;
            m_Camera.farClipPlane = 200f;                       // 图平面(20)与地面最远角都在范围内

            ApplyOrthographicSize();
        }

        /// <summary>
        /// 计算正交尺寸，使视域【完整覆盖】整张别墅图：
        ///  - 若按高度刚好放下：orthoSize = 图高/2；
        ///  - 若当前画面比图片更“窄”（例如没开 16:9 黑边的极端情况）：
        ///    orthoSize = 图宽/(2×画面宽高比)；
        ///  取较大者，再乘上 (1+留白)。
        /// 项目强制 16:9 黑边后，画面比例恒为 16:9，别墅图 1677×938（≈16:9）
        /// 会几乎正好铺满整个画面。
        /// </summary>
        private void ApplyOrthographicSize()
        {
            if (scenePlane == null)
            {
                scenePlane = FindObjectOfType<VillaScenePlane>();
            }

            if (scenePlane != null)
            {
                Vector2 worldSize = scenePlane.WorldSize;
                float aspect = m_Camera.aspect; // 已包含黑边 rect 的影响

                float byHeight = worldSize.y * 0.5f;
                float byWidth = aspect > 0.01f ? worldSize.x / (2f * aspect) : byHeight;

                m_Camera.orthographicSize = Mathf.Max(byHeight, byWidth) * (1f + viewMargin);
            }

            m_LastAspect = m_Camera.aspect;
        }

#if UNITY_EDITOR
        /// <summary>在 Scene 视图画出视域四角射线，方便核对图片是否被完整覆盖</summary>
        private void OnDrawGizmosSelected()
        {
            var cam = GetComponent<Camera>();
            if (cam == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 50f);
        }
#endif
    }
}
