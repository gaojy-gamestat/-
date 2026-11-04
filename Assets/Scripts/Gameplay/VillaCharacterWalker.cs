using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 2D 序列帧角色：在 <see cref="VillaGroundPlane"/> 生成的 3D 平面碰撞体上行走。
    ///
    /// ============================ 挂在哪里 ============================
    /// 挂在角色物体上（现在的「Oscar_玩家」：SpriteRenderer + Animator）。
    ///
    /// ============================ 为什么还要“镜像”一份到 UI ============================
    /// 别墅图是 <b>Screen Space - Overlay</b> 的 UI，而 Overlay 永远画在所有 3D 物体之上，
    /// 世界空间里的 SpriteRenderer 会被别墅图完全挡住（看不见）。
    /// 本脚本默认（mirrorToUI = true）把角色的<b>当前帧画面</b>和<b>屏幕位置</b>镜像到一个 UI Image：
    ///   * 逻辑位置仍然是地面上的真实 3D 坐标（吃得到地面碰撞体，以后接墙体/物理都没问题）；
    ///   * 画面显示在别墅图之上、HUD 之下，并且跟着地面做透视缩放。
    ///
    /// 一句话：<b>3D 负责“在哪”，UI 负责“看得见”</b>。
    /// 如果哪天把 Canvas 改成 Screen Space - Camera，把 mirrorToUI 关掉就能直接看 3D 角色。
    ///
    /// ============================ 操作 ============================
    /// WASD 或 方向键。方向按“画面方向”解释：
    ///   上 = 走进画面深处（远离相机）→ 角色背对镜头 → 播「向后」（后.png，背面素材）
    ///   下 = 走向观众          → 角色面朝镜头 → 播「向前」（前.png，正面素材）
    ///   左 = 往左              → 播「向左」
    ///   右 = 往右              → 播「向左」+ 水平翻转
    /// 注意：素材命名里「前」= 角色走向观众（正面朝镜头）、「后」= 背对观众，
    /// 以前把这两个状态接反了，造成“W 面朝镜头倒退走、S 背对镜头往镜头走”的反转问题，
    /// 本次已修正为上面这个正确对应关系。
    ///
    /// ============================ 和序列帧素材对齐的两个参数 ============================
    /// 你的序列帧切出来的每个 sprite 是「只含角色本人的裁剪框」（约 339×626，轴心在脚底），
    /// 不是带空白的大单帧——所以镜像显示出来就是角色本体的大小。两个参数对着调：
    ///   frameHeightRatioOfVilla：角色显示高度 = 别墅图高度的百分之多少（决定角色多大；
    ///                            也可以直接改角色 transform 缩放，效果叠加，Play 中实时生效）；
    ///   characterAnchorInFrame ：角色“脚”在 sprite 里的位置（0~1，sprite 左下角为原点），
    ///                            裁剪框脚底居中 → 默认 (0.5, 0)。
    /// </summary>
    [DisallowMultipleComponent]
    public class VillaCharacterWalker : MonoBehaviour
    {
        [Header("依赖（留空自动找）")]
        public VillaGroundPlane ground;
        [Tooltip("别墅房间管理器（多地板/障碍/墙/门）。找到它就用新系统；没有才退回旧的单一地面。")]
        public VillaRoomBuilder room;
        public Camera targetCamera;

        [Header("移动")]
        [Tooltip("速度：每秒走过“地板宽度”的百分之几。用比例而不是绝对速度，相机/世界尺寸怎么变都不怕。")]
        public float speedInFloorWidths = 0.28f;
        [Tooltip("出生点：别墅图上的归一化坐标（0~1，左上角为原点）。默认落在走廊/厨房那一带。")]
        public Vector2 spawnImageNorm = new Vector2(0.58f, 0.83f);
        [Tooltip("站着不动时把序列帧定格，避免原地踏步。")]
        public bool freezeAnimationWhenIdle = true;

        [Header("动画（Animator 状态名，控制器里没有就自动跳过）")]
        public Animator animator;
        [Tooltip("W / 上：走进画面深处（远离镜头）→ 角色背对镜头。素材是「向后」(后.png)。")]
        public string walkAwayState = "向后";
        [Tooltip("S / 下：走向镜头 → 角色面朝镜头。素材是「向前」(前.png)。")]
        public string walkTowardState = "向前";
        [Tooltip("往左走的状态名。")]
        public string leftState = "向左";
        [Tooltip("往右走没有单独状态时：用「向左」的状态 + 水平翻转。")]
        public bool flipForRight = true;

        [Header("阻挡与门")]
        [Tooltip("角色半径 = 地板宽度的百分之几（贴墙判定用，防止穿进柜子/墙）。")]
        [Range(0f, 0.1f)] public float characterRadiusInFloorWidths = 0.02f;

        [Header("显示")]
        [Tooltip("把角色镜像到 UI 上显示（Overlay 画布下必须开，否则角色被别墅图挡住）。")]
        public bool mirrorToUI = true;
        [Tooltip("想自己控制样式就拖一个 Image 进来；留空 = 运行时自动生成。")]
        public Image mirrorTarget;
        [Tooltip("角色显示高度 = 别墅图高度的百分之多少。也可以直接改角色的 Transform 缩放（叠加生效）。Play 中调这个值实时生效。")]
        [Range(0.02f, 1f)] public float frameHeightRatioOfVilla = 0.13f;
        [Tooltip("角色“脚”在 sprite 里的位置（0~1，sprite 左下角为原点）。\n" +
                 "序列帧切出来是“只含角色的裁剪框”，脚底居中 → 默认 (0.5, 0)。")]
        public Vector2 characterAnchorInFrame = new Vector2(0.5f, 0f);
        [Tooltip("按透视远近缩放：走进画面变小、走向观众变大。")]
        public bool scaleByDepth = true;
        [Tooltip("角色 sprite 的 Sorting Order（世界空间画法时用）。")]
        public int sortingOrder = 10;

        // ------------------------------------------------------------------
        // 运行时
        // ------------------------------------------------------------------
        private Camera _cam;
        private SpriteRenderer _spriteRenderer;
        private Image _villaImage;
        private Canvas _canvas;

        private RectTransform _mirrorRt;
        private Image _mirrorImage;
        private bool _mirrorReady;

        private bool _ready;
        private string _currentState;
        private bool _flipped;
        private float _lastDoorTime = -10f; // 上次传送时间（门冷却用）
        private bool _usingRoom;            // 当前是否已经在用房间系统（多地板限制）行走
        private float _nextRoomRetry;
        private int _roomRetryCount;

        /// <summary>角色脚底的世界坐标（地面上的真实位置）。</summary>
        public Vector3 FeetWorldPosition { get { return transform.position; } }

        /// <summary>角色目前在别墅图上的归一化坐标（0~1，左上为原点）。</summary>
        public Vector2 ImageNorm
        {
            get { return (ground != null && ground.IsReady) ? ground.GroundToImageNorm(transform.position) : spawnImageNorm; }
        }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------
        private void Start()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (_spriteRenderer != null)
            {
                _spriteRenderer.sortingOrder = sortingOrder;
            }

            TryInit();
        }

        private void Update()
        {
            if (!_ready && !TryInit())
            {
                return;
            }

            if (_cam == null)
            {
                return;
            }

            // 兼容模式升级：第 1 帧房间系统偶尔没就绪时会先走“旧单地面”兼容路径
            // （那条路没有地板限制，人能走到房顶/黑阁楼上）。这里持续重试，
            // 房间系统一旦就绪立刻切回多地板模式并把人放回地板上，绝不永久锁死在兼容模式。
            if (!_usingRoom)
            {
                TryUpgradeToRoom();
            }

            // 每帧自检：万一角色正好站在家具碰撞框里（出生点/传送落点被改过），
            // 四个方向都会被判阻挡，人就永远钉死。这里自动挪到最近的空地。
            EscapeIfStuck();

            Vector2 input = ReadInput();
            Move(input);
            PlayAnimation(input);
            CheckDoor();
        }

        private void LateUpdate()
        {
            if (_ready)
            {
                UpdateMirror();
            }
        }

        /// <summary>依赖准备好之前每帧尝试一次（别墅布局 / 地面需要先算出来）。</summary>
        private bool TryInit()
        {
            if (room == null)
            {
                room = FindObjectOfType<VillaRoomBuilder>();
            }

            if (room != null && !room.IsReady)
            {
                room.Rebuild();
            }

            // 新系统（多地板/障碍/墙/门）优先
            if (room != null && room.IsReady)
            {
                _cam = targetCamera != null ? targetCamera : Camera.main;
                if (_cam == null)
                {
                    return false;
                }

                // 出生位置：编辑态把角色拖到哪，Play 就从哪开始（必须落在某块地板上）；
                // 拖在地板外/没拖过 → 才用 spawnImageNorm 出生点。
                Teleport(GetStartImageNorm());
                // 双保险：出生点万一因数据问题落在所有地板之外（房顶/阁楼），立刻夹回最近地板
                transform.position = room.ClampToWorld(transform.position);
                EscapeIfStuck();
                _usingRoom = true;

                if (!mirrorToUI && _spriteRenderer != null)
                {
                    transform.rotation = _cam.transform.rotation;
                }

                PrepareMirror();
                _ready = true;
                return true;
            }

            // 退回旧的单一地面（没搭房间区域时的兼容路径）
            if (ground == null)
            {
                ground = FindObjectOfType<VillaGroundPlane>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (ground == null || !ground.IsReady || targetCamera == null)
            {
                return false;
            }

            _cam = targetCamera;

            // 落到地面上（用碰撞体往下打射线，打不到就用数学平面）
            Teleport(spawnImageNorm);

            // 世界空间画法时才需要摆正 sprite（UI 镜像模式下看不见它，不用动它）
            if (!mirrorToUI && _spriteRenderer != null)
            {
                transform.rotation = _cam.transform.rotation; // 贴图平面与画面平行 = 永远正对相机
                ApplyWorldScale();
            }

            PrepareMirror();
            _ready = true;
            _usingRoom = false; // 兼容模式：等房间系统就绪后由 TryUpgradeToRoom 升级
            return true;
        }

        /// <summary>
        /// 兼容模式 → 房间系统的升级：每 0.5 秒重试一次（最多 20 次），
        /// 一旦房间系统就绪，立刻把角色放回地板上并启用多地板限制。
        /// 没搭房间区域的项目里 room 永远是 null，此时兼容模式就是最终模式，直接标记完成。
        /// </summary>
        private void TryUpgradeToRoom()
        {
            if (room == null)
            {
                room = FindObjectOfType<VillaRoomBuilder>();
            }

            if (room == null)
            {
                _usingRoom = true; // 整个项目没有房间系统，兼容模式就是最终模式
                return;
            }

            if (room.IsReady)
            {
                SwitchToRoomMode();
                return;
            }

            if (Time.unscaledTime < _nextRoomRetry)
            {
                return;
            }

            _nextRoomRetry = Time.unscaledTime + 0.5f;
            if (++_roomRetryCount > 20)
            {
                Debug.LogWarning("[角色] 房间系统迟迟未就绪，放弃升级，继续用兼容地面行走。");
                _usingRoom = true;
                return;
            }

            room.Rebuild();
            if (room.IsReady)
            {
                SwitchToRoomMode();
            }
        }

        /// <summary>从兼容地面切回房间系统：把人放回地板，启用地板限制。</summary>
        private void SwitchToRoomMode()
        {
            Camera c = targetCamera != null ? targetCamera : Camera.main;
            if (c == null)
            {
                return; // 相机还没好，下一帧再试
            }

            _cam = c;
            Teleport(GetStartImageNorm());
            transform.position = room.ClampToWorld(transform.position); // 绝不悬在房顶/阁楼
            EscapeIfStuck();
            _usingRoom = true;
            Debug.Log("[角色] 房间系统已就绪，已切回多地板行走并把角色放回地板。");
        }

        // ------------------------------------------------------------------
        // 移动
        // ------------------------------------------------------------------
        private static Vector2 ReadInput()
        {
            float x = 0f;
            float y = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            return new Vector2(x, y);
        }

        private void Move(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f)
            {
                return;
            }

            // 把“屏幕上/下/左/右”换算成地面上的方向：
            // 相机的右方向 = 画面右；相机前方向压到水平面 = 画面里“往上走”（走进画面深处）
            Vector3 camRight = Vector3.ProjectOnPlane(_cam.transform.right, Vector3.up).normalized;
            Vector3 camForward = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;

            Vector3 dir = camRight * input.x + camForward * input.y;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            dir.Normalize();

            // 世界速度 = 地板宽度 × 比例（不管世界多大、相机多远，手感一致）
            float refWidth = (room != null && room.IsReady) ? room.FloorWidth
                            : (ground != null ? ground.FloorWidth : 10f);
            float worldSpeed = Mathf.Max(0.01f, refWidth) * speedInFloorWidths;
            Vector3 next = transform.position + dir * worldSpeed * Time.deltaTime;

            if (room != null && room.IsReady)
            {
                // 新系统：障碍/墙阻挡（含沿墙滑动）+ 多地板范围夹取。
                // 方向和速度仍是上面算好的，这里只做“能不能走过去”的判定。
                next = room.ResolveMove(transform.position, next, CharacterRadiusNorm());
                next.y = room.SampleGroundHeight(next);
            }
            else
            {
                // 旧系统兼容：单一地板范围夹取 + 碰撞体贴地
                // 1) 夹在地板范围内 2) 贴到地面碰撞体上
                next = ground.ClampToFloor(next);
                next.y = SampleGroundHeight(next);
            }

            transform.position = next;
        }

        /// <summary>角色半径换算成图坐标（0~1）：贴墙判定用。</summary>
        private float CharacterRadiusNorm()
        {
            // 世界半径 = 地板宽 × 比例；地板大约占图宽 68%，按此折回图坐标
            return Mathf.Clamp(characterRadiusInFloorWidths * 0.68f, 0.0005f, 0.05f);
        }

        /// <summary>
        /// 脱困：角色若正好站在家具/墙的碰撞框里，四个方向都会被判阻挡，
        /// 表现就是「人物卡在这里完全动不了」。
        ///
        /// 判定很克制：先要求「当前位置被挡」，再要求「前后左右各探一步仍全被挡」，
        /// 两个条件都成立才算真卡死。这样贴着家具边缘正常行走（至少一个方向能走）
        /// 不会被误伤、不会每帧把角色弹一下。
        /// 真卡死时才由近及远螺旋搜索一个「落在某块地板内 + 不被阻挡」的位置挪过去。
        /// </summary>
        private void EscapeIfStuck()
        {
            if (room == null || !room.IsReady) return;

            float radius = CharacterRadiusNorm();
            Vector3 here = transform.position;
            if (!room.IsBlocked(here, radius)) return;   // 没被挡，正常

            // 四向探针：只要有一个方向能走，就说明只是贴着家具，不是卡死
            Vector2 origin = room.GroundToImageNorm(here);
            Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            const float probe = 0.022f;
            for (int d = 0; d < dirs.Length; d++)
            {
                Vector3 w = room.ImageNormToGround(origin + dirs[d] * probe);
                if (!room.IsBlocked(w, radius)) return;
            }

            IList<VillaFloorArea> floors = room.Floors;
            float step = Mathf.Max(0.02f, radius * 1.5f);
            for (float r = step; r <= 0.32f; r += step)
            {
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI * 2f / 16f;
                    Vector2 n = origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (n.x < 0f || n.x > 1f || n.y < 0f || n.y > 1f) continue;

                    // 必须真的落在某块地板里，否则 ClampToWorld 又会把人拽回框里
                    bool onFloor = false;
                    for (int k = 0; k < floors.Count; k++)
                    {
                        VillaFloorArea f = floors[k];
                        if (f != null && f.enabled && f.gameObject.activeInHierarchy &&
                            f.ContainsImageNorm(n))
                        {
                            onFloor = true;
                            break;
                        }
                    }
                    if (!onFloor) continue;

                    Vector3 w = room.ImageNormToGround(n);
                    if (room.IsBlocked(w, radius)) continue;

                    Teleport(n);
                    Debug.LogWarning("[角色] 当前位置被家具碰撞框困住，已自动脱困到 " + n);
                    return;
                }
            }
        }

        /// <summary>
        /// 门检测：走到门区域 → 传送到目标门旁的落点（房间之间不直接连通，靠门切换）。
        /// 传送后有一小段冷却（门的 retriggerCooldown），防止落地即被送回。
        /// </summary>
        private void CheckDoor()
        {
            if (room == null || !room.IsReady)
            {
                return;
            }

            VillaDoor door = room.FindDoorAt(transform.position);
            if (door == null)
            {
                return;
            }

            if (Time.time - _lastDoorTime < door.retriggerCooldown)
            {
                return;
            }

            _lastDoorTime = Time.time;
            if (door.targetDoor == null)
            {
                return; // 没指定目标门：先不传送，只当装饰
            }

            Teleport(door.GetSpawnImageNorm());
        }

        /// <summary>用地面碰撞体求高度（打不到就退回数学平面高度）。</summary>
        private float SampleGroundHeight(Vector3 position)
        {
            Collider col = ground.GroundCollider;
            if (col != null)
            {
                var ray = new Ray(new Vector3(position.x, ground.groundHeight + 5f, position.z), Vector3.down);
                RaycastHit hit;
                if (col.Raycast(ray, out hit, 50f))
                {
                    return hit.point.y;
                }
            }

            return ground.groundHeight;
        }

        /// <summary>
        /// 进 Play 的初始位置：角色当前（编辑态摆放的）位置落在某块地板内 → 就用这个位置，
        /// 保证「拖到哪、Play 就从哪开始」；不在任何地板上 → 用 spawnImageNorm 出生点。
        /// </summary>
        private Vector2 GetStartImageNorm()
        {
            if (room != null && room.IsReady)
            {
                Vector2 n = room.GroundToImageNorm(transform.position);
                // 编辑态摆放的位置如果正好压在家具碰撞框上，进 Play 就会把人钉死，
                // 这种情况直接忽略、改用下面配好的安全出生点。
                if (room.IsBlocked(transform.position, CharacterRadiusNorm()))
                {
                    return SafeSpawn();
                }
                IList<VillaFloorArea> floors = room.Floors;
                for (int i = 0; i < floors.Count; i++)
                {
                    VillaFloorArea f = floors[i];
                    if (f != null && f.enabled && f.gameObject.activeInHierarchy && f.ContainsImageNorm(n))
                    {
                        return n;
                    }
                }
            }

            return SafeSpawn();
        }

        /// <summary>
        /// 安全出生点：配置的出生点必须「落在某块地板上 + 不贴任何家具碰撞框」才可用；
        /// 否则自动退到某块地板的中心，保证角色永远不会出生在房顶/阁楼/家具里。
        /// </summary>
        private Vector2 SafeSpawn()
        {
            if (room == null || !room.IsReady)
            {
                return spawnImageNorm;
            }

            float radius = CharacterRadiusNorm();
            IList<VillaFloorArea> floors = room.Floors;

            bool onFloor = false;
            for (int i = 0; i < floors.Count; i++)
            {
                VillaFloorArea f = floors[i];
                if (f != null && f.enabled && f.gameObject.activeInHierarchy &&
                    f.ContainsImageNorm(spawnImageNorm))
                {
                    onFloor = true;
                    break;
                }
            }

            if (onFloor && !room.IsBlocked(room.ImageNormToGround(spawnImageNorm), radius))
            {
                return spawnImageNorm;
            }

            // 出生点不安全（不在地板上 / 压着家具 / 数据被改坏）：逐块地板找中心，
            // 用第一块「中心不被挡」的地板中心出生。
            for (int i = 0; i < floors.Count; i++)
            {
                VillaFloorArea f = floors[i];
                if (f == null || !f.enabled || !f.gameObject.activeInHierarchy) continue;
                Vector2 c = f.CenterImageNorm;
                if (!room.IsBlocked(room.ImageNormToGround(c), radius))
                {
                    Debug.LogWarning("[角色] 配置的出生点不安全，已自动改用「" + f.name + "」中心出生。");
                    return c;
                }
            }

            return spawnImageNorm;
        }

#if UNITY_EDITOR
        /// <summary>编辑态右键菜单：把角色摆到出生点（在 Scene 里看看它到底在哪）。</summary>
        [ContextMenu("把角色放到出生点（编辑态预览）")]
        private void MoveToSpawnInEditor()
        {
            if (room == null)
            {
                room = FindObjectOfType<VillaRoomBuilder>();
            }

            if (room != null && !room.IsReady)
            {
                room.Rebuild();
            }

            if (room != null && room.IsReady)
            {
                Teleport(spawnImageNorm);
                return;
            }

            if (ground == null)
            {
                ground = FindObjectOfType<VillaGroundPlane>();
            }

            if (ground != null && ground.IsReady)
            {
                Teleport(spawnImageNorm);
            }
            else
            {
                Debug.LogWarning("还没找到别墅房间/地面，无法放置。");
            }
        }
#endif

        /// <summary>直接放到别墅图上的某个位置（归一化 0~1，左上为原点）。</summary>
        public void Teleport(Vector2 imageNorm)
        {
            if (room != null && room.IsReady)
            {
                Vector3 p = room.ImageNormToGround(imageNorm);
                p.y = room.SampleGroundHeight(p);
                transform.position = p;
                return;
            }

            if (ground == null || !ground.IsReady)
            {
                return;
            }

            Vector3 p2 = ground.ImageNormToGround(imageNorm);
            p2.y = SampleGroundHeight(p2);
            transform.position = p2;
        }

        // ------------------------------------------------------------------
        // 序列帧动画
        // ------------------------------------------------------------------
        private void PlayAnimation(Vector2 input)
        {
            bool moving = input.sqrMagnitude > 0.0001f;
            string state;
            bool flip;

            if (!moving)
            {
                state = _currentState; // 站着不动：保持最后一次的朝向（freezeAnimationWhenIdle 会定格画面）
                flip = _flipped;
                if (string.IsNullOrEmpty(state))
                {
                    state = walkAwayState;
                }
            }
            else if (Mathf.Abs(input.y) >= Mathf.Abs(input.x))
            {
                // W（走进画面深处）→ 背对镜头 = 「向后」素材；
                // S（走向观众）   → 面朝镜头 = 「向前」素材。之前接反了，这里已修正。
                state = input.y > 0f ? walkAwayState : walkTowardState;
                flip = false;
            }
            else if (input.x < 0f)
            {
                state = leftState;
                flip = false;
            }
            else
            {
                // 往右：没有单独的“向右”序列帧，用「向左」+ 水平翻转
                state = leftState;
                flip = flipForRight;
            }

            if (animator != null)
            {
                if (freezeAnimationWhenIdle)
                {
                    animator.speed = moving ? 1f : 0f;
                }

                if (state != _currentState && !string.IsNullOrEmpty(state))
                {
                    int hash = Animator.StringToHash(state);
                    if (animator.HasState(0, hash))
                    {
                        animator.Play(hash, 0, 0f);
                    }

                    _currentState = state;
                }
            }

            _flipped = flip;
            if (_spriteRenderer != null)
            {
                _spriteRenderer.flipX = flip;
            }

            if (_mirrorRt != null)
            {
                Vector3 s = _mirrorRt.localScale;
                s.x = Mathf.Abs(s.x) * (flip ? -1f : 1f);
                _mirrorRt.localScale = s;
            }
        }

        // ------------------------------------------------------------------
        // UI 镜像
        // ------------------------------------------------------------------
        private void PrepareMirror()
        {
            if (!mirrorToUI || _mirrorReady || _spriteRenderer == null)
            {
                return;
            }

            _villaImage = GetComponent<Image>();
            if (_villaImage == null && ground != null)
            {
                _villaImage = ground.GetComponent<Image>();
            }

            if (_villaImage == null)
            {
                GameObject go = GameObject.Find("别墅");
                _villaImage = go != null ? go.GetComponent<Image>() : null;
            }

            if (_villaImage == null)
            {
                return;
            }

            _canvas = _villaImage.GetComponentInParent<Canvas>();
            Transform parent = _villaImage.transform.parent;
            if (parent == null || _canvas == null)
            {
                return;
            }

            if (mirrorTarget != null)
            {
                _mirrorImage = mirrorTarget;
                _mirrorRt = mirrorTarget.rectTransform;
            }
            else
            {
                // 运行时生成一张 UI Image（HUD 仍在更上层）。
                //
                // ⚠️ 必须挂进别墅图【里面】，不能挂在它旁边：
                // Unity 嵌套 Canvas 的 sortingOrder 只在「父 Canvas 的排序空间」内生效。
                // 家具的排序 Canvas 都在别墅 Canvas 里面；镜像若挂在外面，
                // 别墅（连同全部家具）作为整体拿 -20000 去和镜像比较 → 家具永远盖不到角色，
                // 表现就是"人可以走上沙发/床，家具全都挡不住人"。
                // 挂进去之后，镜像和家具在同一个排序空间里按 sortingOrder 排，遮挡才成立。
                Transform host = _villaImage.transform;
                var go = new GameObject("角色镜像(运行时生成)", typeof(RectTransform), typeof(Image));
                go.hideFlags = HideFlags.DontSave;
                _mirrorRt = (RectTransform)go.transform;
                _mirrorRt.SetParent(host, false);
                _mirrorImage = go.GetComponent<Image>();
                _mirrorImage.raycastTarget = false;
            }

            _mirrorRt.anchorMin = new Vector2(0.5f, 0.5f);
            _mirrorRt.anchorMax = new Vector2(0.5f, 0.5f);
            _mirrorRt.pivot = new Vector2(0.5f, 0.5f);
            _mirrorRt.localScale = Vector3.one;
            _mirrorImage.preserveAspect = true;
            _mirrorImage.raycastTarget = false;

            // 前后顺序交给 VillaDepthSorter 的 sortingOrder（镜像现在就在别墅子树内，
            // 和家具在同一个排序空间里）。这里只把它排到最后，避免加载瞬间被地板盖住。
            if (mirrorTarget == null && _mirrorRt.parent != null)
            {
                _mirrorRt.SetSiblingIndex(_mirrorRt.parent.childCount - 1);
            }

            _mirrorReady = true;
        }

        private void UpdateMirror()
        {
            if (!_mirrorReady || _mirrorRt == null || _mirrorImage == null || _canvas == null)
            {
                return;
            }

            // 1) 贴图：直接抄世界 SpriteRenderer 当前帧（Animator 播到哪就是哪）
            _mirrorImage.sprite = _spriteRenderer != null ? _spriteRenderer.sprite : null;
            _mirrorImage.enabled = _mirrorImage.sprite != null;
            if (_mirrorImage.sprite == null)
            {
                return;
            }

            // 2) 尺寸：角色高度 = 别墅图高度 × 比例 × 角色Transform缩放（Play 中调哪个都实时生效）
            float villaHeight = _villaImage.rectTransform.rect.height;
            Sprite sp = _mirrorImage.sprite;
            float aspect = sp.rect.height > 0.01f ? sp.rect.width / sp.rect.height : 1f;
            float sizeScale = Mathf.Max(0.01f, Mathf.Abs(transform.localScale.y));
            float frameH = Mathf.Max(1f, villaHeight * frameHeightRatioOfVilla * DepthScale() * sizeScale);
            Vector2 size = new Vector2(frameH * aspect, frameH);
            _mirrorRt.sizeDelta = size;

            // 3) 位置：脚底世界坐标 → 屏幕像素 → 画布局部坐标
            Vector3 screen = _cam.WorldToScreenPoint(transform.position);
            if (screen.z <= 0f)
            {
                _mirrorImage.enabled = false; // 跑到相机背后了
                return;
            }

            var canvasRt = _canvas.transform as RectTransform;
            Vector2 local;
            if (canvasRt == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRt, screen, _canvas.worldCamera, out local))
            {
                return;
            }

            // 单帧里“脚”的位置（翻转时 x 镜像）→ 换算成相对轴心的偏移 → 让脚落在 local 上
            Vector2 anchor = characterAnchorInFrame;
            if (_flipped)
            {
                anchor.x = 1f - anchor.x;
            }

            Vector2 offset = new Vector2((anchor.x - 0.5f) * size.x, (anchor.y - 0.5f) * size.y);
            _mirrorRt.anchoredPosition = local - offset;
        }

        /// <summary>透视缩放：以“地板中心”的深度为 1，越靠观众越大。</summary>
        private float DepthScale()
        {
            if (!scaleByDepth || _cam == null || ground == null)
            {
                return 1f;
            }

            Vector3 f = _cam.transform.forward;
            float depth = Vector3.Dot(transform.position - _cam.transform.position, f);
            float refDepth = Vector3.Dot(ground.Center - _cam.transform.position, f);
            if (depth < 0.01f || refDepth < 0.01f)
            {
                return 1f;
            }

            return Mathf.Clamp(refDepth / depth, 0.6f, 1.6f);
        }

        /// <summary>世界空间画法时：把 sprite 缩放到“在屏幕上就是配置里那个高度”。</summary>
        private void ApplyWorldScale()
        {
            if (_spriteRenderer == null || _spriteRenderer.sprite == null || _cam == null || ground == null)
            {
                return;
            }

            Rect img = ground.ImageScreenRect;
            float wantedPixels = img.height * frameHeightRatioOfVilla;
            float depth = Vector3.Dot(transform.position - _cam.transform.position, _cam.transform.forward);
            if (depth < 0.01f)
            {
                return;
            }

            float worldHeight = 2f * depth * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad)
                                * (wantedPixels / Mathf.Max(1f, Screen.height));
            float spriteHeight = _spriteRenderer.sprite.rect.height / _spriteRenderer.sprite.pixelsPerUnit;
            if (spriteHeight > 0.0001f)
            {
                transform.localScale = Vector3.one * (worldHeight / spriteHeight);
            }
        }
    }
}
