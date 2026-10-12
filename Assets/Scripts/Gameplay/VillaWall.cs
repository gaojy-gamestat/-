using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 墙壁：角色不能穿墙。
    ///
    /// ============================ 用法 ============================
    /// 沿着别墅图里墙壁的走向拉一条细长的四边形（Scene 视图拖 4 个顶点），
    /// Game 视图里对照“投影位置线框”（蓝/青色）让它贴在图里墙的投影上。
    ///
    /// ============================ 与门的关系 ============================
    /// 墙上开门的位置<b>不要</b>用墙盖住，在那里放一个 <see cref="VillaDoor"/>
    /// （门不阻挡、只触发传送），角色走到门区域就会被传送到目标房间。
    ///
    /// ============================ 运行时 ============================
    /// 与障碍物一样：生成柱体 MeshCollider + 行走几何判定，双重阻挡。
    /// </summary>
    [DisallowMultipleComponent]
    public class VillaWall : VillaZoneBase
    {
        [Tooltip("墙体碰撞体的高度（世界单位）。")]
        public float height = 2f;

        private void Reset()
        {
            // 墙壁：蓝/青系
            actualWireColor = new Color(0.25f, 0.65f, 1f, 1f);
            projectedWireColor = new Color(0.3f, 0.9f, 1f, 1f);
        }
    }
}
