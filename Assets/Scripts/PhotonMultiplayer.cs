using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using ExitGames.Client.Photon;

public class PhotonMultiplayer : MonoBehaviourPunCallbacks, IOnEventCallback
{
    private const byte ChatEventCode = 1;

    [SerializeField] private string roomCode = "1234";
    [SerializeField] private int maxPlayersPerRoom = 8;

    public string Status { get; private set; } = "Disconnected";
    public bool IsConnected => PhotonNetwork.InRoom;
    public string SessionCode => roomCode;

    public delegate void OnChatReceived(string sender, string message);
    public static event OnChatReceived ChatReceived;

    public delegate void OnChatReceivedWithActor(int actorNumber, string sender, string message);
    public static event OnChatReceivedWithActor ChatReceivedWithActor;

    private string lastRoomCode;
    private static PhotonMultiplayer instance;
    private static GameObject localPlayerObject;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
        localPlayerObject = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
#if UNITY_2023_1_OR_NEWER
        if (FindAnyObjectByType<PhotonMultiplayer>() != null)
#else
        if (FindObjectOfType<PhotonMultiplayer>() != null)
#endif
        {
            return;
        }

        GameObject bootstrap = new GameObject(nameof(PhotonMultiplayer));
        DontDestroyOnLoad(bootstrap);
        bootstrap.AddComponent<PhotonMultiplayer>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (!PhotonNetwork.IsConnected)
        {
            Status = "Connecting to Photon...";
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    public void SetSessionCode(string code)
    {
        roomCode = string.IsNullOrWhiteSpace(code) ? roomCode : code.Trim();
    }

    public void StartHost()
    {
        roomCode = GenerateRoomCode();
        lastRoomCode = roomCode;
        Status = $"Creating room '{roomCode}'...";

        RoomOptions roomOpts = new RoomOptions { MaxPlayers = (byte)maxPlayersPerRoom };
        PhotonNetwork.CreateRoom(roomCode, roomOpts, TypedLobby.Default);
    }

    public void StartClient(string address = "")
    {
        // In Photon, we ignore the address and join by room code instead
        lastRoomCode = roomCode;
        Status = $"Joining room '{roomCode}'...";

        PhotonNetwork.JoinRoom(roomCode);
    }

    public void StopSession()
    {
        Status = "Disconnected";
        localPlayerObject = null;
        PhotonNetwork.LeaveRoom();
    }

    public void SendChatMessage(string message)
    {
        if (!PhotonNetwork.InRoom || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string nickname = string.IsNullOrWhiteSpace(PhotonNetwork.NickName)
            ? $"Player {PhotonNetwork.LocalPlayer.ActorNumber}"
            : PhotonNetwork.NickName;
        object[] content = { nickname, message.Trim() };
        RaiseEventOptions options = new RaiseEventOptions { Receivers = ReceiverGroup.All };
        PhotonNetwork.RaiseEvent(ChatEventCode, content, options, SendOptions.SendReliable);
    }

    public override void OnConnected()
    {
        Status = "Connected to Photon";
        Debug.Log("PhotonMultiplayer: Connected to Photon server.");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Status = $"Disconnected ({cause})";
        localPlayerObject = null;
        Debug.LogWarning($"PhotonMultiplayer: Disconnected. Reason: {cause}");
    }

    public override void OnJoinedRoom()
    {
        Status = $"In room '{lastRoomCode}' ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})";
        Debug.Log($"PhotonMultiplayer: Joined room '{lastRoomCode}'");

        if (localPlayerObject != null)
        {
            PlayerMovement existingPlayer = localPlayerObject.GetComponent<PlayerMovement>();
            if (existingPlayer != null)
            {
                existingPlayer.SetPlayerVariantForActor(PhotonNetwork.LocalPlayer.ActorNumber);
            }

            RemoveExtraLocalPlayers(localPlayerObject);
            return;
        }

        if (TryFindLocalNetworkPlayer(out PlayerMovement foundPlayer))
        {
            localPlayerObject = foundPlayer.gameObject;
            foundPlayer.SetPlayerVariantForActor(PhotonNetwork.LocalPlayer.ActorNumber);
            RemoveExtraLocalPlayers(localPlayerObject);
            return;
        }

        DisableScenePlayerTemplates();

        GameObject playerObject = PhotonNetwork.Instantiate(
            "Player",
            Vector3.zero,
            Quaternion.identity
        );

        PlayerMovement playerMovement = playerObject.GetComponent<PlayerMovement>();
        if (playerMovement != null)
        {
            playerMovement.SetPlayerVariantForActor(PhotonNetwork.LocalPlayer.ActorNumber);
        }

        localPlayerObject = playerObject;
        RemoveExtraLocalPlayers(localPlayerObject);
    }

    public override void OnLeftRoom()
    {
        localPlayerObject = null;
    }

    private static bool TryFindLocalNetworkPlayer(out PlayerMovement playerMovement)
    {
        playerMovement = null;

#if UNITY_2023_1_OR_NEWER
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
#endif

        for (int i = 0; i < players.Length; i++)
        {
            PlayerMovement candidate = players[i];
            if (candidate == null)
            {
                continue;
            }

            PhotonView candidateView = candidate.GetComponent<PhotonView>();
            if (candidateView != null && candidateView.IsMine && candidateView.ViewID != 0)
            {
                playerMovement = candidate;
                return true;
            }
        }

        return false;
    }

    private static void DisableScenePlayerTemplates()
    {
#if UNITY_2023_1_OR_NEWER
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
#endif

        for (int i = 0; i < players.Length; i++)
        {
            PlayerMovement candidate = players[i];
            if (candidate == null)
            {
                continue;
            }

            PhotonView candidateView = candidate.GetComponent<PhotonView>();
            if (candidateView != null && candidateView.ViewID != 0)
            {
                continue;
            }

            candidate.gameObject.SetActive(false);
        }
    }

    private static void RemoveExtraLocalPlayers(GameObject keepObject)
    {
#if UNITY_2023_1_OR_NEWER
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
#endif

        for (int i = 0; i < players.Length; i++)
        {
            PlayerMovement candidate = players[i];
            if (candidate == null || candidate.gameObject == keepObject)
            {
                continue;
            }

            PhotonView candidateView = candidate.GetComponent<PhotonView>();
            if (candidateView != null && candidateView.IsMine && candidateView.ViewID != 0)
            {
                PhotonNetwork.Destroy(candidate.gameObject);
                continue;
            }

            if (candidateView == null || candidateView.ViewID == 0)
            {
                candidate.gameObject.SetActive(false);
            }
        }
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Status = "Room not found (create it first)";
        Debug.LogWarning($"PhotonMultiplayer: Join failed. {message}");
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"PhotonMultiplayer: Create failed. {message}");
        if (returnCode != ErrorCode.GameIdAlreadyExists)
        {
            Status = "Failed to create room";
            return;
        }

        roomCode = GenerateRoomCode();
        lastRoomCode = roomCode;
        Status = $"Creating room '{roomCode}'...";
        RoomOptions roomOpts = new RoomOptions { MaxPlayers = (byte)maxPlayersPerRoom };
        PhotonNetwork.CreateRoom(roomCode, roomOpts, TypedLobby.Default);
    }

    private static string GenerateRoomCode()
    {
        return Random.Range(1000, 10000).ToString();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"PhotonMultiplayer: Player {newPlayer.NickName} joined room.");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"PhotonMultiplayer: Player {otherPlayer.NickName} left room.");
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code != ChatEventCode)
        {
            return;
        }

        string sender = null;
        string message = null;

        if (photonEvent.CustomData is object[] data)
        {
            if (data.Length > 0)
            {
                sender = data[0] as string;
            }

            if (data.Length > 1)
            {
                message = data[1] as string;
            }
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sender))
        {
            Player senderPlayer = PhotonNetwork.CurrentRoom?.GetPlayer(photonEvent.Sender);
            sender = senderPlayer?.NickName ?? $"Player {photonEvent.Sender}";
        }

        Debug.Log($"[CHAT] {sender}: {message}");
        ChatReceived?.Invoke(sender, message);
        ChatReceivedWithActor?.Invoke(photonEvent.Sender, sender, message);
    }
}
