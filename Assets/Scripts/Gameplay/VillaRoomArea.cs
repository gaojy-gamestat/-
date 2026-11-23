using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 一个“房间区域”：逻辑意义上的一个房间，由人工在编辑器里拖出来。
    ///
    /// ============================ 为什么需要它 ============================
    /// 地板可能被拆成好几块拼起来（例如「客厅地板左」+「客厅地板右」），
    /// 只按“同一块地板”判定的话，两人都在客厅但各站一块拼接地板时不算同房间。
    /// 本区域只需要罩住整个房间即可——判定“玩家与房主是否同处一室”以房间区域为准。
    ///
    /// ============================ 用法 ============================
    /// 在「别墅」图物体下创建子物体并挂本组件，然后在 Scene 视图里拖拽 4 个顶点，
    /// 让橙色线框盖住整个房间（可以盖住多块拼接地板）。
    /// 一个房间一个 VillaRoomArea；不摆放任何房间区域时，同房间判定退化为“同一块地板”。
    /// </summary>
    [DisallowMultipleComponent]
    public class VillaRoomArea : VillaZoneBase
    {
        [Tooltip("房间名（仅用于日志显示），例如：客厅 / 主卧 / 厨房 / 走廊。")]
        public string roomName = "未命名房间";

        private void Reset()
        {
            // 新加组件时给房间区域一套橙系默认色，和地板/障碍物的线框区分开
            actualWireColor = new Color(1f, 0.55f, 0.15f, 1f);
            projectedWireColor = new Color(1f, 0.4f, 0.1f, 1f);
        }
    }
}
