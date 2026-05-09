using UnityEngine;
using Photon.Pun;

public class PhotonMultiplayerUI : MonoBehaviour
{
    private PhotonMultiplayer net;
    private string roomCodeInput = "1234";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
#if UNITY_2023_1_OR_NEWER
        if (FindAnyObjectByType<PhotonMultiplayerUI>() != null)
#else
        if (FindObjectOfType<PhotonMultiplayerUI>() != null)
#endif
        {
            return;
        }

        GameObject go = new GameObject(nameof(PhotonMultiplayerUI));
        DontDestroyOnLoad(go);
        go.AddComponent<PhotonMultiplayerUI>();
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
        if (net == null)
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 100));
            GUILayout.Box("Waiting for PhotonMultiplayer...");
            GUILayout.EndArea();
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 350, 250));
        
        GUILayout.Box("=== Photon Multiplayer ===", GUILayout.ExpandWidth(true), GUILayout.Height(30));
        
        GUILayout.Label("Status: " + net.Status, GUILayout.Height(25));
        
        if (!net.IsConnected)
        {
            GUILayout.Label("Room Code:", GUILayout.Height(20));
            roomCodeInput = GUILayout.TextField(roomCodeInput, 50, GUILayout.Height(35));
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host", GUILayout.Height(40), GUILayout.Width(100)))
            {
                net.StartHost();
                roomCodeInput = net.SessionCode;
            }
            
            if (GUILayout.Button("Join", GUILayout.Height(40), GUILayout.Width(100)))
            {
                net.SetSessionCode(roomCodeInput);
                net.StartClient();
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label("Connected to room!", GUILayout.Height(30));
            if (GUILayout.Button("Disconnect", GUILayout.Height(40)))
            {
                net.StopSession();
            }
        }
        
        GUILayout.EndArea();
    }
}
