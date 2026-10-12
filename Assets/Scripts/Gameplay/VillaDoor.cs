using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 门：房间之间的传送点。房间之间<b>不是直接连通行走</b>——每块地板是独立的，
    /// 角色走到门区域时被“传送”到目标门旁边的落点。
    ///
    /// ============================ 用法 ============================
    /// 1. 在墙要开门的位置放一个门区域（四边形拖拽同地板/墙），
    ///    让它的一小段伸进房间地板内部（角色踩得到才算“走到门口”）；
    /// 2. 把 targetDoor 指向另一扇门（另一个房间门口的那扇）——
    ///    <b>两扇门自动成组、互相传送</b>：A 指向 B 的同时，B 会自动指回 A；
    ///    改指向/清空时旧配对也会自动解除（双向同步，不会出现单边残留）；
    /// 3. 调 spawnOffset：传送后落点相对目标门中心的偏移（图坐标），
    ///    默认往图下方偏 0.04（往“镜头方向”进入房间内部），按房间朝向自行调整。
    ///
    /// ============================ 防抖 ============================
    /// 传送后 retriggerCooldown 秒内不会再触发任何门，防止刚落地又被送回去。
    /// </summary>
    [DisallowMultipleComponent]
    public class VillaDoor : VillaZoneBase
    {
        [Tooltip("配对门：拖入另一扇门即自动成组（互相传送）。A 指向 B 的同时，B 自动指回 A；清空 = 解除整组配对。")]
        public VillaDoor targetDoor;

        [Tooltip("（自动维护，别手改）当前配对的那扇门的记录，改 targetDoor 时用来解除旧配对。")]
        [HideInInspector] public VillaDoor pairedDoor;

        [Tooltip("传送后落点 = 目标门中心 + 该偏移（图坐标 0~1）。往房间内部偏一点，避免落地即回传。")]
        public Vector2 spawnOffset = new Vector2(0f, 0.04f);

        [Tooltip("传送冷却（秒）：刚落地不会被任何门立刻送回去。")]
        public float retriggerCooldown = 1f;

        [Tooltip("门的名字（可选），只用来给人看，方便配对。")]
        public string doorLabel;

        /// <summary>是否已两两配对（互指）。</summary>
        public bool IsPaired
        {
            get { return targetDoor != null && targetDoor != this && targetDoor.targetDoor == this; }
        }

        /// <summary>传送落点（图坐标）：目标门中心 + 目标门的 spawnOffset。</summary>
        public Vector2 GetSpawnImageNorm()
        {
            VillaDoor target = targetDoor != null ? targetDoor : this;
            return target.CenterImageNorm + target.spawnOffset;
        }

        private void Reset()
        {
            // 门：品红/粉系——既不蓝（墙）也不红（障碍），一眼认出“传送点”
            actualWireColor = new Color(1f, 0.55f, 1f, 1f);
            projectedWireColor = new Color(1f, 0.75f, 0.95f, 1f);
            // 门默认小一点，比地板好拖
            corners = new Vector2[]
            {
                new Vector2(0.45f, 0.75f), new Vector2(0.55f, 0.75f),
                new Vector2(0.55f, 0.80f), new Vector2(0.45f, 0.80f)
            };
        }

        /// <summary>Inspector 上显示配对信息。</summary>
        public override string ToString()
        {
            if (IsPaired)
            {
                return string.Format("门[{0}] ↔ 门[{1}]（互相传送）", doorLabel, targetDoor.doorLabel);
            }

            return string.Format("门[{0}] → {1}", doorLabel,
                targetDoor != null ? ("门[" + targetDoor.doorLabel + "]") : "(未配对)");
        }
    }
}
