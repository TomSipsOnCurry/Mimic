using UnityEngine;
using Photon.Pun;

public class PhotonMultiplayerUI : MonoBehaviour
{
    private PhotonMultiplayer net;
    private string roomCodeInput = "1234";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        // MainMenuMultiplayerLobby owns the multiplayer UI now.
    }

    private void Start()
    {
#if UNITY_2023_1_OR_NEWER
        net = FindAnyObjectByType<PhotonMultiplayer>();
#else
        net = FindObjectOfType<PhotonMultiplayer>();
#endif
    }

    private void Update()
    {
        if (net == null)
        {
#if UNITY_2023_1_OR_NEWER
            net = FindAnyObjectByType<PhotonMultiplayer>();
#else
            net = FindObjectOfType<PhotonMultiplayer>();
#endif
        }
    }

    private void OnGUI()
    {
    }
}
