using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 第一关“正交斜俯视场景”一键搭建（运行时执行，不改场景文件）。
    ///
    /// 把它挂到第一关场景的任意常驻物体上（推荐 Main Camera），
    /// 或运行 Editor 菜单「Tools/第一关/搭建正交斜俯视场景」自动挂上并保存。
    /// 进入 Play 后 Awake 里会把现有场景整理成下面的结构：
    ///
    ///   Main Camera                         ← VillaSceneCamera + FixedAspectLetterbox
    ///   ├─ 场景平面_别墅 (World Space 画布)  ← VillaScenePlane
    ///   │    └─ 别墅 (Image, 1677×938 原始尺寸)
    ///   ├─ 行走地面 (BoxCollider, y=0)      ← VillaGround（投影对齐别墅图地板）
    ///   ├─ Oscar_玩家 (SpriteRenderer+Anim) ← OscarPlayerController（WASD）
    ///   └─ Canvas (HUD：愤怒值/任务栏/倒计时…) ← Screen Space - Camera 模式
    ///
    /// 整理内容：
    ///  1. 原 Canvas 是 Screen Space-Camera 且没指定相机、根缩放为 0（历史遗留问题），
    ///     HUD 会集体不可见 → 改为 Screen Space-Camera 绑定主相机 + 1920×1080
    ///     ScaleWithScreenSize，随 16:9 画面缩放、相对位置不变；
    ///  2. 「别墅」Image 移入新建的世界空间画布，由主相机渲染；
    ///  3. 原始 Oscar（UI Image + SpriteRenderer + Rigidbody2D 等混杂组件，
    ///     且动画剪辑绑定的是 SpriteRenderer，挂 Image 上根本播不出来）
    ///     → 重建为纯 SpriteRenderer 角色，复用其 Animator 控制器；
    ///  4. 旧的测试方块「墙体与地面」（带缺材质的 MeshRenderer）会被停用，
    ///     避免洋红色错误方块穿帮。
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class VillaLevelBootstrap : MonoBehaviour
    {
        private const string VillaName = "别墅";
        private const string OldOscarName = "Oscar";
        private const string PlaneRootName = "场景平面_别墅";
        private const string GroundName = "行走地面";
        private const string PlayerName = "Oscar_玩家";
        private const string LegacyBlockName = "墙体与地面";

        [Header("场景平面（世界尺寸）")]
        [Tooltip("别墅图在游戏世界中的目标宽度（单位）。换任意分辨率的图都会自动适配，场景比例不变")]
        [SerializeField] private float sceneWorldWidth = 16.77f;

        [Header("行走地面（floorRect = 别墅图归一化地板范围：x,y=左下角；w,h=宽高，0~1）")]
        [Tooltip("Play 中选中「行走地面」拖 floorRect 对齐地板后，用菜单 Tools/第一关 把值写回这里")]
        [SerializeField] private Rect groundFloorRect = new Rect(0.08f, 0.10f, 0.84f, 0.80f);

        [Header("玩家 Oscar")]
        [Tooltip("行走速度（世界单位/秒），地面宽约 14 单位，4 约 3.5 秒横穿")]
        [SerializeField, Min(0.1f)] private float playerMoveSpeed = 4f;

        [Tooltip("角色体型缩放，0.15 ≈ 画面高度 10%")]
        [SerializeField, Min(0.01f)] private float playerScale = 0.15f;

        [Tooltip("出生点（别墅图片像素坐标，图片左下角为原点）。2160×1208 图下的默认值 ≈ 图中部偏下")]
        [SerializeField] private Vector2 playerSpawnPixel = new Vector2(1080f, 386f);

        [Tooltip("行走动画播放速度（1=原速）")]
        [SerializeField, Min(0.05f)] private float playerAnimationSpeed = 1f;

        private void Awake()
        {
            Camera cam = EnsureCamera();
            Image villa = FindVillaImage();
            if (villa == null)
            {
                Debug.LogError("[VillaLevelBootstrap] 场景里找不到名为“别墅”的 Image，搭建中止。");
                return;
            }

            // ---------- 0) 各对象优先复用场景里“已固化”的（编辑器菜单搭好的），
            //               没有才动态创建（兼容未固化的旧用法） ----------
            var planeRoot = GameObject.Find(PlaneRootName);

            // ---------- 1) 正交斜俯视相机先就位 ----------
            // 必须先于场景平面对齐：平面要按“相机的新姿态”对齐
            var sceneCamera = cam.GetComponent<VillaSceneCamera>();
            if (sceneCamera == null)
            {
                sceneCamera = cam.gameObject.AddComponent<VillaSceneCamera>();
            }

            // ---------- 2) 世界空间画布承载别墅图 ----------
            if (planeRoot == null)
            {
                planeRoot = new GameObject(PlaneRootName, typeof(RectTransform), typeof(Canvas));

                // 先把别墅图挂为子节点，再添加 VillaScenePlane：
                // 这样它的 Awake 能立刻找到 Image 并完成“铺满”布局
                var villaRt = villa.rectTransform;
                villaRt.SetParent(planeRoot.transform, false);
                villaRt.localScale = Vector3.one;
            }

            var plane = planeRoot.GetComponent<VillaScenePlane>();
            if (plane == null)
            {
                plane = planeRoot.AddComponent<VillaScenePlane>();
            }

            plane.RenderCamera = cam;
            plane.InitializeForBootstrap(villa);

            // 新图可能是任意分辨率：按「目标世界宽度 ÷ 新图像素宽」重算像素→世界比例，
            // 保证游戏世界尺寸与换图前一致，相机/地面/角色比例全部自动适配
            if (plane.ImagePixelSize.x > 0f)
            {
                plane.SetUnitsPerPixel(sceneWorldWidth / plane.ImagePixelSize.x);
            }

            sceneCamera.BindPlane(plane);        // 相机按别墅图实际尺寸计算正交视域
            sceneCamera.ConfigureNow();

            planeRoot.transform.SetSiblingIndex(0); // 场景根下排前面，仅为了层级整洁

            // ---------- 3) 行走区域：优先用固化好的多区域（多楼层），
            //               没有则退回旧的单块「行走地面」 ----------
            VillaWalkAreas walkAreas = FindObjectOfType<VillaWalkAreas>();
            VillaGround ground = null;
            if (walkAreas == null)
            {
                ground = FindObjectOfType<VillaGround>();
                if (ground == null)
                {
                    var groundGo = new GameObject(GroundName);
                    ground = groundGo.AddComponent<VillaGround>();
                }

                ground.Wire(plane, sceneCamera);
                ground.ApplyFloorRect(groundFloorRect);
            }

            // ---------- 4) 玩家 Oscar：优先复用固化的 ----------
            OscarPlayerController player;
            var playerGo = GameObject.Find(PlayerName);
            if (playerGo == null)
            {
                RuntimeAnimatorController oscarController = ExtractOldOscarAssets(out Sprite oscarSprite);
                playerGo = new GameObject(PlayerName);
                var sprite = playerGo.AddComponent<SpriteRenderer>();
                sprite.sprite = oscarSprite;
                sprite.sortingOrder = 5; // 别墅图=0，角色=5，HUD=10

                var animator = playerGo.AddComponent<Animator>();
                animator.runtimeAnimatorController = oscarController;

                player = playerGo.AddComponent<OscarPlayerController>();
            }
            else
            {
                player = playerGo.GetComponent<OscarPlayerController>();
                if (player == null)
                {
                    player = playerGo.AddComponent<OscarPlayerController>();
                }

                // 固化模式下玩家已持久存在，只清理旧版 Oscar 物体（不再取资源）
                DestroyLegacyOscar();
            }

            player.Wire(sceneCamera, plane, ground);
            player.SetWalkAreas(walkAreas);
            player.ConfigureForBootstrap(playerMoveSpeed, playerScale, playerSpawnPixel, playerAnimationSpeed);

            // ---------- 5) HUD 画布：Overlay 模式 + 1920×1080 等比缩放 ----------
            // 注意：别墅图可能已挂进世界画布，不能再用它的父画布当 HUD
            ConfigureHudCanvas(FindHudCanvas(planeRoot.transform));

            // ---------- 6) 清理历史遗留 ----------
            DisableLegacyObjects();

            Debug.Log("[VillaLevelBootstrap] 第一关斜俯视场景就绪：WASD 移动 Oscar。" +
                      (walkAreas != null
                          ? "行走范围=行走区域（多楼层），选中它可在 Scene 视图拖拽编辑。"
                          : "行走范围=单块行走地面，改 floorRect 对齐地板。"));
        }

        private void Start()
        {
            // 搭建完成一帧后打印诊断快照（此时所有组件的 Start/LateUpdate 已跑过，
            // 反映的是真实运行状态）。若画面异常，把这段日志完整发回即可定位。
            LogDiagnostics("Play 启动快照");

            // 1 秒后再打一次，捕捉延迟生效的变化（例如别的脚本改了相机/画布）
            Invoke(nameof(LogDelayedDiagnostics), 1f);
        }

        private void LogDelayedDiagnostics()
        {
            LogDiagnostics("1 秒后复检");
        }

        /// <summary>
        /// 打印完整运行时状态：相机、所有相机列表、场景平面、别墅 Image、
        /// HUD 画布、地面、玩家、EventSystem。任何一项异常都能从数值上直接看出来。
        /// </summary>
        private void LogDiagnostics(string title)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Villa诊断] ===== {title} =====");

            // ---- 主相机 ----
            var cam = Camera.main;
            if (cam == null)
            {
                sb.AppendLine("[Villa诊断] Camera.main 为空！");
            }
            else
            {
                sb.AppendLine($"主相机: enabled={cam.enabled} ortho={cam.orthographic} size={cam.orthographicSize:F2} " +
                               $"rect={cam.rect} pixel={cam.pixelWidth}x{cam.pixelHeight} aspect={cam.aspect:F3} " +
                               $"depth={cam.depth} cullingMask={cam.cullingMask} clear={cam.clearFlags}");
                sb.AppendLine($"  位置={cam.transform.position} 旋转={cam.transform.eulerAngles} " +
                               $"activeInHierarchy={cam.gameObject.activeInHierarchy} layer={cam.gameObject.layer}");
            }

            // ---- 场景中所有相机（排查黑边相机/多余相机抢占画面） ----
            foreach (var c in FindObjectsOfType<Camera>())
            {
                sb.AppendLine($"相机[{c.name}]: depth={c.depth} rect={c.rect} enabled={c.enabled} " +
                              $"ortho={c.orthographic} targetTexture={(c.targetTexture != null ? "有" : "无")}");
            }

            // ---- 场景平面（别墅图） ----
            var plane = FindObjectOfType<VillaScenePlane>();
            if (plane == null)
            {
                sb.AppendLine("[Villa诊断] 找不到 VillaScenePlane！");
            }
            else
            {
                var prt = plane.transform as RectTransform;
                var img = plane.SceneImage;
                sb.AppendLine($"场景平面: mode={plane.GetComponent<Canvas>().renderMode} " +
                               $"pos={prt.position} euler={prt.eulerAngles} scale={prt.localScale} " +
                               $"sizeDelta={prt.sizeDelta} 世界尺寸={plane.WorldSize} " +
                               $"active={prt.gameObject.activeInHierarchy} layer={prt.gameObject.layer} " +
                               $"sortingOrder={plane.GetComponent<Canvas>().sortingOrder}");
                if (img != null)
                {
                    sb.AppendLine($"别墅Image: enabled={img.enabled} sprite={(img.sprite != null ? img.sprite.name : "无")} " +
                                   $"sizeDelta={img.rectTransform.sizeDelta} active={img.gameObject.activeInHierarchy}");
                }
                else
                {
                    sb.AppendLine("[Villa诊断] 场景平面上没有别墅 Image！");
                }

                // 关键校验：画布四个角换算到相机视口坐标，正常应全部落在 [0,1] 区间
                if (cam != null)
                {
                    var corners = new Vector3[4];
                    prt.GetWorldCorners(corners);
                    bool allInside = true;
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 vp = cam.WorldToViewportPoint(corners[i]);
                        if (vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f || vp.z <= 0f)
                        {
                            allInside = false;
                        }

                        sb.AppendLine($"  角点{i}: world={corners[i]} viewport=({vp.x:F3}, {vp.y:F3}, z={vp.z:F2})");
                    }

                    sb.AppendLine(allInside
                        ? "  => 四角全部在相机视口内：别墅图应当铺满画面（若仍黑屏，问题在渲染而非位置）"
                        : "  => 有角点在视口外：别墅图不在相机视野里，这就是黑屏原因！");
                }
            }

            // ---- HUD 画布 ----
            var es = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            sb.AppendLine($"EventSystem: {(es != null ? es.gameObject.name : "无")} " +
                          $"Canvas总数={FindObjectsOfType<Canvas>().Length}");

            // ---- 地面与玩家 ----
            var ground = FindObjectOfType<VillaGround>();
            if (ground != null && ground.Box != null)
            {
                sb.AppendLine($"行走地面: center={ground.Box.bounds.center} size={ground.Box.bounds.size}");
            }

            var player = FindObjectOfType<OscarPlayerController>();
            if (player != null)
            {
                sb.AppendLine($"玩家: pos={player.transform.position} scale={player.transform.localScale} " +
                              $"groundPos={player.GroundPosition}");
            }

            Debug.Log(sb.ToString());
        }

        /// <summary>找到主相机；没有就创建一个（含 AudioListener）</summary>
        private Camera EnsureCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            return cam;
        }

        private Image FindVillaImage()
        {
            var go = GameObject.Find(VillaName);
            return go != null ? go.GetComponent<Image>() : null;
        }

        /// <summary>
        /// 从原始 Oscar 上取走需要复用的资源：动画控制器 + 一张初始立绘。
        /// 注意：控制器必须取——动画剪辑绑定的是 SpriteRenderer.m_Sprite，
        /// 挂在 Image 上无法播放，这也是必须重建角色的原因。
        /// </summary>
        private RuntimeAnimatorController ExtractOldOscarAssets(out Sprite sprite)
        {
            sprite = null;
            RuntimeAnimatorController controller = null;

            var old = GameObject.Find(OldOscarName);
            if (old == null)
            {
                return null;
            }

            var animator = old.GetComponent<Animator>();
            if (animator != null)
            {
                controller = animator.runtimeAnimatorController;
            }

            var sr = old.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                sprite = sr.sprite;
            }
            else
            {
                var img = old.GetComponent<Image>();
                if (img != null)
                {
                    sprite = img.sprite;
                }
            }

            // 原始 Oscar 的组件组合已废弃（Image+Rigidbody2D+CapsuleCollider2D 混挂），
            // 运行时销毁，场景文件不受影响
            Destroy(old);
            return controller;
        }

        /// <summary>
        /// 查找 HUD 画布：排除世界空间的场景平面（别墅图所在画布）。
        /// 固化搭建后别墅 Image 挂在世界画布下，不能再用 GetComponentInParent 找 HUD。
        /// </summary>
        private Canvas FindHudCanvas(Transform worldPlaneRoot)
        {
            foreach (Canvas c in FindObjectsOfType<Canvas>())
            {
                if (c.transform == worldPlaneRoot || c.transform.IsChildOf(worldPlaneRoot))
                {
                    continue;
                }

                if (c.GetComponent<VillaScenePlane>() != null)
                {
                    continue;
                }

                return c;
            }

            return null;
        }

        /// <summary>销毁旧版 Oscar 物体（玩家已固化时不再需要取它的资源，只做清理）</summary>
        private void DestroyLegacyOscar()
        {
            var old = GameObject.Find(OldOscarName);
            if (old != null)
            {
                Destroy(old);
            }
        }

        /// <summary>
        /// HUD 画布改为 Screen Space - Overlay：
        ///  - Overlay 模式不依赖任何事件相机，从根源上消除
        ///    “Screen position out of view frustum” 刷屏报错
        ///    （该报错的经典成因就是 Camera 模式画布与事件相机不匹配）；
        ///  - CanvasScaler 用 1920×1080 等比缩放，任意分辨率下 UI 随屏幕缩放、
        ///    相对位置不变。16:9 屏幕上 Overlay 区域与相机 16:9 视域完全重合。
        /// </summary>
        private void ConfigureHudCanvas(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;     // 盖在别墅图(0)与角色(5)之上

            // 修复历史遗留：根节点 localScale 曾被改成 0
            // （Overlay 模式下根 RectTransform 由 Canvas 驱动，这里重置一次以防万一）
            var rt = canvas.transform as RectTransform;
            if (rt != null)
            {
                rt.localScale = Vector3.one;
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        /// <summary>停用早期试验遗留的 3D 方块（缺材质会显示成洋红色错误块）</summary>
        private void DisableLegacyObjects()
        {
            var legacy = GameObject.Find(LegacyBlockName);
            if (legacy != null)
            {
                legacy.SetActive(false);
            }
        }
    }
}
