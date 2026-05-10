using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;

// Static game state — never needs instantiation, always accessible from any script.
// Handles token counting, win/lose, and Photon sync via a lightweight PunCallbacks helper.
public static class GameState
{
    public const int TotalTokens = 7;

    private const string RoomKeyTokens  = "tk";
    private const string RoomKeyGameOver = "go"; // 1 = escaped, 2 = dead

    // Raw static counter (offline + authoritative on MasterClient for online)
    private static int _tokenCount;
    private static bool _gameOver;

    // Reset every time Play mode starts
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        _tokenCount = 0;
        _gameOver   = false;
    }

    // ── Token counting ────────────────────────────────────────────────────────

    public static int TokenCount
    {
        get
        {
            if (PhotonNetwork.InRoom &&
                PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKeyTokens, out object val))
                return (int)val;

            return _tokenCount;
        }
    }

    // Call this on the MasterClient (or offline) when a token is collected
    public static void AddToken()
    {
        if (_gameOver) return;

        if (PhotonNetwork.InRoom)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            int next = TokenCount + 1;
            Debug.Log($"GameState.AddToken — online, next={next}");
            var props = new Hashtable { { RoomKeyTokens, next } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);

            if (next >= TotalTokens)
            {
                var end = new Hashtable { { RoomKeyGameOver, 1 } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(end);
            }
        }
        else
        {
            _tokenCount++;
            Debug.Log($"GameState.AddToken — offline, count={_tokenCount}");

            if (_tokenCount >= TotalTokens)
                ShowEndScreen(true);
        }
    }

    // ── Death ─────────────────────────────────────────────────────────────────

    public static void TriggerDeath()
    {
        if (_gameOver) return;
        Debug.Log("GameState.TriggerDeath called");

        if (PhotonNetwork.InRoom)
        {
            var props = new Hashtable { { RoomKeyGameOver, 2 } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }
        else
        {
            ShowEndScreen(false);
        }
    }

    // ── End screen ────────────────────────────────────────────────────────────

    public static void ShowEndScreen(bool escaped)
    {
        if (_gameOver) return;
        _gameOver = true;

        Sprite image = escaped ? LoadSprite("Textures/Escaped") : LoadSprite("Textures/Dead");
        BuildEndScreen(image);
    }

    private static void BuildEndScreen(Sprite image)
    {
        var canvasGO = new GameObject("EndScreen");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        // Full black background
        var bg = new GameObject("BG");
        bg.transform.SetParent(canvas.transform, false);
        Stretch(bg.AddComponent<RectTransform>());
        bg.AddComponent<Image>().color = Color.black;

        // Scaled image
        if (image != null)
        {
            var imgGO = new GameObject("EndImage");
            imgGO.transform.SetParent(canvas.transform, false);
            var rt = imgGO.AddComponent<RectTransform>();
            Stretch(rt);
            var img = imgGO.AddComponent<Image>();
            img.sprite = image;
            img.preserveAspect = true;
        }

        UnityEngine.Object.DontDestroyOnLoad(canvasGO);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = Resources.Load<Sprite>(path);
        if (sprite != null) return sprite;

        var tex = Resources.Load<Texture2D>(path);
        if (tex != null)
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        Debug.LogWarning($"GameState: could not load sprite at Resources/{path}");
        return null;
    }

    // ── Photon room property listener (created automatically at scene load) ───

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsurePhotonListener()
    {
        if (UnityEngine.Object.FindObjectOfType<PhotonStateListener>() != null) return;
        var go = new GameObject("PhotonStateListener");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<PhotonStateListener>();
    }
}

// Tiny MonoBehaviour whose only job is to receive Photon room property callbacks
public sealed class PhotonStateListener : MonoBehaviourPunCallbacks
{
    public override void OnRoomPropertiesUpdate(Hashtable changedProps)
    {
        if (changedProps.ContainsKey("go"))
        {
            int state = (int)changedProps["go"];
            GameState.ShowEndScreen(state == 1);
        }
        // Token count is read directly from room properties by GameState.TokenCount
        // so no action needed here — TokenProgressUI polls TokenCount every frame
    }
}
