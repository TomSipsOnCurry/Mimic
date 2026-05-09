using UnityEngine;
using TMPro;

public class VolumeController : MonoBehaviour
{
    public TMP_InputField volumeInput;

    public void ApplyVolume()
    {
        if (float.TryParse(volumeInput.text, out float v))
        {
            v = Mathf.Clamp(v, 0f, 100f); // user range 0–100
            AudioListener.volume = v / 100f; // convert to 0–1
            Debug.Log("Volume set to: " + AudioListener.volume);
        }
        else
        {
            Debug.Log("Invalid volume input");
        }
    }

}
