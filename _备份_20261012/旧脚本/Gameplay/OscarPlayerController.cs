using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 第一关玩家角色 Oscar 的操控脚本（WASD 行走 + 2D 序列帧动画切换）。
    ///
    /// 数据结构前提（由 VillaLevelBootstrap 自动创建/配置）：
    ///  - 本物体挂 SpriteRenderer + Animator（动画控制器--小胖子.controller）；
    ///    动画剪辑绑定的是 SpriteRenderer.m_Sprite（classID 212），
    ///    所以角色必须用 SpriteRenderer 播序列帧，不能用 UI Image；
    ///  - 角色精灵的轴心在脚底（导入设置 alignment=Bottom），
    ///    因此“物体位置”即脚底位置，直接站在行走面上；
    ///  - 角色不直接参与物理，逻辑位置保存为地面 XZ 坐标，
    ///    每帧夹紧在 VillaGround 的碰撞体范围内，再投影到相机前方图平面上显示。
    ///
    /// 操作与动画映射（画布 y 向上 = 远离相机方向）：
    ///   W（向画面深处走）→ 「向后」动画（背对镜头）
    ///   S（朝画面近处走）→ 「向前」动画（面朝镜头）
    ///   A（向左）         → 「向左」动画
    ///   D（向右）         → 「向左」动画 + flipX 水平翻转（美术只提供左向）
    /// 斜方向时取分量更大的一轴决定动画；松开按键后动画定格（Animator.speed=0）。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class OscarPlayerController : MonoBehaviour
    {
        private const string StateFront = "向前"; // 面朝镜头（S）
        private const string StateBack = "向后";  // 背对镜头（W）
        private const string StateLeft = "向左";  // 向左（A；D 时配合翻转）

        [Header("移动")]
        [Tooltip("行走速度（世界单位/秒）。地面宽约 14 单位，4 大约 3.5 秒横穿")]
        [SerializeField] private float moveSpeed = 4f;

        [Tooltip("角色缩放。原帧约 340×626px(PPU100)=3.4×6.3 单位；0.15 时高约 0.94 单位 ≈ 画面高度 10%")]
        [SerializeField] private float playerScale = 0.15f;

        [Tooltip("出生点（别墅图片像素坐标，图片左下角为原点）。默认约在图片中部")]
        [SerializeField] private Vector2 spawnImagePixel = new Vector2(838f, 300f);

        [Header("动画")]
        [Tooltip("行走动画播放速度（1=原速）。数值越大步频越快，越小越慢")]
        [SerializeField, Min(0.05f)] private float animationSpeed = 1f;

        [Header("自动接线（由 Bootstrap 填写，也可手动拖）")]
        [SerializeField] private VillaSceneCamera sceneCamera;
        [SerializeField] private VillaScenePlane scenePlane;
        [SerializeField] private VillaGround ground;

        [Tooltip("多区域行走（多楼层/多房间）。配置了它就优先于单块 ground")]
        [SerializeField] private VillaWalkAreas walkAreas;

        [Header("渲染层级")]
        [Tooltip("SpriteRenderer 排序：别墅图=0，角色=5，HUD=10，保证角色画在图上、HUD 之下")]
        [SerializeField] private int sortingOrder = 5;

        [Tooltip("角色相对别墅图平面向相机方向挪近的距离，确保稳定盖在图上")]
        [SerializeField] private float renderOffset = 0.5f;

        private SpriteRenderer m_Sprite;
        private Animator m_Animator;
        private Transform m_CamTransform;
        private Vector3 m_GroundPos;     // 逻辑地面位置（y=0 行走面）
        private string m_CurrentState;
        private bool m_SpawnFromScenePosition; // true=出生点取编辑器里摆放的位置

        /// <summary>外部接线（Bootstrap 调用，也可在 Inspector 手动拖引用）</summary>
        public void Wire(VillaSceneCamera camera, VillaScenePlane plane, VillaGround walkGround)
        {
            sceneCamera = camera;
            scenePlane = plane;
            ground = walkGround;
        }

        /// <summary>接入多区域行走（多楼层/多房间）；传 null 表示只用单块地面</summary>
        public void SetWalkAreas(VillaWalkAreas areas)
        {
            walkAreas = areas;
        }

        /// <summary>
        /// Bootstrap 调用：场景里已存在本物体（编辑器里摆好的）时，
        /// 出生点取它的摆放位置——即在 Scene 视图拖动 Oscar 就能决定出场位置。
        /// 必须在 Start 之前调用。
        /// </summary>
        public void UseScenePositionAsSpawn()
        {
            m_SpawnFromScenePosition = true;
        }

        /// <summary>角色当前所在的地面世界坐标（可作为交互判定的输入）</summary>
        public Vector3 GroundPosition => m_GroundPos;

        // ---- 供编辑器「写回场景」读取当前运行值 ----
        public float MoveSpeed => moveSpeed;
        public float PlayerScale => playerScale;
        public Vector2 SpawnImagePixel => spawnImagePixel;
        public float AnimationSpeed => animationSpeed;

        /// <summary>
        /// 外部（Bootstrap）覆盖默认参数。必须在 Start 之前调用
        /// （Bootstrap 在 Awake 里创建本物体，Start 要到下一阶段才跑，时序安全）。
        /// </summary>
        public void ConfigureForBootstrap(float speed, float scale, Vector2 spawnPixel, float animSpeed)
        {
            moveSpeed = speed;
            playerScale = scale;
            spawnImagePixel = spawnPixel;
            animationSpeed = animSpeed;
        }

        private void OnValidate()
        {
            // Play 模式下拖 Inspector 数值实时生效：
            // 速度/动画速度每帧都读字段天然实时；体型在这里立即应用
            if (Application.isPlaying)
            {
                transform.localScale = Vector3.one * playerScale;
            }
        }

        private void Start()
        {
            m_Sprite = GetComponent<SpriteRenderer>();
            m_Animator = GetComponent<Animator>();

            if (sceneCamera == null)
            {
                sceneCamera = FindObjectOfType<VillaSceneCamera>();
            }

            if (scenePlane == null)
            {
                scenePlane = FindObjectOfType<VillaScenePlane>();
            }

            if (ground == null)
            {
                ground = FindObjectOfType<VillaGround>();
            }

            if (walkAreas == null)
            {
                walkAreas = FindObjectOfType<VillaWalkAreas>();
            }

            var cam = sceneCamera != null ? sceneCamera.GetComponent<Camera>()
                                          : Camera.main;
            if (cam != null)
            {
                m_CamTransform = cam.transform;
                if (scenePlane != null)
                {
                    scenePlane.RenderCamera = cam;
                }
            }

            // 外观初始化
            transform.localScale = Vector3.one * playerScale;
            m_Sprite.sortingOrder = sortingOrder;
            m_Sprite.flipX = false;

            // 出生点：
            //  - 本物体是编辑器里摆好的（场景持久存在）→ 用【摆放位置的投影点】，
            //    于是“在 Scene 视图拖动 Oscar”就等于设置出生位置；
            //  - 运行时新建的物体 → 用 Bootstrap 给的图片像素出生点。
            if (m_SpawnFromScenePosition && m_CamTransform != null)
            {
                float sinPitch = sceneCamera != null ? Mathf.Max(0.01f, Mathf.Sin(sceneCamera.PitchRadians)) : 0.5f;
                Vector3 local = m_CamTransform.InverseTransformPoint(transform.position);
                m_GroundPos = new Vector3(local.x, 0f, local.y / sinPitch);
            }
            else if (ground != null)
            {
                m_GroundPos = ground.ImagePixelToGround(spawnImagePixel);
            }
            else
            {
                m_GroundPos = Vector3.zero;
            }

            // 出生点必须落在行走区域内：区域之间的空隙是“墙”，
            // 站在区域外会四面被挡、按 WASD 走不动（常见误配）
            if (walkAreas != null && !walkAreas.Contains(m_GroundPos))
            {
                Debug.LogWarning("[Oscar] 出生点不在任何行走区域内：角色会被卡住、按 WASD 走不动。" +
                                 "请选中 Oscar，把它拖到别墅图的地板区域内（会有黄色/彩色辅助线提示）。", this);
            }

            if (m_Animator != null)
            {
                m_Animator.Play(StateFront, 0, 0f);
                m_Animator.speed = 0f; // 出生时定格在第一帧
                m_CurrentState = StateFront;
            }
        }

        private void Update()
        {
            if (m_CamTransform == null)
            {
                return;
            }

            // ---------- 1) 读取 WASD / 方向键 ----------
            Vector3 dir = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dir.z += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dir.z -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dir.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dir.x += 1f;

            bool moving = dir.sqrMagnitude > 0.0001f;
            if (moving)
            {
                dir.Normalize();

                // ---------- 2) 在行走区域（多楼层/房间并集）上移动 ----------
                // 多区域模式：撞“墙”（区域之间的空隙）会沿单轴滑行；
                // 未配置多区域时退回旧的单块地面夹紧
                Vector3 target = m_GroundPos + dir * (moveSpeed * Time.deltaTime);
                m_GroundPos = walkAreas != null
                    ? walkAreas.ClampMove(m_GroundPos, target)
                    : ClampToGround(target);

                // ---------- 3) 按移动方向切换动画 ----------
                ApplyFacing(dir);
            }

            // 松开按键：动画定格（保持最后姿势，不会原地滑步）；
            // 按住时按 animationSpeed 播放（调大=步频快，调小=慢动作）
            if (m_Animator != null)
            {
                m_Animator.speed = moving ? animationSpeed : 0f;
            }
        }

        private void LateUpdate()
        {
            if (m_CamTransform == null)
            {
                return;
            }

            // ---------- 4) 画面位置：地面坐标 → 相机前方图平面（正交投影下屏幕位置一致） ----------
            //    角色面片与别墅图平面平行、向相机挪近 renderOffset，保证画在图之上。
            float distance = (sceneCamera != null ? sceneCamera.PlaneDistance : 50f) - renderOffset;
            Vector3 local = m_CamTransform.InverseTransformPoint(m_GroundPos);
            transform.position = m_CamTransform.position
                                  + m_CamTransform.right * local.x
                                  + m_CamTransform.up * local.y
                                  + m_CamTransform.forward * distance;

            // 角色面片正对相机（精灵轴心在脚底 → 位置即脚底，“站”在投影点上）
            transform.rotation = m_CamTransform.rotation;
        }

        /// <summary>把位置夹紧在行走地面碰撞体的 XZ 范围内（墙体/出界限制）</summary>
        private Vector3 ClampToGround(Vector3 pos)
        {
            if (ground == null || ground.Box == null)
            {
                return pos;
            }

            Bounds b = ground.Box.bounds;
            pos.x = Mathf.Clamp(pos.x, b.min.x, b.max.x);
            pos.z = Mathf.Clamp(pos.z, b.min.z, b.max.z);
            pos.y = 0f;
            return pos;
        }

        /// <summary>
        /// 按移动方向切换序列帧：
        /// 水平分量大 → 向左/向右（向右 = 向左动画 + flipX）；
        /// 垂直分量大 → 向前（朝镜头，S）/ 向后（背镜头，W）。
        /// </summary>
        private void ApplyFacing(Vector3 dir)
        {
            string state;
            bool flip;

            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.z))
            {
                state = StateLeft;
                flip = dir.x > 0f; // 向右走：镜像“向左”动画
            }
            else
            {
                state = dir.z > 0f ? StateBack : StateFront;
                flip = false;
            }

            m_Sprite.flipX = flip;

            if (m_Animator != null && state != m_CurrentState)
            {
                m_Animator.Play(state, 0, 0f);
                m_CurrentState = state;
            }
        }
    }
}
