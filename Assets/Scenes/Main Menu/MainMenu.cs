using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    public GameObject mainMenu;
    public GameObject settingsMenu;

    public void PlayGame()
    {
        SceneManager.LoadScene("Main"); // or index
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
