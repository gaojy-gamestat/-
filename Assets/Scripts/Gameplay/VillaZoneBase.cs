using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 别墅图上所有“可拖拽四边形区域”的公共基类。
    /// 地板（VillaFloorArea）、障碍物（VillaObstacle）、墙壁（VillaWall）、门（VillaDoor）都继承它。
    ///
    /// ============================ 坐标约定 ============================
    /// 4 个顶点全部用“别墅图上的归一化坐标”存储（0~1，左上角为原点，
    /// x 从左到右、y 从上到下，和看图软件一致）。这样存的好处：
    ///   * 图片分辨率 / 屏幕宽高比 / Canvas 缩放怎么变，区域都永远“贴在图上”，不会错位；
    ///   * 运行时再通过 <see cref="VillaRoomBuilder"/> 用相机射线反算成世界坐标，生成 3D 碰撞体。
    ///
    /// ============================ 顶点顺序 ============================
    /// corners[0]=左上  [1]=右上  [2]=右下  [3]=左下（顺时针绕一圈）。
    /// 斜俯视视角下地板在图里是梯形，所以允许拖成不规则四边形。
    ///
    /// ============================ 两套线框 ============================
    ///   * 实际位置线框（actualWireColor）：Scene 视图里世界空间的真实四边形——碰撞体真正所在；
    ///   * 投影位置线框（projectedWireColor）：Game 视图里该四边形盖在别墅图上的范围。
    /// 两套线框颜色不同，用来核对“碰撞体”和“图里看到的地板”是否对齐。
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class VillaZoneBase : MonoBehaviour
    {
        [Tooltip("4 个顶点（别墅图归一化坐标 0~1，左上为原点）：0=左上 1=右上 2=右下 3=左下。Scene 视图里可直接拖拽小圆点调整。")]
        public Vector2[] corners = new Vector2[]
        {
            new Vector2(0.30f, 0.70f), new Vector2(0.60f, 0.70f),
            new Vector2(0.60f, 0.95f), new Vector2(0.30f, 0.95f)
        };

        [Header("线框颜色（两套线框用于摆放辅助参考）")]
        [Tooltip("实际位置线框颜色：Scene 视图里世界空间的真实四边形（3D 碰撞体所在处）。")]
        public Color actualWireColor = new Color(1f, 0.8f, 0f, 1f);
        [Tooltip("投影位置线框颜色：Game 视图里该四边形盖在别墅图上的范围。")]
        public Color projectedWireColor = new Color(0f, 1f, 0.4f, 1f);

        /// <summary>所属的房间搭建器（父物体上的 VillaRoomBuilder，放在「别墅」图物体下面）。</summary>
        public VillaRoomBuilder Builder
        {
            get { return GetComponentInParent<VillaRoomBuilder>(); }
        }

        /// <summary>4 个顶点的世界坐标（y = 地面高度；换算失败返回空数组）。</summary>
        public Vector3[] GetWorldCorners()
        {
            VillaRoomBuilder b = Builder;
            if (b == null || corners == null || corners.Length != 4)
            {
                return new Vector3[0];
            }

            var result = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                result[i] = b.ImageNormToGround(SanitizeNorm(corners[i]));
            }

            return result;
        }

        /// <summary>
        /// 顶点保洁：3D 里拖点时鼠标射线可能几乎与地面平行，反算出的图坐标会是天文数字，
        /// 进而生成跑到几百万单位外的碰撞体——在 Scene 视图按 F 聚焦它，相机距离会被拉到几十万，
        /// 整个场景看起来就“空了”。这里把顶点限制在图纸附近的合理范围，并挡掉 NaN/Infinity。
        /// </summary>
        public static Vector2 SanitizeNorm(Vector2 n)
        {
            float x = Mathf.Clamp(n.x, -1f, 2f);
            float y = Mathf.Clamp(n.y, -1f, 2f);
            if (float.IsNaN(x) || float.IsInfinity(x))
            {
                x = 0.5f;
            }

            if (float.IsNaN(y) || float.IsInfinity(y))
            {
                y = 0.5f;
            }

            return new Vector2(x, y);
        }

        /// <summary>四边形中心（图坐标，4 顶点平均值）。</summary>
        public Vector2 CenterImageNorm
        {
            get
            {
                Vector2 c = Vector2.zero;
                for (int i = 0; i < corners.Length; i++)
                {
                    c += corners[i];
                }

                return c / corners.Length;
            }
        }

        /// <summary>点（图坐标）是否在本四边形内。支持拖歪了的凹四边形。</summary>
        public bool ContainsImageNorm(Vector2 p)
        {
            return PolygonContains(p, corners);
        }

        /// <summary>
        /// 把点夹到四边形内（图坐标）：在内部返回原值；在外部返回离它最近的“边上点”。
        /// 角色走出地板范围时用它拉回来。
        /// </summary>
        public Vector2 ClosestImageNorm(Vector2 p)
        {
            if (PolygonContains(p, corners))
            {
                return p;
            }

            Vector2 best = p;
            float bestSq = float.MaxValue;
            int j = corners.Length - 1;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 c = ClosestOnSegment(corners[j], corners[i], p);
                float d = (c - p).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = c;
                }

                j = i;
            }

            return best;
        }

        /// <summary>点到四边形最近边的距离（图坐标）。用于“角色半径”判定：贴着墙也进不去。</summary>
        public float EdgeDistanceImageNorm(Vector2 p)
        {
            if (PolygonContains(p, corners))
            {
                return 0f; // 在内部：直接算“最近边距离”意义不大，返回 0 由调用方按“已在内”处理
            }

            Vector2 c = ClosestImageNorm(p);
            return Vector2.Distance(c, p);
        }

        /// <summary>世界坐标点是否在区域内（内部换算成图坐标再判定）。</summary>
        public bool ContainsWorld(Vector3 world)
        {
            VillaRoomBuilder b = Builder;
            return b != null && ContainsImageNorm(b.GroundToImageNorm(world));
        }

        // ------------------------------------------------------------------
        // 静态几何工具（任意四边形/多边形通用）
        // ------------------------------------------------------------------

        /// <summary>射线法（even-odd）判断点是否在多边形内。</summary>
        public static bool PolygonContains(Vector2 p, Vector2[] poly)
        {
            if (poly == null || poly.Length < 3)
            {
                return false;
            }

            bool inside = false;
            int j = poly.Length - 1;
            for (int i = 0; i < poly.Length; i++)
            {
                bool cross = (poly[i].y > p.y) != (poly[j].y > p.y);
                if (cross)
                {
                    // 求边与水平线交点的 x
                    float x = (poly[j].x - poly[i].x) * (p.y - poly[i].y)
                              / (poly[j].y - poly[i].y) + poly[i].x;
                    if (p.x < x)
                    {
                        inside = !inside;
                    }
                }

                j = i;
            }

            return inside;
        }

        /// <summary>点到线段的最近点。</summary>
        public static Vector2 ClosestOnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-10f)
            {
                return a;
            }

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return a + ab * t;
        }

        /// <summary>给 Inspector 显示：当前区域中心（图坐标）四舍五入到 3 位。</summary>
        public override string ToString()
        {
            return string.Format("{0} @ ({1:F3}, {2:F3})", GetType().Name, CenterImageNorm.x, CenterImageNorm.y);
        }
    }
}
