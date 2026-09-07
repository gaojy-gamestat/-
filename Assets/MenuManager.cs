using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [Header("制作人员面板")]
    public GameObject creditsPanel;

    public void OnCreateGame()
    {
        Debug.Log("创建游戏");
    }

    public void OnJoinGame()
    {
        Debug.Log("加入游戏");
    }

    public void OnSettings()
    {
        Debug.Log("打开设置");
    }

    public void OnCredits()
    {
        if (creditsPanel != null)
            creditsPanel.SetActive(true);
        else
            Debug.LogWarning("MenuManager：没有拖入制作人员面板！");
    }

    public void CloseCredits()
    {
        if (creditsPanel != null)
            creditsPanel.SetActive(false);
    }
}
