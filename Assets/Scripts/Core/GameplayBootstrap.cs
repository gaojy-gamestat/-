using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// GamePlay 场景引导：双方进入 GamePlay 后输出验证日志（场景名、玩家数量、各自位置），
/// 供联机链路验证与 E2E 测试取证使用。
/// </summary>
public class GameplayBootstrap : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(0.5f);
        Report();
    }

    private void Report()
    {
        int playerCount = 0;
        string positions = string.Empty;

        foreach (var netObj in FindObjectsOfType<NetworkObject>())
        {
            if (netObj.IsPlayerObject)
            {
                playerCount++;
                positions += $"[Player ClientId={netObj.OwnerClientId} pos={netObj.transform.position} rot={netObj.transform.eulerAngles.y:F1}] ";
            }
        }

        Debug.Log($"[Gameplay] 场景加载完成：{gameObject.scene.name} | 本机 IsHost={NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost} | 玩家 NetworkObject 数量={playerCount} | {positions}");
    }
}
