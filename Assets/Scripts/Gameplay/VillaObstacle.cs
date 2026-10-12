using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 室内障碍物（柜子、床、桌子等）：角色不能穿过。
    ///
    /// ============================ 原理 ============================
    /// 别墅是 2D 整图，柜子/床/桌子画在图里。本组件把“物体投影到地面的范围”
    /// 画成一个四边形（图坐标，可直接拖拽 4 个顶点）：
    ///   1. 运行时 VillaRoomBuilder 会按这个四边形生成一根“柱体” MeshCollider
    ///      （从地面顶到 height 高，物理角色也撞不进去）；
    ///   2. VillaCharacterWalker 行走时每步都做“投影位置几何判定”，
    ///      落在障碍物四边形里（或贴到角色半径以内）的步子直接取消。
    /// 两种方案同时生效：基于物体投影位置生成碰撞体 + 行走脚本的层级式阻挡判定。
    /// </summary>
    [DisallowMultipleComponent]
    public class VillaObstacle : VillaZoneBase
    {
        [Tooltip("障碍物碰撞体的高度（世界单位）。角色撞到侧面就被挡住。")]
        public float height = 1.2f;

        private void Reset()
        {
            // 障碍物：红系。实际线框=深红，投影线框=亮红，一眼认出“不能走”
            actualWireColor = new Color(1f, 0.35f, 0.15f, 1f);
            projectedWireColor = new Color(1f, 0.3f, 0.3f, 1f);
        }
    }
}
