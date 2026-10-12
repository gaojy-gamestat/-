using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 行走地面：把「别墅图里的地板区域」自动换算成世界空间里的 <b>3D 平面碰撞体</b>。
    ///
    /// ============================ 挂在哪里 ============================
    /// 挂在 <b>Canvas 下的「别墅」物体上</b>（就是那张别墅图的 Image）。
    /// 不需要你手动摆任何东西，运行时它自己算出地面位置。
    ///
    /// ============================ 怎么保证「和图片里的地板视觉对齐」 ============================
    /// 1. 先取「别墅」这张 UI 图在屏幕上的真实像素位置（会自动算上 Canvas 的缩放 / 分辨率）；
    /// 2. 把图里“地板区域”的 4 个角（floorLeftTop / floorRightBottom，归一化 0~1）
    ///    换算成屏幕像素，再从相机发射 4 条射线，打到 y = groundHeight 的水平面上；
    /// 3. 这 4 个落点组成的四边形，就是「在相机视角下正好盖住图片地板」的那块地面；
    ///    给它生成四边形 Mesh + MeshCollider = 3D 平面碰撞体。
    ///
    /// 因为是<b>用相机射线反算</b>出来的，所以：
    ///   * 屏幕是 16:9 还是别的比例、窗口怎么缩放，都对得上；
    ///   * 相机怎么摆（俯角、高度、FOV）都对得上；
    ///   * 想核对：Game 视图右上角打开 Gizmos，就能看到黄色地面框，
    ///     它应该正好压在别墅图的地板上（稍微调 floorLeftTop / floorRightBottom 即可微调）。
    ///
    /// ============================ 运行时生成，不动你的场景 ============================
    /// 地面物体只在运行时创建（HideFlags.DontSave），不会写进场景文件；退出播放自动消失。
    /// 想让它变成场景里的常驻物体，见 CreateGroundObject 的注释。
    /// </summary>
    [DefaultExecutionOrder(-100)] // 比角色脚本先跑，保证角色 Start 时地面已就绪
    [DisallowMultipleComponent]
    public class VillaGroundPlane : MonoBehaviour
    {
        [Header("别墅图")]
        [Tooltip("留空 = 自动找场景里叫「别墅」的 Image（建议留空）。")]
        public Image villaImage;

        [Header("地板区域（别墅图上的归一化坐标：0~1，左上角为原点，跟看图软件一致）")]
        [Tooltip("地板区域的左上角。默认值对应 2160×1208 别墅图里一楼客厅~厨房那一层地板。")]
        public Vector2 floorLeftTop = new Vector2(0.245f, 0.655f);
        [Tooltip("地板区域的右下角。")]
        public Vector2 floorRightBottom = new Vector2(0.926f, 0.965f);
        [Tooltip("角色离地板边缘至少留多少（归一化），防止贴边卡住。")]
        public float floorPadding = 0.012f;

        [Header("地面高度（世界坐标 y）")]
        [Tooltip("水平地面的高度。0 = 世界原点所在的水平面。")]
        public float groundHeight = 0f;

        [Header("生成")]
        [Tooltip("生成 MeshCollider（3D 平面碰撞体）。")]
        public bool createCollider = true;
        [Tooltip("调试用：给地面加一块半透明黄色片，方便肉眼确认对齐。正式用请关掉。")]
        public bool showDebugQuad = false;
        public Color debugColor = new Color(1f, 0.85f, 0f, 0.28f);

        [Header("Gizmos")]
        [Tooltip("在 Scene / Game 视图画出地面框（只画，不改任何东西）。")]
        public bool drawGizmo = true;
        public Color gizmoColor = new Color(1f, 0.75f, 0f, 1f);

        // ------------------------------------------------------------------
        // 运行时数据
        // ------------------------------------------------------------------
        private Camera _cam;
        private Canvas _canvas;
        private readonly Vector3[] _corners = new Vector3[4]; // 0=左下 1=左上 2=右上 3=右下（世界坐标）
        private bool _ready;
        private GameObject _groundGo;
        private MeshFilter _meshFilter;
        private MeshCollider _meshCollider;
        private MeshRenderer _debugRenderer;
        private Mesh _mesh;

        private int _lastScreenW = -1;
        private int _lastScreenH = -1;
        private Vector2 _lastCanvasSize;

        /// <summary>地面是否已经算好。</summary>
        public bool IsReady { get { return _ready; } }

        /// <summary>地面四角世界坐标：0=左下(近) 1=左上(远) 2=右上(远) 3=右下(近)。</summary>
        public Vector3[] Corners { get { return _corners; } }

        /// <summary>地面中心（世界坐标）。</summary>
        public Vector3 Center
        {
            get
            {
                return (_corners[0] + _corners[1] + _corners[2] + _corners[3]) * 0.25f;
            }
        }

        /// <summary>地面宽度（世界单位）。角色速度用它换算成“每秒走多少世界单位”。</summary>
        public float FloorWidth
        {
            get { return Vector3.Distance(_corners[0], _corners[3]); }
        }

        /// <summary>运行时生成的行走地面物体（带 MeshCollider）。</summary>
        public GameObject GroundObject { get { return _groundGo; } }

        /// <summary>运行时生成的平面碰撞体。</summary>
        public Collider GroundCollider { get { return _meshCollider; } }

        /// <summary>别墅图在屏幕上的像素矩形（左下为原点）。</summary>
        public Rect ImageScreenRect
        {
            get
            {
                if (!ResolveRefs())
                {
                    return new Rect(0f, 0f, Screen.width, Screen.height);
                }

                Vector2 bl = ImageNormToScreen(new Vector2(0f, 1f)); // 左下（norm.y 从上往下量）
                Vector2 tr = ImageNormToScreen(new Vector2(1f, 0f)); // 右上
                return Rect.MinMaxRect(bl.x, bl.y, tr.x, tr.y);
            }
        }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------
        private void Start()
        {
            Rebuild();
        }

        private void LateUpdate()
        {
            // 分辨率 / 窗口比例 / 画布尺寸变了就重算，保证永远对齐
            if (Screen.width != _lastScreenW || Screen.height != _lastScreenH
                || (_canvas != null && _lastCanvasSize != _canvas.pixelRect.size))
            {
                Rebuild();
            }
        }

        /// <summary>重新计算地面（可随时手动调用）。</summary>
        public void Rebuild()
        {
            if (!ResolveRefs())
            {
                _ready = false;
                return;
            }

            Canvas.ForceUpdateCanvases(); // 确保读到的是最新布局（屏幕刚变化时尤其重要）

            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            _lastCanvasSize = _canvas.pixelRect.size;

            ComputeCorners();
            EnsureGroundObject();
            UpdateGroundObject();
            _ready = true;
        }

        private bool ResolveRefs()
        {
            if (villaImage == null)
            {
                // 自动找：先找这栋楼自己身上的 Image，再退回场景里叫「别墅」的物体
                villaImage = GetComponent<Image>();
                if (villaImage == null)
                {
                    GameObject go = GameObject.Find("别墅");
                    if (go != null)
                    {
                        villaImage = go.GetComponent<Image>();
                    }
                }
            }

            if (villaImage == null)
            {
                return false;
            }

            if (_canvas == null)
            {
                _canvas = villaImage.GetComponentInParent<Canvas>();
                if (_canvas == null)
                {
                    // Overlay 画布也可能取不到（例如图被临时移出画布），退回根 Canvas
                    _canvas = villaImage.canvas;
                }
            }

            if (_cam == null)
            {
                _cam = Camera.main;
            }

            return villaImage != null && _canvas != null && _cam != null;
        }

        // ------------------------------------------------------------------
        // 图片坐标 <-> 屏幕坐标 <-> 地面世界坐标
        // ------------------------------------------------------------------

        /// <summary>别墅图归一化坐标（0~1，左上为原点）→ 屏幕像素（左下为原点）。</summary>
        public Vector2 ImageNormToScreen(Vector2 norm)
        {
            if (!ResolveRefs())
            {
                return new Vector2(Screen.width * norm.x, Screen.height * (1f - norm.y));
            }

            var rt = villaImage.rectTransform;
            var c = new Vector3[4];
            rt.GetWorldCorners(c); // 0=左下 1=左上 2=右上 3=右下

            Vector3 bottom = Vector3.Lerp(c[0], c[3], norm.x);
            Vector3 top = Vector3.Lerp(c[1], c[2], norm.x);
            Vector3 world = Vector3.Lerp(bottom, top, 1f - norm.y); // norm.y 从上往下量，所以这里翻转

            // Overlay 画布传 null；Screen Space - Camera 画布要传它的相机
            return RectTransformUtility.WorldToScreenPoint(_canvas.worldCamera, world);
        }

        /// <summary>屏幕像素 → 地面上的世界点（相机射线与 y = groundHeight 平面求交）。</summary>
        public Vector3 ScreenPointToGround(Vector2 screenPoint)
        {
            if (_cam == null)
            {
                _cam = Camera.main;
            }

            if (_cam == null)
            {
                return new Vector3(0f, groundHeight, 0f);
            }

            Ray ray = _cam.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
            float enter;
            if (plane.Raycast(ray, out enter))
            {
                return ray.GetPoint(enter);
            }

            return new Vector3(0f, groundHeight, 0f);
        }

        /// <summary>别墅图归一化坐标（0~1，左上为原点）→ 地面世界坐标。</summary>
        public Vector3 ImageNormToGround(Vector2 norm)
        {
            return ScreenPointToGround(ImageNormToScreen(norm));
        }

        /// <summary>地面世界坐标 → 别墅图归一化坐标（0~1，左上为原点）。</summary>
        public Vector2 GroundToImageNorm(Vector3 world)
        {
            if (!ResolveRefs())
            {
                return new Vector2(0.5f, 0.5f);
            }

            Vector3 sp = _cam.WorldToScreenPoint(world);
            if (sp.z <= 0f)
            {
                return new Vector2((floorLeftTop.x + floorRightBottom.x) * 0.5f,
                                   (floorLeftTop.y + floorRightBottom.y) * 0.5f);
            }

            Vector2 local;
            bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                villaImage.rectTransform, sp, _canvas.worldCamera, out local);
            if (!ok)
            {
                return new Vector2(0.5f, 0.5f);
            }

            Rect r = villaImage.rectTransform.rect;
            float u = r.width > 0.0001f ? (local.x - r.xMin) / r.width : 0.5f;
            float v = r.height > 0.0001f ? (local.y - r.yMin) / r.height : 0.5f;
            return new Vector2(u, 1f - v); // 转成“左上为原点”
        }

        /// <summary>把一个地面点夹到地板范围内（含 padding）。角色移动时用它。</summary>
        public Vector3 ClampToFloor(Vector3 world)
        {
            Vector2 n = GroundToImageNorm(world);
            n.x = Mathf.Clamp(n.x, floorLeftTop.x + floorPadding, floorRightBottom.x - floorPadding);
            n.y = Mathf.Clamp(n.y, floorLeftTop.y + floorPadding, floorRightBottom.y - floorPadding);

            Vector3 p = ImageNormToGround(n);
            p.y = groundHeight;
            return p;
        }

        // ------------------------------------------------------------------
        // 生成地面
        // ------------------------------------------------------------------
        private void ComputeCorners()
        {
            Vector2 lt = floorLeftTop;
            Vector2 rb = floorRightBottom;

            _corners[0] = ImageNormToGround(new Vector2(lt.x, rb.y)); // 左下（靠观众）
            _corners[1] = ImageNormToGround(new Vector2(lt.x, lt.y)); // 左上（远处）
            _corners[2] = ImageNormToGround(new Vector2(rb.x, lt.y)); // 右上（远处）
            _corners[3] = ImageNormToGround(new Vector2(rb.x, rb.y)); // 右下（靠观众）
        }

        private void EnsureGroundObject()
        {
            if (_groundGo == null)
            {
                // 只在运行时创建，且不写进场景文件
                _groundGo = new GameObject("行走地面(运行时生成)");
                _groundGo.hideFlags = HideFlags.DontSave;
                _groundGo.transform.SetParent(null);          // 放在场景根，避免被 UI 的缩放影响
                _meshFilter = _groundGo.AddComponent<MeshFilter>();
                _debugRenderer = _groundGo.AddComponent<MeshRenderer>();
                _debugRenderer.enabled = false;               // 默认不渲染，只用 Gizmos 看
            }

            if (createCollider && _meshCollider == null)
            {
                _meshCollider = _groundGo.AddComponent<MeshCollider>();
                _meshCollider.convex = false;                 // 静态平面，非凸
            }
        }

        private void UpdateGroundObject()
        {
            if (_groundGo == null)
            {
                return;
            }

            Vector3 center = Center;
            _groundGo.transform.position = center;
            _groundGo.transform.rotation = Quaternion.identity;
            _groundGo.transform.localScale = Vector3.one;

            if (_mesh == null)
            {
                _mesh = new Mesh();
                _mesh.name = "行走地面Mesh";
                _mesh.hideFlags = HideFlags.DontSave;
            }

            // 顶点用相对中心的局部坐标（物体本身就在地面上，Inspector 看起来干净）
            _mesh.Clear();
            _mesh.SetVertices(new Vector3[]
            {
                _corners[0] - center,
                _corners[1] - center,
                _corners[2] - center,
                _corners[3] - center
            });
            _mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            if (_meshFilter != null)
            {
                _meshFilter.sharedMesh = _mesh;
            }

            if (_meshCollider != null)
            {
                _meshCollider.sharedMesh = null;  // 先清掉再赋值，让 Collider 重新烘焙
                _meshCollider.sharedMesh = _mesh;
            }

            if (_debugRenderer != null)
            {
                _debugRenderer.enabled = showDebugQuad;
                if (showDebugQuad)
                {
                    if (_debugRenderer.sharedMaterial == null)
                    {
                        Shader sh = Shader.Find("Sprites/Default");
                        if (sh == null)
                        {
                            sh = Shader.Find("Unlit/Color");
                        }

                        if (sh != null)
                        {
                            var mat = new Material(sh);
                            mat.hideFlags = HideFlags.DontSave;
                            _debugRenderer.sharedMaterial = mat;
                        }
                    }

                    if (_debugRenderer.sharedMaterial != null)
                    {
                        _debugRenderer.sharedMaterial.color = debugColor;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Gizmos：只画不算，用来核对“地面和气别墅图地板是否对齐”
        // ------------------------------------------------------------------
        private void OnDrawGizmos()
        {
            if (!drawGizmo || !ResolveRefs())
            {
                return;
            }

            // 用当前（编辑器里的）布局临时算一遍，不动任何物体
            Vector2 lt = floorLeftTop;
            Vector2 rb = floorRightBottom;

            Vector3 p0 = ImageNormToGround(new Vector2(lt.x, rb.y));
            Vector3 p1 = ImageNormToGround(new Vector2(lt.x, lt.y));
            Vector3 p2 = ImageNormToGround(new Vector2(rb.x, lt.y));
            Vector3 p3 = ImageNormToGround(new Vector2(rb.x, rb.y));

            Gizmos.color = gizmoColor;
            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p0);

            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.12f);
            Gizmos.DrawSphere(Center, Mathf.Max(0.02f, FloorWidth * 0.01f));
        }
    }
}
