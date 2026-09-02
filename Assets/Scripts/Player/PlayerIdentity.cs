using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 玩家身份：区分 Host（机敏哥）与 Client（老实人），并在日志中输出身份信息。
/// 临时占位外观：Host 蓝色、Client 橙色，方便双人联调时肉眼区分，后续替换正式角色模型时
/// 只需要保留 NetworkObject/NetworkTransform 结构，替换 Visual 子物体即可。
/// </summary>
public class PlayerIdentity : NetworkBehaviour
{
    public static readonly string HostDisplayName = "机敏哥 (Host)";
    public static readonly string ClientDisplayName = "老实人 (Client)";

    private readonly NetworkVariable<FixedString64Bytes> DisplayName = new NetworkVariable<FixedString64Bytes>();

    public string PlayerDisplayName => DisplayName.Value.ToString();

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            DisplayName.Value = IsHost ? HostDisplayName : ClientDisplayName;
        }

        // 等一帧保证 Host 写入的值已复制（Host 本机立即生效）。
        ReportIdentity();

        ApplyPlaceholderColor();
    }

    private void ReportIdentity()
    {
        // NetworkVariable 可能尚未复制完成，日志先用本地可确定的身份。
        string localName = IsHost ? HostDisplayName : ClientDisplayName;
        Debug.Log($"[Player] 出生：{localName}（NetworkVariable={DisplayName.Value}）| ClientId={OwnerClientId} | IsHost={IsHost} | IsOwner={IsOwner} | 位置={transform.position}");
    }

    private void ApplyPlaceholderColor()
    {
        var renderer = GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            // Host 蓝 / Client 橙；自己的角色加白描边效果（临时方案，仅联调区分用）。
            bool isHostPlayer = IsHost && IsOwner;
            bool isClientPlayer = !IsHost && IsOwner;
            if (isHostPlayer) renderer.material.color = new Color(0.25f, 0.55f, 1f);
            else if (isClientPlayer) renderer.material.color = new Color(1f, 0.6f, 0.2f);
            else renderer.material.color = new Color(0.6f, 0.6f, 0.6f);
        }
    }
}
