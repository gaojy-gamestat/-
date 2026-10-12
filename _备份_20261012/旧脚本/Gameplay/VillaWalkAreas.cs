using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 一块可行走区域：四个顶点用「别墅图归一化坐标」表示（0~1，左下角为原点）。
    /// 顺序固定为：左下 → 右下 → 右上 → 左上。
    ///
    /// 之所以不是矩形：装修风剖面图里的地板是【带透视的梯形】，
    /// 轴对齐矩形永远贴不上；四角可自由拖成梯形/平行四边形，正好贴住地板。
    /// </summary>
    [Serializable]
    public class WalkArea
    {
        [Tooltip("区域名字（仅编辑器显示用，如：一楼客厅 / 二楼卧室）")]
        public string name = "房间";

        [Tooltip("四个顶点（归一化图像坐标 0~1）。顺序：左下 → 右下 → 右上 → 左上。" +
                 "拖成梯形即可贴合带透视的地板")]
        public Vector2[] corners = DefaultCorners();

        // ---------- 旧版「矩形区域」的数据：首次读取时自动转成四角，之后不再使用 ----------
        [HideInInspector] public Rect rect = new Rect(0.3f, 0.3f, 0.4f, 0.4f);
        [HideInInspector] public bool migrated;

        public static Vector2[] DefaultCorners()
        {
            return new[]
            {
                new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.3f),
                new Vector2(0.7f, 0.7f), new Vector2(0.3f, 0.7f),
            };
        }

        /// <summary>
        /// 保证 corners 有效；老场景里只有 rect 的数据会被转换成矩形四角（只做一次）。
        /// 组件的 Awake/OnValidate 与编辑器都会调用，避免任何情况下出现空顶点。
        /// </summary>
        public void EnsureCorners()
        {
            if (migrated)
            {
                if (corners == null || corners.Length != 4)
                {
                    corners = DefaultCorners();
                }

                return;
            }

            corners = new[]
            {
                new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax),
            };
            migrated = true;
        }
    }

    /// <summary>
    /// 行走区域（取代单块「行走地面」）：支持多块四边形，对应剖面别墅的
    /// 一楼 / 二楼 / 三楼……每层楼板、每个房间各一块（梯形地板也能贴合）。
    ///
    /// 原理与单块地面一致：
    ///  - 区域顶点在别墅图上定义（归一化坐标，与图片分辨率无关）；
    ///  - 运行时把图上的四边形仿射映射到地面 y=0 平面上
    ///    （深度方向除以 sin(俯角)，补偿斜俯视的画面压缩）；
    ///  - 角色移动被限制在【所有区域的并集】内——
    ///    区域之间的空隙自动就是“墙”，不需要单独摆墙体碰撞体。
    ///
    /// 多层楼说明：
    ///  - 角色“站”在哪一层，画面上就投影到图上对应楼层的位置；
    ///  - 楼层之间通行：把楼梯两端的四边形画成重叠/相接即可走过去；
    ///    不重叠就完全隔离。
    ///
    /// 编辑方式：编辑模式下选中「行走区域」物体，Scene 视图会出现一张
    /// 临时预览别墅图（位置与 Play 时一致、不存入场景文件），
    /// 每块区域画两条辅助线：彩色=图上对齐框、黄色=地面实际位置，
    /// 直接拖拽中心/四角手柄即可调整。辅助线只在编辑器显示，不进游戏画面。
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class VillaWalkAreas : MonoBehaviour
    {
        [Header("对齐数据源（运行时自动查找接线，无需手动设置）")]
        [SerializeField] private VillaScenePlane scenePlane;
        [SerializeField] private VillaSceneCamera sceneCamera;

        [Header("行走区域列表（编辑器辅助线拖拽编辑）")]
        [SerializeField] private List<WalkArea> areas = new List<WalkArea>();

        private readonly List<Vector2[]> m_WorldQuads = new List<Vector2[]>(); // 地面 XZ 四边形

        /// <summary>区域列表（编辑器手柄直接改这里）</summary>
        public List<WalkArea> Areas => areas;

        public VillaScenePlane PlaneRef => scenePlane;
        public VillaSceneCamera CameraRef => sceneCamera;

        private void Awake()
        {
            EnsureReferences();
            Recalculate();
        }

        private void Start()
        {
            // 场景平面/相机可能在 Awake 之后才就绪，再校正一次
            EnsureReferences();
            Recalculate();
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

        private void OnValidate()
        {
            EnsureCornersAll();

            // Inspector 里直接改数值时实时生效
            if (Application.isPlaying)
            {
                EnsureReferences();
                Recalculate();
            }
        }

        /// <summary>把老场景里的矩形数据补齐成四角（新场景是无操作）</summary>
        public void EnsureCornersAll()
        {
            for (int i = 0; i < areas.Count; i++)
            {
                if (areas[i] != null)
                {
                    areas[i].EnsureCorners();
                }
            }
        }

        /// <summary>把所有归一化四边形换算成地面 XZ 四边形</summary>
        private void Recalculate()
        {
            m_WorldQuads.Clear();
            if (scenePlane == null || scenePlane.ImagePixelSize.x <= 0f)
            {
                return;
            }

            Vector2 px = scenePlane.ImagePixelSize;
            float s = scenePlane.UnitsPerPixel;
            float sinPitch = sceneCamera != null ? Mathf.Sin(sceneCamera.PitchRadians) : 0.5f;
            if (sinPitch < 0.01f)
            {
                sinPitch = 0.01f;
            }

            for (int i = 0; i < areas.Count; i++)
            {
                WalkArea a = areas[i];
                if (a == null)
                {
                    continue;
                }

                a.EnsureCorners();
                m_WorldQuads.Add(ToGroundQuad(a.corners, px, s, sinPitch));
            }
        }

        /// <summary>
        /// 图上的归一化四边形 → 地面 y=0 平面上的 XZ 四边形。
        /// 与 VillaGround 同一套换算：深度方向除以 sin(俯角) 补偿斜视压缩。
        /// </summary>
        public static Vector2[] ToGroundQuad(Vector2[] corners, Vector2 px, float s, float sinPitch)
        {
            var quad = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                quad[i] = new Vector2(
                    (corners[i].x - 0.5f) * px.x * s,
                    (corners[i].y - 0.5f) * px.y * s / sinPitch);
            }

            return quad;
        }

        /// <summary>地面坐标是否在任意一块行走区域内</summary>
        public bool Contains(Vector3 groundPos)
        {
            var p = new Vector2(groundPos.x, groundPos.z);
            for (int i = 0; i < m_WorldQuads.Count; i++)
            {
                if (PointInPolygon(m_WorldQuads[i], p))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>点在四边形内（射线法，凹四边形也正确）</summary>
        private static bool PointInPolygon(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// 从 from 走到 to 的碰撞处理：
        ///  - 目标点合法 → 直走；
        ///  - 撞“墙” → 沿单轴滑行（贴墙滑动，手感自然）；
        ///  - 两轴都撞 → 原地不动。
        /// 区域列表为空时不限制（避免没配置时角色卡死）。
        /// </summary>
        public Vector3 ClampMove(Vector3 from, Vector3 to)
        {
            if (m_WorldQuads.Count == 0 || Contains(to))
            {
                return to;
            }

            Vector3 slideX = new Vector3(to.x, 0f, from.z);
            if (Contains(slideX))
            {
                return slideX;
            }

            Vector3 slideZ = new Vector3(from.x, 0f, to.z);
            if (Contains(slideZ))
            {
                return slideZ;
            }

            return from;
        }

#if UNITY_EDITOR
        /// <summary>选中时在 Scene 视图画出每块区域的地面四边形（黄色）与图上对齐四边形（各区域不同颜色）</summary>
        private void OnDrawGizmosSelected()
        {
            if (scenePlane == null || sceneCamera == null || scenePlane.ImagePixelSize.x <= 0f)
            {
                return;
            }

            Camera cam = scenePlane.RenderCamera != null ? scenePlane.RenderCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector2 px = scenePlane.ImagePixelSize;
            float s = scenePlane.UnitsPerPixel;
            float sinPitch = Mathf.Max(0.01f, Mathf.Sin(sceneCamera.PitchRadians));
            float distance = sceneCamera.PlaneDistance;

            for (int i = 0; i < areas.Count; i++)
            {
                WalkArea area = areas[i];
                if (area == null)
                {
                    continue;
                }

                area.EnsureCorners();
                Vector2[] ground = ToGroundQuad(area.corners, px, s, sinPitch);

                Gizmos.color = Color.yellow;
                for (int k = 0; k < 4; k++)
                {
                    Gizmos.DrawLine(
                        new Vector3(ground[k].x, 0f, ground[k].y),
                        new Vector3(ground[(k + 1) % 4].x, 0f, ground[(k + 1) % 4].y));
                }

                // 投影到图平面（与画面里地板重合即为对齐）
                Gizmos.color = Color.HSVToRGB((i * 0.37f) % 1f, 0.9f, 1f);
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p0 = VillaScenePlane.ProjectGroundToPlane(
                        cam, distance, new Vector3(ground[k].x, 0f, ground[k].y));
                    Vector3 p1 = VillaScenePlane.ProjectGroundToPlane(
                        cam, distance, new Vector3(ground[(k + 1) % 4].x, 0f, ground[(k + 1) % 4].y));
                    Gizmos.DrawLine(p0, p1);
                }
            }
        }
#endif
    }
}
