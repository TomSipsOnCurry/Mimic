using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;

public class GameManager : MonoBehaviourPunCallbacks
{
    public const int TotalTokens = 7;

    private const string RoomKeyTokens = "tk";
    private const string RoomKeyGameOver = "go"; // 1 = escaped, 2 = dead

    public static GameManager Instance { get; private set; }
    public static event Action<int> OnTokensChanged;

    [Header("End Screen Images")]
    [SerializeField] private Sprite escapedSprite;
    [SerializeField] private Sprite deadSprite;

    private int offlineTokenCount;
    private bool gameEnded;

    // Auto-creates the GameManager before any scene loads — no need to place it in a scene
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("GameManager");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<GameManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadEndSprites();
    }

    private void LoadEndSprites()
    {
        if (escapedSprite == null)
            escapedSprite = LoadSprite("Textures/Escaped");

        if (deadSprite == null)
            deadSprite = LoadSprite("Textures/Dead");
    }

    private static Sprite LoadSprite(string resourcePath)
    {
        var sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite != null) return sprite;

        var tex = Resources.Load<Texture2D>(resourcePath);
        if (tex != null)
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        Debug.LogWarning($"GameManager: Could not load sprite at Resources/{resourcePath}");
        return null;
    }

    public static int GetGlobalTokenCount()
    {
        if (PhotonNetwork.InRoom &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKeyTokens, out object val))
        {
            return (int)val;
        }

        return Instance != null ? Instance.offlineTokenCount : 0;
    }

    // Called by CollectibleItem on MasterClient (or locally when offline)
    public void AddToken()
    {
        if (gameEnded) return;
        Debug.Log($"GameManager.AddToken — offline count={offlineTokenCount + 1}, inRoom={PhotonNetwork.InRoom}");

        if (PhotonNetwork.InRoom)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            int next = GetGlobalTokenCount() + 1;
            var props = new Hashtable { { RoomKeyTokens, next } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);

            if (next >= TotalTokens)
            {
                var endProps = new Hashtable { { RoomKeyGameOver, 1 } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(endProps);
            }
        }
        else
        {
            offlineTokenCount++;
            OnTokensChanged?.Invoke(offlineTokenCount);

            if (offlineTokenCount >= TotalTokens)
                ShowEndScreen(true);
        }
    }

    // Any client can call this — Photon propagates the property to all clients immediately
    public static void TriggerDeath()
    {
        Debug.Log($"GameManager.TriggerDeath called — Instance={Instance != null}, gameEnded={Instance?.gameEnded}");
        if (Instance == null || Instance.gameEnded) return;

        if (PhotonNetwork.InRoom)
        {
            var props = new Hashtable { { RoomKeyGameOver, 2 } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }
        else
        {
            Instance.ShowEndScreen(false);
        }
    }

    public override void OnRoomPropertiesUpdate(Hashtable changedProps)
    {
        if (changedProps.ContainsKey(RoomKeyTokens))
        {
            int count = (int)changedProps[RoomKeyTokens];
            OnTokensChanged?.Invoke(count);
        }

        if (changedProps.ContainsKey(RoomKeyGameOver))
        {
            int state = (int)changedProps[RoomKeyGameOver];
            ShowEndScreen(state == 1);
        }
    }

    private void ShowEndScreen(bool escaped)
    {
        if (gameEnded) return;
        gameEnded = true;
        BuildEndScreen(escaped ? escapedSprite : deadSprite);
    }

    private void BuildEndScreen(Sprite image)
    {
        var canvasGO = new GameObject("EndScreen");
        DontDestroyOnLoad(canvasGO);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Full black background
        var bg = new GameObject("BG");
        bg.transform.SetParent(canvas.transform, false);
        StretchRect(bg.AddComponent<RectTransform>());
        bg.AddComponent<Image>().color = Color.black;

        // End image — fills the screen, aspect-ratio preserved
        if (image != null)
        {
            var imgGO = new GameObject("EndImage");
            imgGO.transform.SetParent(canvas.transform, false);
            var rt = imgGO.AddComponent<RectTransform>();
            StretchRect(rt);
            var img = imgGO.AddComponent<Image>();
            img.sprite = image;
            img.preserveAspect = true;
        }
    }

    private static void StretchRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
