using UnityEngine;

public class PopupManager : MonoBehaviour
{
    [Header("Popup Objects")]
    public GameObject settingsPanel;
    public GameObject creditPanel;

    public void OpenSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
        }
    }

    public void CloseSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
    }

    public void OpenCredit()
    {
        if (creditPanel != null)
        {
            creditPanel.SetActive(true);
        }
    }

    public void CloseCredit()
    {
        if (creditPanel != null)
        {
            creditPanel.SetActive(false);
        }
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
