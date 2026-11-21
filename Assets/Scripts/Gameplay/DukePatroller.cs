using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 房主杜克的巡逻系统：按路线依次到点 → 播动作 → 停留 → 走下一站，循环往复。
    ///
    /// ============================ 挂在哪里 ============================
    /// 把 Assets/Duke/Duke.prefab 拖进第一关场景，再把这个脚本挂到它身上
    /// （Prefab 自带 SpriteRenderer + Animator，状态名 WatchTV/Eat/Sleep/WalkFront/WalkBack/WalkLeft/WalkRight）。
    ///
    /// ============================ 显示方式 ============================
    /// 和玩家一样：别墅图是 Overlay UI，世界里的 SpriteRenderer 看不见，
    /// 本脚本运行时生成一张「杜克镜像(运行时生成)」UI Image 叠在别墅图上
    /// （VillaDepthSorter 会自动把它当家具一起做前后遮挡排序）。
    ///
    /// ============================ 怎么配置路线 ============================
    /// Patrol Stops 里按顺序加站点，每一站填：
    ///   * 地点：两种给法（二选一，Floor Area 拖了就优先用它）——
    ///       - Floor Area：把场景里那块「地板区域」(VillaFloorArea，比如客厅地板) 拖进来，
    ///         停靠点 = 地板中心 + Offset（图坐标微调，站在沙发前就把 Offset 往沙发方向挪）；
    ///       - Image Norm：直接填别墅图归一化坐标 (0~1，左上为原点)。
    ///   * Action：到达后播的动作（WatchTV 看电视 / Eat 进食 / Sleep 睡觉）；
    ///   * Stay Seconds：这一站停留多少秒；
    ///   * Anchor In Frame：动作画面里“对齐点”在 sprite 里的位置（0~1，左下为原点），
    ///     (-1,-1) = 自动按动作取默认值（看电视对齐座位、睡觉对齐床中心、进食对齐脚底）。
    /// 播完最后一站回到第一站，一直循环；玩家通关后调用 StopPatrol() 让他停下睡觉。
    ///
    /// ============================ 调点位的技巧 ============================
    /// Play 模式里按 F9，Console 会打印杜克当前的图坐标，直接抄到站点 Image Norm 里。
    /// </summary>
    [DisallowMultipleComponent]
    public class DukePatroller : MonoBehaviour
    {
        public enum DukeAction
        {
            WatchTV, // 看电视（对齐座位/骨盆）
            Eat,     // 进食（对齐脚底）
            Sleep,   // 睡觉（对齐床中央）
        }

        /// <summary>一个停靠点 = 一个地点 + 一个动作 + 停留时长。</summary>
        [Serializable]
        public class PatrolStop
        {
            [Tooltip("站点名字，纯备注用，比如「客厅沙发看电视」。")]
            public string stopName = "停靠点";

            [Header("地点（Floor Area 拖了就优先用它）")]
            [Tooltip("场景里的地板区域（如客厅地板）：停靠点 = 区域中心 + Offset。")]
            public VillaFloorArea floorArea;
            [Tooltip("在地板中心基础上再挪多少（图坐标 0~1）。想坐沙发就把点往沙发方向挪。")]
            public Vector2 offset = Vector2.zero;

            [Tooltip("不拖 Floor Area 时直接用这个图坐标（0~1，左上为原点）。")]
            public Vector2 imageNorm = new Vector2(0.5f, 0.8f);
            [Tooltip("动作显示位置；负值使用行走落点。坐下/躺下时与可行走的接近点分开。") ]
            public Vector2 activityNorm = new Vector2(-1f, -1f);

            [Header("到达后做什么")]
            public DukeAction action = DukeAction.WatchTV;
            [Tooltip("停留秒数（动作是循环的，停留多久播多久）。")]
            public float staySeconds = 10f;
            [Tooltip("动作画面的对齐点在 sprite 里的位置（0~1，左下为原点）。(-1,-1) = 按动作自动取默认。")]
            public Vector2 anchorInFrame = new Vector2(-1f, -1f);
            [Header("从上一站走到这里")]
            [Tooltip("这一段步行总时长。行走速度按路线长度自动计算。")]
            [Min(0.1f)] public float travelSeconds = 10f;
            [Tooltip("归一化别墅图路点（左上为原点），按顺序填写。经过配对 VillaDoor 时会使用门传送。")]
            public List<Vector2> routePoints = new List<Vector2>();
        }

        [Header("依赖（留空自动找）")]
        [Tooltip("用来做坐标换算的房间管理器（挂在「别墅」图上）。留空自动找。")]
        public VillaRoomBuilder room;
        [Tooltip("旧版单一地面（没搭房间区域时的兼容路径）。留空自动找。")]
        public VillaGroundPlane ground;
        public Camera targetCamera;
        [Tooltip("可选：倒计时结束时停止巡逻；留空自动查找。")]
        public CountdownSystem countdownSystem;

        [Header("巡逻路线（按顺序走，走完最后一站回到第一站循环）")]
        public List<PatrolStop> stops = new List<PatrolStop>
        {
            new PatrolStop { stopName = "客厅沙发看电视", action = DukeAction.WatchTV,
                staySeconds = 10f, travelSeconds = 10f,
                imageNorm = new Vector2(0.519f, 0.790f), activityNorm = new Vector2(0.514f, 0.724f),
                routePoints = new List<Vector2> { new Vector2(0.623f, 0.512f), new Vector2(0.614f, 0.478f),
                    new Vector2(0.595f, 0.790f), new Vector2(0.563f, 0.780f),
                    new Vector2(0.519f, 0.802f) } },
            new PatrolStop { stopName = "厨房餐桌右侧吃饭", action = DukeAction.Eat,
                staySeconds = 10f, travelSeconds = 10f,
                imageNorm = new Vector2(0.847f, 0.785f),
                routePoints = new List<Vector2> { new Vector2(0.543f, 0.780f),
                    new Vector2(0.595f, 0.790f), new Vector2(0.631f, 0.780f),
                    new Vector2(0.690f, 0.815f), new Vector2(0.840f, 0.815f) } },
            new PatrolStop { stopName = "主卧床上睡觉", action = DukeAction.Sleep,
                staySeconds = 10f, travelSeconds = 10f,
                imageNorm = new Vector2(0.642f, 0.512f), activityNorm = new Vector2(0.690f, 0.474f),
                routePoints = new List<Vector2> { new Vector2(0.840f, 0.815f),
                    new Vector2(0.690f, 0.815f), new Vector2(0.656f, 0.780f),
                    new Vector2(0.596f, 0.730f), new Vector2(0.596f, 0.690f),
                    new Vector2(0.623f, 0.512f) } },
        };

        [Header("移动")]
        [Tooltip("步行速度：每秒走过「参考地板宽度」的百分之几（和玩家的 speedInFloorWidths 同一套换算）。")]
        public float speedInFloorWidths = 0.25f;
        [Tooltip("与家具边缘保持的图片归一化间距。") ]
        [Min(0.001f)] public float clearanceNorm = 0.004f;
        [Tooltip("换算移动方向用的坐标差阈值（图坐标）。小于它不算换了方向。")]
        public float directionThreshold = 0.004f;

        [Header("动画状态名（Duke.controller 里的名字，一般不用改）")]
        public string walkFrontState = "WalkFront";
        public string walkBackState = "WalkBack";
        public string walkLeftState = "WalkLeft";
        public string walkRightState = "WalkRight";
        [Tooltip("调用 StopPatrol()（玩家通关）后杜克播放的动作。")]
        public DukeAction stopAction = DukeAction.Sleep;

        [Header("显示")]
        [Tooltip("杜克显示高度 = 别墅图高度的百分之多少（也可以改 Transform 缩放叠加微调）。")]
        [Range(0.02f, 1f)] public float heightRatioOfVilla = 0.115f;
        [Tooltip("按透视远近缩放：走近镜头变大、走远变小。")]
        public bool scaleByDepth = true;
        [Tooltip("杜克 sprite 的 Sorting Order（世界空间画法时用；镜像排序由 VillaDepthSorter 管）。")]
        public int sortingOrder = 10;
        [Tooltip("Play 模式按 F9 打印当前图坐标（调点位用）。")]
        public bool enableDebugLog = true;

        // ------------------------------------------------------------------
        // 运行时状态
        // ------------------------------------------------------------------
        private Camera _cam;
        private SpriteRenderer _spriteRenderer;
        private Animator _animator;
        private Image _villaImage;
        private Canvas _canvas;

        private RectTransform _mirrorRt;
        private Image _mirrorImage;
        private DukeFurnitureForeground _furnitureForeground;
        private bool _mirrorReady;

        private bool _ready;
        private bool _patrolling = true;   // false = 已被 StopPatrol() 停下
        private int _stopIndex;
        private enum Phase { Acting, Walking }
        private Phase _phase = Phase.Acting;
        private float _phaseTimer;
        private float _walkTimer;
        private float _walkDuration;
        private float _walkSpeed;
        private readonly List<Vector2> _activePath = new List<Vector2>();
        private int _activePathIndex;
        private float _lastDoorTime = -10f;
        private string _currentAnim;

        /// <summary>杜克当前在别墅图上的归一化坐标（0~1，左上为原点）。</summary>
        public Vector2 ImageNorm
        {
            get { return ToNorm(transform.position); }
        }

        /// <summary>杜克脚底/对齐点的世界坐标。</summary>
        public Vector3 WorldPosition { get { return transform.position; } }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------
        private void Start()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _animator = GetComponent<Animator>();
            if (_spriteRenderer != null)
            {
                _spriteRenderer.sortingOrder = sortingOrder;
            }

            if (countdownSystem == null)
            {
                countdownSystem = FindObjectOfType<CountdownSystem>();
            }
            if (countdownSystem != null)
            {
                countdownSystem.OnTimeUp += HandleTimeUp;
            }

            TryInit();
        }

        private void OnDestroy()
        {
            if (countdownSystem != null)
            {
                countdownSystem.OnTimeUp -= HandleTimeUp;
            }
        }

        private void Update()
        {
            if (!_ready && !TryInit())
            {
                return;
            }

            if (enableDebugLog && Input.GetKeyDown(KeyCode.F9))
            {
                Debug.Log($"[杜克巡逻] 当前图坐标 = ({ImageNorm.x:F3}, {ImageNorm.y:F3})，" +
                          $"世界坐标 = {transform.position}。把这个坐标填到站点的 Image Norm 里即可。");
            }

            if (_patrolling)
            {
                if (_phase == Phase.Acting)
                {
                    UpdateActing();
                }
                else
                {
                    UpdateWalking();
                }
            }
        }

        private void LateUpdate()
        {
            if (_ready)
            {
                UpdateMirror();
            }
        }

        // ------------------------------------------------------------------
        // 对外接口（通关 / 被抓等事件调用）
        // ------------------------------------------------------------------

        /// <summary>停止巡逻（比如玩家通关了）：走到哪停哪，改播 stopAction 并一直循环。</summary>
        public void StopPatrol()
        {
            _patrolling = false;
            PlayAnim(ActionState(stopAction));
            Debug.Log("[杜克巡逻] 已停止巡逻（通关），播放动作：" + ActionState(stopAction));
        }

        /// <summary>恢复巡逻：从当前站重新开始（先播当前站动作，再继续走下一站）。</summary>
        public void ResumePatrol()
        {
            _patrolling = true;
            EnterStop(_stopIndex);
        }

        /// <summary>立刻跳到第 index 站（0 起）并开始播动作。</summary>
        public void JumpToStop(int index)
        {
            if (stops == null || stops.Count == 0)
            {
                return;
            }

            _stopIndex = Mathf.Clamp(index, 0, stops.Count - 1);
            Vector2 norm = StopNorm(stops[_stopIndex]);
            transform.position = ToGround(norm);
            EnterStop(_stopIndex);
        }

        // ------------------------------------------------------------------
        // 状态机
        // ------------------------------------------------------------------
        private void UpdateActing()
        {
            PatrolStop stop = CurrentStop();
            if (stop == null)
            {
                return;
            }

            _phaseTimer += Time.deltaTime;
            if (_phaseTimer >= Mathf.Max(0.1f, stop.staySeconds))
            {
                BeginWalking((_stopIndex + 1) % stops.Count);
            }
        }

        private void UpdateWalking()
        {
            PatrolStop destination = CurrentStop();
            if (destination == null)
            {
                return;
            }

            _walkTimer += Time.deltaTime;
            if (_activePathIndex < _activePath.Count)
            {
                Vector2 point = _activePath[_activePathIndex];
                Vector3 target = ToGround(point);
                Vector3 next = Vector3.MoveTowards(transform.position, target, _walkSpeed * Time.deltaTime);
                // 路线必须绕开障碍；被新家具挡住时停下并报告，不能穿过去或跳到终点。
                if (room != null && room.IsBlockedPath(CollisionPoint(transform.position), CollisionPoint(next), clearanceNorm))
                {
                    Debug.LogError($"[杜克巡逻] 路线被家具阻挡：{destination.stopName}，路点 {_activePathIndex}，图坐标 {ImageNorm}。", this);
                    enabled = false;
                    return;
                }
                transform.position = next;
                if ((target - transform.position).sqrMagnitude > 0.000001f) PlayAnim(PickWalkAnim(point));

                float arriveGap = 0.00001f * RefWidth();
                if ((target - transform.position).sqrMagnitude <= arriveGap * arriveGap)
                {
                    transform.position = target;
                    TraverseDoorAtCurrentPosition();
                    _activePathIndex++;
                }
            }

            if (_walkTimer >= _walkDuration && _activePathIndex >= _activePath.Count)
            {
                transform.position = ToGround(StopNorm(destination));
                EnterStop(_stopIndex);
            }
        }

        private void BeginWalking(int destinationIndex)
        {
            _stopIndex = destinationIndex;
            PatrolStop destination = CurrentStop();
            if (destination == null)
            {
                return;
            }

            _phase = Phase.Walking;
            _walkTimer = 0f;
            _walkDuration = Mathf.Max(0.1f, destination.travelSeconds);
            _activePath.Clear();
            if (destination.routePoints != null)
            {
                for (int i = 0; i < destination.routePoints.Count; i++)
                {
                    _activePath.Add(VillaZoneBase.SanitizeNorm(destination.routePoints[i]));
                }
            }

            Vector2 finalPoint = StopNorm(destination);
            if (_activePath.Count == 0 || Vector2.Distance(_activePath[_activePath.Count - 1], finalPoint) > 0.001f)
            {
                _activePath.Add(finalPoint);
            }

            _activePathIndex = 0;
            float pathLength = CalculateWalkPathLength();
            _walkSpeed = Mathf.Max(0.001f, Mathf.Min(pathLength / _walkDuration, SpeedWorld()));
            PlayAnim(PickWalkAnim(_activePath[0]));
        }

        private float CalculateWalkPathLength()
        {
            Vector3 cursor = transform.position;
            float total = 0f;
            for (int i = 0; i < _activePath.Count; i++)
            {
                Vector3 point = ToGround(_activePath[i]);
                total += Vector3.Distance(cursor, point);
                VillaDoor door = room != null && room.IsReady ? room.FindDoorAt(CollisionPoint(point)) : null;
                cursor = door != null && door.IsPaired
                    ? ToGround(door.GetSpawnImageNorm())
                    : point;
            }

            return total;
        }

        private void TraverseDoorAtCurrentPosition()
        {
            if (room == null || !room.IsReady)
            {
                return;
            }

            VillaDoor door = room.FindDoorAt(CollisionPoint(transform.position));
            if (door == null || !door.IsPaired ||
                Time.time - _lastDoorTime < Mathf.Max(0f, door.retriggerCooldown))
            {
                return;
            }

            transform.position = ToGround(door.GetSpawnImageNorm());
            _lastDoorTime = Time.time;
        }

        /// <summary>到达一站：切回“播动作”阶段。</summary>
        private void EnterStop(int index)
        {
            _stopIndex = index;
            _phase = Phase.Acting;
            _phaseTimer = 0f;

            PatrolStop stop = CurrentStop();
            if (stop != null)
            {
                PlayAnim(ActionState(stop.action));
                Debug.Log($"[杜克巡逻] 到达「{stop.stopName}」，播放 {stop.action}，停留 {stop.staySeconds:F1} 秒。");
            }
        }

        private void HandleTimeUp()
        {
            StopPatrol();
        }

        private PatrolStop CurrentStop()
        {
            if (stops == null || stops.Count == 0)
            {
                return null;
            }

            _stopIndex = Mathf.Clamp(_stopIndex, 0, stops.Count - 1);
            return stops[_stopIndex];
        }

        // ------------------------------------------------------------------
        // 动画
        // ------------------------------------------------------------------

        /// <summary>动作枚举 → Animator 状态名。</summary>
        private string ActionState(DukeAction action)
        {
            switch (action)
            {
                case DukeAction.Eat: return "Eat";
                case DukeAction.Sleep: return "Sleep";
                default: return "WatchTV";
            }
        }

        /// <summary>按这一站相对当前位置的图坐标差挑行走动画。</summary>
        private string PickWalkAnim(Vector2 targetNorm)
        {
            Vector2 cur = ToNorm(transform.position);
            Vector2 d = targetNorm - cur;

            bool vertical = Mathf.Abs(d.y) > Mathf.Abs(d.x);
            if (vertical)
            {
                // 图坐标 y 向下 = 靠近观众（同玩家：下 = WalkFront 走向镜头）
                return d.y > 0f ? walkFrontState : walkBackState;
            }

            return d.x > 0f ? walkRightState : walkLeftState;
        }

        /// <summary>只在动作真正变化时调 Play，避免每帧从 0 重播。</summary>
        private void PlayAnim(string stateName)
        {
            if (string.IsNullOrEmpty(stateName) || stateName == _currentAnim)
            {
                return;
            }

            _currentAnim = stateName;
            if (_animator != null && _animator.isActiveAndEnabled)
            {
                _animator.Play(stateName, 0, 0f);
            }
        }

        // ------------------------------------------------------------------
        // 初始化（别墅布局就绪前每帧重试）
        // ------------------------------------------------------------------
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

            if (_cam == null)
            {
                _cam = targetCamera != null ? targetCamera : Camera.main;
            }

            // 找别墅图（镜像要挂在它里面才能和家具互相遮挡）
            if (_villaImage == null)
            {
                GameObject go = GameObject.Find("别墅");
                _villaImage = go != null ? go.GetComponent<Image>() : null;
                if (_villaImage == null && room != null)
                {
                    _villaImage = room.GetComponent<Image>();
                }
            }

            if (_cam == null || _villaImage == null)
            {
                return false;
            }

            _canvas = _villaImage.GetComponentInParent<Canvas>();

            // 兼容模式：没有房间系统时退回旧地面（房间系统就绪与否都先能跑）
            if (room == null || !room.IsReady)
            {
                if (ground == null)
                {
                    ground = FindObjectOfType<VillaGroundPlane>();
                }

                if (ground == null || !ground.IsReady)
                {
                    return false;
                }
            }

            // 出生：第一站的位置（客厅）
            PatrolStop first = CurrentStop();
            if (first != null)
            {
                transform.position = ToGround(StopNorm(first));
            }

            PrepareMirror();
            EnterStop(_stopIndex);
            _ready = true;
            Debug.Log("[杜克巡逻] 初始化完成，从「" + (first != null ? first.stopName : "?") + "」开始巡逻。");
            return true;
        }

        // ------------------------------------------------------------------
        // 坐标换算
        // ------------------------------------------------------------------

        /// <summary>站点的图坐标：优先用 Floor Area 中心 + Offset，否则用 Image Norm。</summary>
        private Vector2 StopNorm(PatrolStop stop)
        {
            if (stop == null)
            {
                return new Vector2(0.5f, 0.8f);
            }

            if (stop.floorArea != null)
            {
                return stop.floorArea.CenterImageNorm + stop.offset;
            }

            return stop.imageNorm;
        }

        private Vector2 ToNorm(Vector3 world)
        {
            if (room != null && room.IsReady)
            {
                Vector2 rectNorm = room.GroundToImageNorm(world);
                Rect art = ArtworkRect();
                return new Vector2((rectNorm.x - art.x) / art.width, (rectNorm.y - art.y) / art.height);
            }

            if (ground != null && ground.IsReady)
            {
                return ground.GroundToImageNorm(world);
            }

            return Vector2.zero;
        }

        private Vector3 ToGround(Vector2 norm)
        {
            if (room != null && room.IsReady)
            {
                Rect art = ArtworkRect();
                return room.ImageNormToGround(new Vector2(art.x + norm.x * art.width, art.y + norm.y * art.height));
            }

            if (ground != null && ground.IsReady)
            {
                return ground.ImageNormToGround(norm);
            }

            return transform.position;
        }

        // Image.preserveAspect 会在 RectTransform 内留白，路点坐标以实际图像为准。
        private Rect ArtworkRect()
        {
            if (_villaImage == null || !_villaImage.preserveAspect || _villaImage.sprite == null)
                return new Rect(0f, 0f, 1f, 1f);
            Rect rect = _villaImage.rectTransform.rect;
            float spriteAspect = _villaImage.sprite.rect.width / _villaImage.sprite.rect.height;
            float rectAspect = rect.width / Mathf.Max(1f, rect.height);
            float width = Mathf.Min(1f, spriteAspect / rectAspect);
            float height = Mathf.Min(1f, rectAspect / spriteAspect);
            Vector2 pivot = _villaImage.rectTransform.pivot;
            return new Rect((1f - width) * pivot.x, (1f - height) * (1f - pivot.y), width, height);
        }

        // 现有区域的 Corners 存储的是图像坐标；在该坐标系内复用家具与门判定。
        private Vector3 CollisionPoint(Vector3 world)
        {
            return room.ImageNormToGround(ToNorm(world));
        }

        /// <summary>参考宽度（世界单位）：优先房间系统的参考地板宽度，否则退回地面宽度。</summary>
        private float RefWidth()
        {
            if (room != null && room.IsReady)
            {
                return Mathf.Max(0.01f, room.FloorWidth);
            }

            if (ground != null && ground.IsReady)
            {
                Vector3 a = ground.ImageNormToGround(new Vector2(0f, 0.9f));
                Vector3 b = ground.ImageNormToGround(new Vector2(1f, 0.9f));
                return Mathf.Max(0.01f, Mathf.Abs(b.x - a.x));
            }

            return 1f;
        }

        private float SpeedWorld()
        {
            return Mathf.Max(0.01f, speedInFloorWidths) * RefWidth();
        }

        /// <summary>透视缩放：以“地面中心”的深度为 1，越靠观众越大（同玩家逻辑）。</summary>
        private float DepthScale()
        {
            if (!scaleByDepth || _cam == null)
            {
                return 1f;
            }

            Vector3 refPoint;
            if (ground != null && ground.IsReady)
            {
                refPoint = ground.Center;
            }
            else if (room != null && room.IsReady)
            {
                refPoint = room.transform.position;
            }
            else
            {
                return 1f;
            }

            Vector3 f = _cam.transform.forward;
            float depth = Vector3.Dot(transform.position - _cam.transform.position, f);
            float refDepth = Vector3.Dot(refPoint - _cam.transform.position, f);
            if (depth < 0.01f || refDepth < 0.01f)
            {
                return 1f;
            }

            return Mathf.Clamp(refDepth / depth, 0.6f, 1.6f);
        }

        // ------------------------------------------------------------------
        // UI 镜像（同玩家的做法：3D 管逻辑位置，UI 管显示）
        // ------------------------------------------------------------------
        private void PrepareMirror()
        {
            if (_mirrorReady)
            {
                return;
            }

            Transform host = _villaImage.transform;
            _canvas = _villaImage.GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                return;
            }

            // 必须挂在别墅图里面，和家具在同一个排序空间（原因同玩家镜像）。
            var go = new GameObject("杜克镜像(运行时生成)", typeof(RectTransform), typeof(Image));
            go.hideFlags = HideFlags.DontSave;
            _mirrorRt = (RectTransform)go.transform;
            _mirrorRt.SetParent(host, false);
            _mirrorImage = go.GetComponent<Image>();
            _mirrorImage.raycastTarget = false;
            _mirrorRt.SetSiblingIndex(_mirrorRt.parent.childCount - 1);

            _mirrorRt.anchorMin = new Vector2(0.5f, 0.5f);
            _mirrorRt.anchorMax = new Vector2(0.5f, 0.5f);
            _mirrorRt.pivot = new Vector2(0.5f, 0.5f);
            _mirrorRt.localScale = Vector3.one;
            _mirrorImage.preserveAspect = true;

            var foreground = new GameObject("Duke家具前缘", typeof(RectTransform), typeof(DukeFurnitureForeground));
            foreground.hideFlags = HideFlags.DontSave;
            var frontRt = (RectTransform)foreground.transform;
            frontRt.SetParent(host, false);
            frontRt.anchorMin = Vector2.zero;
            frontRt.anchorMax = Vector2.one;
            frontRt.sizeDelta = Vector2.zero;
            frontRt.anchoredPosition = Vector2.zero;
            _furnitureForeground = foreground.GetComponent<DukeFurnitureForeground>();
            _mirrorReady = true;
        }

        private void UpdateMirror()
        {
            if (!_mirrorReady || _mirrorRt == null || _mirrorImage == null || _villaImage == null || _cam == null)
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

            // 2) 尺寸：杜克高度 = 别墅图高度 × 比例 × 透视 × Transform 缩放
            float villaHeight = _villaImage.rectTransform.rect.height * ArtworkRect().height;
            Sprite sp = _mirrorImage.sprite;
            float aspect = sp.rect.height > 0.01f ? sp.rect.width / sp.rect.height : 1f;
            float sizeScale = Mathf.Max(0.01f, Mathf.Abs(transform.localScale.y));
            float frameH = Mathf.Max(1f, villaHeight * heightRatioOfVilla * DepthScale() * sizeScale);
            Vector2 size = new Vector2(frameH * aspect, frameH);
            _mirrorRt.sizeDelta = size;

            // 3) 位置：对齐点（脚底/座位/床中心）世界坐标 → 屏幕像素 → 别墅图局部坐标
            Vector3 displayPosition = transform.position;
            PatrolStop displayStop = CurrentStop();
            if (_phase == Phase.Acting && displayStop != null && displayStop.activityNorm.x >= 0f)
                displayPosition = ToGround(displayStop.activityNorm);
            Vector3 screen = _cam.WorldToScreenPoint(displayPosition);
            if (screen.z <= 0f)
            {
                _mirrorImage.enabled = false;
                return;
            }

            var canvasRt = _villaImage.rectTransform;
            Vector2 local;
            if (canvasRt == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRt, screen, _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera, out local))
            {
                return;
            }

            Vector2 anchor = CurrentAnchor();

            Vector2 offset = new Vector2((anchor.x - 0.5f) * size.x, (anchor.y - 0.5f) * size.y);
            _mirrorRt.anchoredPosition = local - offset;
            if (_furnitureForeground != null)
            {
                bool acting = _phase == Phase.Acting && displayStop != null;
                _furnitureForeground.Configure(_villaImage, _mirrorRt,
                    acting && displayStop.action == DukeAction.WatchTV,
                    acting && displayStop.action == DukeAction.Sleep);
            }
        }

        /// <summary>当前对齐点：站点的 Anchor In Frame，没填 (-1,-1) 就按动作取默认值。</summary>
        private Vector2 CurrentAnchor()
        {
            if (_patrolling && _phase == Phase.Acting)
            {
                PatrolStop stop = CurrentStop();
                if (stop != null && stop.anchorInFrame.x >= 0f && stop.anchorInFrame.y >= 0f)
                {
                    return stop.anchorInFrame;
                }

                if (stop != null)
                {
                    return DefaultAnchor(stop.action);
                }
            }

            if (!_patrolling)
            {
                return DefaultAnchor(stopAction);
            }

            // 走路：脚底居中
            return new Vector2(0.5f, 0.07421875f);
        }

        /// <summary>各动作的默认对齐点（来自 Duke 素材说明的支点表）。</summary>
        private static Vector2 DefaultAnchor(DukeAction action)
        {
            switch (action)
            {
                case DukeAction.WatchTV: return new Vector2(0.54980469f, 0.32031250f); // 座位/骨盆
                case DukeAction.Eat:     return new Vector2(0.60546875f, 0.07714844f); // 脚底
                case DukeAction.Sleep:   return new Vector2(0.5f, 0.47265625f);        // 床中央
                default: return new Vector2(0.5f, 0.07421875f);
            }
        }
    }
}
