using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 别墅房间搭建器：统一管理「别墅」图上的所有区域——地板 / 障碍物 / 墙 / 门。
    ///
    /// ============================ 挂在哪里 ============================
    /// 挂在 Canvas 下的「别墅」物体上（就是那张别墅图的 Image）。
    /// 不用手动摆：菜单 Tools/心灵驾到/初始化别墅行走区域 会一键挂好，
    /// 并把旧的 VillaGroundPlane 地板范围转成第一块 VillaFloorArea。
    ///
    /// ============================ 它负责什么 ============================
    /// 1. 坐标换算：图坐标(0~1) ↔ 屏幕像素 ↔ 世界坐标。
    ///    用“相机射线反算”（同原 VillaGroundPlane 的算法），所以相机怎么摆、
    ///    屏幕什么比例，图上的区域换算到世界后都和画面严格对齐；
    /// 2. 运行时为每块地板生成 3D 平面碰撞体（MeshCollider 水平四边形），
    ///    为障碍物/墙生成柱体碰撞体（从地面顶到指定高度的棱柱）；
    /// 3. 给角色脚本提供 API：地板范围夹取、障碍/墙阻挡判定（含沿墙滑动）、门触发；
    /// 4. Game 视图画“投影位置线框”（各区域盖在别墅图上的范围，按类型着色），
    ///    Scene 视图画“实际位置线框”（由各区域的编辑器绘制）。
    /// </summary>
    [ExecuteAlways] // 编辑态也要画 Game 视图的投影线框（OnGUI 在 Game 视图收到事件时调用）
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)] // 比角色脚本先跑
    public class VillaRoomBuilder : MonoBehaviour
    {
        [Header("别墅图")]
        [Tooltip("留空 = 自动取自己身上的 Image（挂在「别墅」上就不用管）。")]
        public Image villaImage;

        [Header("地面高度（世界坐标 y）")]
        [Tooltip("所有地板所在的水平面高度。0 = 世界原点水平面。")]
        public float groundHeight = 0f;

        [Header("运行时生成")]
        [Tooltip("为每块地板生成 3D 平面碰撞体（MeshCollider）。")]
        public bool createFloorColliders = true;
        [Tooltip("为障碍物/墙生成柱体碰撞体（MeshCollider），物理角色也撞不进。")]
        public bool createBlockerColliders = true;

        [Header("调试线框")]
        [Tooltip("Game 视图：画“投影位置线框”——各区域盖在别墅图上的范围（颜色区分类型）。")]
        public bool showProjectedWireframe = true;
        [Tooltip("Scene 视图：画“实际位置线框”（各区域编辑器也会画，这里是所有区域一起画）。")]
        public bool drawGizmos = true;

        // ------------------------------------------------------------------
        // 运行时数据
        // ------------------------------------------------------------------
        private Camera _cam;
        private Canvas _canvas;
        private readonly List<VillaFloorArea> _floors = new List<VillaFloorArea>();
        private readonly List<VillaZoneBase> _blockers = new List<VillaZoneBase>();
        private readonly List<VillaDoor> _doors = new List<VillaDoor>();
        private readonly List<GameObject> _runtimeObjects = new List<GameObject>();

        private int _lastScreenW = -1;
        private int _lastScreenH = -1;
        private bool _ready;

        /// <summary>是否已就绪（找到图和相机，并收集到至少一块地板）。</summary>
        public bool IsReady { get { return _ready; } }

        /// <summary>所有地板（只读）。</summary>
        public IList<VillaFloorArea> Floors { get { return _floors; } }

        /// <summary>所有门（只读）。</summary>
        public IList<VillaDoor> Doors { get { return _doors; } }

        /// <summary>参考地板宽度（世界单位）：取第一块地板“近边”的宽度，用来把速度/半径换算成世界量。</summary>
        public float FloorWidth
        {
            get
            {
                for (int i = 0; i < _floors.Count; i++)
                {
                    Vector3[] w = _floors[i].GetWorldCorners();
                    if (w.Length == 4)
                    {
                        float d = Vector3.Distance(w[0], w[3]);
                        if (d > 0.01f)
                        {
                            return d;
                        }
                    }
                }

                return 10f; // 没有地板时的保底值
            }
        }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------
        private void Start()
        {
            if (!Application.isPlaying)
            {
                RefreshZones(); // 编辑态也要让 Zone 列表可用（供换算/线框）
                return;
            }

            Rebuild();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            // 分辨率 / 窗口比例变了就重算，保证世界碰撞体永远和图对齐
            if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
            {
                Rebuild();
            }
        }

        private void OnEnable()
        {
            RefreshZones();
        }

        private void OnDestroy()
        {
            DestroyRuntimeObjects();
        }

        /// <summary>重新收集所有子区域并重建运行时碰撞体（可随时手动调用）。</summary>
        public void Rebuild()
        {
            Canvas.ForceUpdateCanvases();
            RefreshZones();

            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;

            if (Application.isPlaying)
            {
                DestroyRuntimeObjects();
                if (createFloorColliders)
                {
                    for (int i = 0; i < _floors.Count; i++)
                    {
                        CreateFloorCollider(_floors[i], i);
                    }
                }

                if (createBlockerColliders)
                {
                    for (int i = 0; i < _blockers.Count; i++)
                    {
                        float h = GetBlockerHeight(_blockers[i]);
                        CreateBlockerCollider(_blockers[i], h, i);
                    }
                }
            }

            _ready = _floors.Count > 0 && ResolveRefs();
        }

        /// <summary>重新收集子物体上的地板/障碍物/墙/门。增删区域后自动生效（OnEnable + 每次重建时调用）。</summary>
        public void RefreshZones()
        {
            _floors.Clear();
            _blockers.Clear();
            _doors.Clear();
            GetComponentsInChildren(true, _floors);
            GetComponentsInChildren(true, _blockers);
            GetComponentsInChildren(true, _doors);

            // 墙和障碍物都算阻挡；门不阻挡（只触发传送）
            for (int i = _blockers.Count - 1; i >= 0; i--)
            {
                if (_blockers[i] is VillaDoor || _blockers[i] is VillaFloorArea)
                {
                    _blockers.RemoveAt(i);
                }
            }

            if (!Application.isPlaying)
            {
                _ready = _floors.Count > 0 && ResolveRefs();
            }
        }

        private float GetBlockerHeight(VillaZoneBase z)
        {
            var o = z as VillaObstacle;
            if (o != null)
            {
                return Mathf.Max(0.1f, o.height);
            }

            var w = z as VillaWall;
            if (w != null)
            {
                return Mathf.Max(0.1f, w.height);
            }

            return 1f;
        }

        // ------------------------------------------------------------------
        // 坐标换算（图 ↔ 屏幕 ↔ 世界）
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
            Vector3 world = Vector3.Lerp(bottom, top, 1f - norm.y); // norm.y 从上往下量

            // Overlay 画布传 null；Screen Space - Camera 画布传它的相机
            return RectTransformUtility.WorldToScreenPoint(_canvas.worldCamera, world);
        }

        /// <summary>屏幕像素 → 地面世界点（相机射线与 y = groundHeight 水平面求交）。</summary>
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
                return new Vector2(0.5f, 0.5f);
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

        private bool ResolveRefs()
        {
            if (villaImage == null)
            {
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
                    _canvas = villaImage.canvas;
                }
            }

            if (_cam == null)
            {
                _cam = Camera.main;
            }

            return _canvas != null && _cam != null;
        }

        // ------------------------------------------------------------------
        // 角色行走 API（VillaCharacterWalker 调用）
        // ------------------------------------------------------------------

        /// <summary>地面高度（所有地板都在同一水平面上）。</summary>
        public float SampleGroundHeight(Vector3 position)
        {
            return groundHeight;
        }

        /// <summary>
        /// 把世界点夹进“可行走范围”：在任何一块地板内 → 原样返回；
        /// 都不在 → 拉到最近一块地板的最近边上点。
        /// </summary>
        public Vector3 ClampToWorld(Vector3 world)
        {
            Vector2 n = GroundToImageNorm(world);
            Vector2 bestP = n;
            float bestSq = float.MaxValue;

            for (int i = 0; i < _floors.Count; i++)
            {
                Vector2 p = _floors[i].ClosestImageNorm(n);
                float d = (p - n).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    bestP = p;
                }
            }

            return ImageNormToGround(bestP);
        }

        /// <summary>
        /// 世界点是否被障碍物/墙挡住（按“物体投影位置”做几何判定）。
        /// radiusNorm = 角色半径（图坐标），贴墙时也进不去。
        /// </summary>
        public bool IsBlocked(Vector3 world, float radiusNorm)
        {
            if (_blockers.Count == 0)
            {
                return false;
            }

            Vector2 n = GroundToImageNorm(world);
            for (int i = 0; i < _blockers.Count; i++)
            {
                VillaZoneBase z = _blockers[i];
                if (z == null || !z.enabled || !z.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (z.ContainsImageNorm(n) || z.EdgeDistanceImageNorm(n) < radiusNorm)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 试着从 from 走到 to（世界坐标）：
        /// 1. 整步被挡 → 尝试只走左右分量（沿墙滑动）→ 再尝试只走前后分量；
        /// 2. 都不行 → 原地不动；
        /// 3. 能走 → 夹进可行走范围（地板并集）返回。
        /// 注意：这里只做“阻挡与范围”，不改变角色脚本算出来的方向和速度。
        /// </summary>
        public Vector3 ResolveMove(Vector3 from, Vector3 to, float radiusNorm)
        {
            if (!IsBlocked(to, radiusNorm))
            {
                return ClampToWorld(to);
            }

            // 整步被挡：先试“只走左右”（沿墙滑）
            Vector3 xOnly = new Vector3(to.x, groundHeight, from.z);
            if (!IsBlocked(xOnly, radiusNorm))
            {
                return ClampToWorld(xOnly);
            }

            // 再试“只走前后”
            Vector3 zOnly = new Vector3(from.x, groundHeight, to.z);
            if (!IsBlocked(zOnly, radiusNorm))
            {
                return ClampToWorld(zOnly);
            }

            return from;
        }

        /// <summary>角色所在的门（不在任何门区域返回 null）。</summary>
        public VillaDoor FindDoorAt(Vector3 world)
        {
            Vector2 n = GroundToImageNorm(world);
            for (int i = 0; i < _doors.Count; i++)
            {
                VillaDoor d = _doors[i];
                if (d != null && d.enabled && d.gameObject.activeInHierarchy && d.ContainsImageNorm(n))
                {
                    return d;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------
        // 运行时碰撞体生成（不写进场景文件，退出播放自动销毁）
        // ------------------------------------------------------------------
        private void CreateFloorCollider(VillaFloorArea floor, int index)
        {
            Vector3[] c = floor.GetWorldCorners();
            if (c.Length != 4)
            {
                return;
            }

            var go = new GameObject("地板碰撞体_" + floor.name);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(null);

            Vector3 center = (c[0] + c[1] + c[2] + c[3]) * 0.25f;
            go.transform.position = center;

            Mesh mesh = BuildQuadMesh(c, center, "地板Mesh_" + index);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = false; // 静态水平面，非凸

            _runtimeObjects.Add(go);
        }

        private void CreateBlockerCollider(VillaZoneBase zone, float height, int index)
        {
            Vector3[] c = zone.GetWorldCorners();
            if (c.Length != 4)
            {
                return;
            }

            var go = new GameObject("阻挡碰撞体_" + zone.name);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(null);

            Vector3 center = (c[0] + c[1] + c[2] + c[3]) * 0.25f;
            center.y = groundHeight + height * 0.5f;
            go.transform.position = center;

            Mesh mesh = BuildPrismMesh(c, height, center, "阻挡Mesh_" + index);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = false; // 凹形柱体（斜墙/斜柜也能精确挡）

            _runtimeObjects.Add(go);
        }

        /// <summary>水平四边形网格（地板碰撞体用）。</summary>
        private static Mesh BuildQuadMesh(Vector3[] c, Vector3 center, string name)
        {
            var mesh = new Mesh();
            mesh.name = name;
            mesh.hideFlags = HideFlags.DontSave;
            mesh.SetVertices(new List<Vector3>
            {
                c[0] - center, c[1] - center, c[2] - center, c[3] - center
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>柱体网格：底面四边形（4 顶点）向上拉伸 height（障碍物/墙碰撞体用）。</summary>
        private static Mesh BuildPrismMesh(Vector3[] c, float height, Vector3 center, string name)
        {
            var verts = new List<Vector3>(8);
            for (int i = 0; i < 4; i++)
            {
                Vector3 b = c[i];
                b.y = 0f;
                verts.Add(b);            // 0..3 底面
            }

            for (int i = 0; i < 4; i++)
            {
                Vector3 t = c[i];
                t.y = height;
                verts.Add(t);            // 4..7 顶面
            }

            // 顶点相对柱体中心
            for (int i = 0; i < verts.Count; i++)
            {
                verts[i] -= new Vector3(0f, height * 0.5f, 0f);
            }

            var tris = new List<int>();
            // 4 个侧面（每面 2 三角形）。MeshCollider 不要求朝向，只要求形状正确
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                tris.Add(i); tris.Add(j); tris.Add(4 + j);
                tris.Add(i); tris.Add(4 + j); tris.Add(4 + i);
            }

            // 顶面 + 底面（底面给挡下坠的角色用）
            tris.Add(4); tris.Add(5); tris.Add(6);
            tris.Add(4); tris.Add(6); tris.Add(7);
            tris.Add(0); tris.Add(2); tris.Add(1);
            tris.Add(0); tris.Add(3); tris.Add(2);

            var mesh = new Mesh();
            mesh.name = name;
            mesh.hideFlags = HideFlags.DontSave;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void DestroyRuntimeObjects()
        {
            for (int i = 0; i < _runtimeObjects.Count; i++)
            {
                if (_runtimeObjects[i] != null)
                {
                    Destroy(_runtimeObjects[i]);
                }
            }

            _runtimeObjects.Clear();
        }

        // ------------------------------------------------------------------
        // 调试图形
        // ------------------------------------------------------------------

#if UNITY_EDITOR
        /// <summary>
        /// Game 视图：画“投影位置线框”。OnGUI 画在最上层（盖在别墅图之上），
        /// 编辑态（Game 视图收到鼠标事件时）和运行态都会刷新。
        /// 这套线框 = 各区域换算成世界后再投影回屏幕的范围，即“盖在图上的范围”。
        /// </summary>
        private void OnGUI()
        {
            if (!showProjectedWireframe || Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (!ResolveRefs() || _cam == null)
            {
                return;
            }

            RefreshZones();
            DrawProjectedWire(_floors);
            DrawProjectedWire(_blockers);
            DrawProjectedWire(_doors);
        }

        private static Material _glMat;

        /// <summary>把一组区域的“投影位置线框”画到 Game 视图（GL 立即模式直线）。</summary>
        private void DrawProjectedWire<T>(List<T> zones) where T : VillaZoneBase
        {
            for (int i = 0; i < zones.Count; i++)
            {
                VillaZoneBase z = zones[i];
                if (z == null || !z.enabled || !z.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3[] w = z.GetWorldCorners();
                if (w.Length != 4)
                {
                    continue;
                }

                // 世界 → 屏幕像素（左下原点，和 GL.LoadPixelMatrix 一致）
                var pts = new Vector2[4];
                for (int k = 0; k < 4; k++)
                {
                    Vector3 s = _cam.WorldToScreenPoint(w[k]);
                    pts[k] = new Vector2(s.x, s.y);
                }

                if (_glMat == null)
                {
                    _glMat = new Material(Shader.Find("Hidden/Internal-Colored"));
                    _glMat.hideFlags = HideFlags.HideAndDontSave;
                }

                GL.PushMatrix();
                GL.LoadPixelMatrix();
                _glMat.SetPass(0);
                GL.Begin(GL.LINES);
                GL.Color(z.projectedWireColor);
                for (int k = 0; k < 4; k++)
                {
                    Vector2 a = pts[k];
                    Vector2 b = pts[(k + 1) % 4];
                    GL.Vertex3(a.x, a.y, 0f);
                    GL.Vertex3(b.x, b.y, 0f);
                }

                GL.End();
                GL.PopMatrix();
            }
        }
#endif

        /// <summary>Scene 视图（Game 视图开 Gizmos 时也会有，但在 UI 图下层）：画“实际位置线框”。</summary>
        private void OnDrawGizmos()
        {
            if (!drawGizmos)
            {
                return;
            }

            RefreshZones();

            DrawZoneGizmo(_floors);
            DrawZoneGizmo(_blockers);
            DrawZoneGizmo(_doors);
        }

        private void DrawZoneGizmo<T>(List<T> zones) where T : VillaZoneBase
        {
            for (int i = 0; i < zones.Count; i++)
            {
                VillaZoneBase z = zones[i];
                if (z == null)
                {
                    continue;
                }

                Vector3[] w = z.GetWorldCorners();
                if (w.Length != 4)
                {
                    continue;
                }

                Gizmos.color = z.actualWireColor;
                for (int k = 0; k < 4; k++)
                {
                    Gizmos.DrawLine(w[k], w[(k + 1) % 4]);
                }

                // 顶点小方块，方便在 Scene 视图认出手柄位置
                Gizmos.color = Color.white;
                for (int k = 0; k < 4; k++)
                {
                    Gizmos.DrawCube(w[k], new Vector3(0.02f, 0.02f, 0.02f) * Mathf.Max(1f, FloorWidth * 0.02f));
                }
            }
        }
    }
}
