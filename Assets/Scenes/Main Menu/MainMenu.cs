using UnityEngine;

public class MenuManager : MonoBehaviour
{
    public GameObject mainMenu;
    public GameObject settingsMenu;
    public GameObject playMenu;
    private MainMenuMultiplayerLobby multiplayerLobby;

    private void Awake()
    {
        multiplayerLobby = GetComponent<MainMenuMultiplayerLobby>();
        if (multiplayerLobby == null)
        {
            multiplayerLobby = gameObject.AddComponent<MainMenuMultiplayerLobby>();
        }

        multiplayerLobby.Configure(playMenu);
    }

    public void PlayGame()
    {
        if (multiplayerLobby != null)
        {
            multiplayerLobby.Open();
            return;
        }

        playMenu.SetActive(true);
    }

    public void BackGame()
    {
        if (multiplayerLobby != null)
        {
            multiplayerLobby.CloseAndLeave();
            return;
        }

        playMenu.SetActive(false);
    }

    public void QuitGame()
    {
        Application.Quit();
        Debug.Log("Quit Game"); // Only visible in editor
    }

    public void OpenSettings()
    {
        settingsMenu.SetActive(true);
    }

    public void CloseSettings()
    {
        settingsMenu.SetActive(false);
    }
}
