using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 一块“可行走地板”：对应别墅图里一个房间（或一段走廊）的地面。
    ///
    /// ============================ 用法 ============================
    /// 在「别墅」图物体下创建子物体并挂本组件（或用 VillaRoomBuilder Inspector 的
    /// 「＋ 添加地板」按钮一键创建），然后在 Scene 视图里拖拽 4 个顶点，
    /// 对着 Game 视图的“投影位置线框”让它盖住图里那个房间的地板（斜俯视下是梯形）。
    ///
    /// ============================ 运行时 ============================
    /// VillaRoomBuilder 会为每块地板生成一个 3D 平面碰撞体（MeshCollider，水平四边形），
    /// 角色只能踩在这些四边形上走；走出范围会被拉回最近的边上点。
    /// 多个房间 = 多块地板；房间之间不连通，靠 VillaDoor 传送。
    /// </summary>
    [DisallowMultipleComponent]
    public class VillaFloorArea : VillaZoneBase
    {
        [Tooltip("角色离这块地板边缘至少保留的距离（图坐标 0~1）。0 = 允许贴边。")]
        [Range(0f, 0.05f)] public float padding = 0.008f;

        [Tooltip("Scene 视图里给地板涂一层半透明颜色，方便区分房间。")]
        public bool drawFilledGizmo = true;

        private void Reset()
        {
            // 新加组件时给地板一套绿系默认色：实际=黄绿、投影=绿
            actualWireColor = new Color(0.6f, 1f, 0.2f, 1f);
            projectedWireColor = new Color(0f, 1f, 0.4f, 1f);
        }
    }
}
