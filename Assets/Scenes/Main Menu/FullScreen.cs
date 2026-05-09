using UnityEngine;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    private bool isFullscreen = false;

    public void ToggleFullscreen()
    {
        isFullscreen = !isFullscreen;
        Screen.fullScreen = isFullscreen;
    }
}
