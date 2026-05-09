using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class SimpleLanMultiplayer : MonoBehaviour
{
    [Header("Connection")]
    [SerializeField] private string joinAddress = "127.0.0.1";
    [SerializeField] private int port = 7777;

    [Tooltip("UDP port used for very simple LAN host discovery.")]
    [SerializeField] private int discoveryPort = 7778;

    [Tooltip("How often the host broadcasts its presence on LAN.")]
    [SerializeField] private float hostBeaconInterval = 0.5f;

    [Tooltip("Clients must provide this code to join the host.")]
    [SerializeField] private string sessionCode = "1234";

    [Header("Replication")]
    [SerializeField] private float sendInterval = 0.05f;

    public string Status { get; private set; } = "Disconnected";
    public string SessionCode => sessionCode;
    public bool IsConnected => isConnected;

    public delegate void OnChatReceived(string sender, string message);
    public static event OnChatReceived ChatReceived;

    private const int HostPlayerId = 1;

    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private readonly Dictionary<int, RemotePlayer> remotePlayers = new Dictionary<int, RemotePlayer>();
    private readonly Dictionary<int, Vector2> knownPositions = new Dictionary<int, Vector2>();
    private readonly List<ServerPeer> serverPeers = new List<ServerPeer>();

    private PlayerMovement localPlayer;
    private TcpListener serverListener;
    private TcpClient clientSocket;
    private StreamReader clientReader;
    private StreamWriter clientWriter;
    private Thread serverAcceptThread;
    private Thread clientReadThread;
    private Thread hostBeaconThread;
    private Thread discoveryThread;
    private UdpClient hostBeaconSocket;
    private UdpClient discoverySocket;
    private bool isHost;
    private bool isConnected;
    private volatile bool runHostBeacon;
    private volatile bool runDiscovery;
    private int localPlayerId = -1;
    private int nextPlayerId = HostPlayerId + 1;
    private float sendTimer;

    [Serializable]
    private class NetMessage
    {
        public string type;
        public int id;
        public float x;
        public float y;
        public string code;
        public string text;
        public string sender;
    }

    private sealed class RemotePlayer
    {
        public Transform transform;
        public Vector2 targetPosition;
    }

    private sealed class ServerPeer
    {
        public int playerId;
        public TcpClient socket;
        public StreamReader reader;
        public StreamWriter writer;
        public Thread readThread;
        public bool authenticated;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
#if UNITY_2023_1_OR_NEWER
        if (FindAnyObjectByType<SimpleLanMultiplayer>() != null)
#else
        if (FindObjectOfType<SimpleLanMultiplayer>() != null)
#endif
        {
            return;
        }

        GameObject bootstrap = new GameObject(nameof(SimpleLanMultiplayer));
        DontDestroyOnLoad(bootstrap);
        bootstrap.AddComponent<SimpleLanMultiplayer>();
    }

    private void Start()
    {
        FindLocalPlayer();
    }

    private void Update()
    {
        ExecuteMainThreadActions();
        FindLocalPlayer();
        HandleDebugKeys();
        UpdateRemotePlayers();
        SendLocalStateIfNeeded();
    }

    private void OnDestroy()
    {
        StopSession();
    }

    public void SetJoinAddress(string address)
    {
        joinAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
    }

    public void SetSessionCode(string code)
    {
        sessionCode = string.IsNullOrWhiteSpace(code) ? sessionCode : code.Trim();
    }

    public void SendChatMessage(string message)
    {
        if (!isConnected || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string sender = isHost ? "Host" : $"Player{localPlayerId}";
        var chatMsg = new NetMessage
        {
            type = "CHAT",
            id = localPlayerId,
            text = message,
            sender = sender
        };

        if (isHost)
        {
            BroadcastFromHost(chatMsg, null);
            mainThreadActions.Enqueue(() => ChatReceived?.Invoke(sender, message));
        }
        else
        {
            SendToClient(chatMsg);
        }
    }

    public void StartHost()
    {
        if (isConnected)
        {
            StopSession();
        }

        FindLocalPlayer();
        if (localPlayer == null)
        {
            Debug.LogError("SimpleLanMultiplayer: No PlayerMovement found.");
            return;
        }

        if (string.IsNullOrWhiteSpace(sessionCode))
        {
            sessionCode = UnityEngine.Random.Range(1000, 10000).ToString();
        }

        isHost = true;
        isConnected = true;
        localPlayerId = HostPlayerId;
        Status = $"Hosting :{port} (Code {sessionCode})";
        nextPlayerId = HostPlayerId + 1;
        knownPositions[localPlayerId] = localPlayer.transform.position;

        serverListener = new TcpListener(IPAddress.Any, port);
        serverListener.Start();
        serverAcceptThread = new Thread(ServerAcceptLoop) { IsBackground = true };
        serverAcceptThread.Start();

        StartHostBeacon();

        Debug.Log($"SimpleLanMultiplayer: Hosting on port {port} with code '{sessionCode}'");
    }

    public void StartClient()
    {
        StartClient(joinAddress);
    }

    public void StartClient(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            StartClientAutoDiscover();
            return;
        }

        if (isConnected)
        {
            StopSession();
        }

        FindLocalPlayer();
        if (localPlayer == null)
        {
            Debug.LogError("SimpleLanMultiplayer: No PlayerMovement found.");
            return;
        }

        ConnectClient(address);
    }

    public void StopSession()
    {
        isConnected = false;
        isHost = false;
        localPlayerId = -1;
        sendTimer = 0f;
        Status = "Disconnected";

        StopHostBeacon();
        StopDiscovery();

        if (serverListener != null)
        {
            serverListener.Stop();
            serverListener = null;
        }

        if (clientSocket != null)
        {
            clientSocket.Close();
            clientSocket = null;
        }

        clientReader = null;
        clientWriter = null;

        lock (serverPeers)
        {
            foreach (ServerPeer peer in serverPeers)
            {
                peer.socket?.Close();
            }

            serverPeers.Clear();
        }

        foreach (KeyValuePair<int, RemotePlayer> entry in remotePlayers)
        {
            if (entry.Value.transform != null)
            {
                Destroy(entry.Value.transform.gameObject);
            }
        }

        remotePlayers.Clear();
        knownPositions.Clear();
        while (mainThreadActions.TryDequeue(out _))
        {
        }
    }

    private void ConnectClient(string address)
    {
        if (isConnected)
        {
            StopSession();
        }

        FindLocalPlayer();
        if (localPlayer == null)
        {
            Debug.LogError("SimpleLanMultiplayer: No PlayerMovement found.");
            return;
        }

        try
        {
            clientSocket = new TcpClient();
            clientSocket.Connect(address, port);
            NetworkStream stream = clientSocket.GetStream();
            clientReader = new StreamReader(stream, Encoding.UTF8);
            clientWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            clientReadThread = new Thread(ClientReadLoop) { IsBackground = true };
            clientReadThread.Start();

            isHost = false;
            isConnected = true;
            Status = $"Connecting to {address}:{port}...";

            // Immediately send join code; host will reject if it doesn't match.
            SendToClient(new NetMessage { type = "JOIN", code = sessionCode });

            Debug.Log($"SimpleLanMultiplayer: Connected to {address}:{port}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"SimpleLanMultiplayer: Failed to connect to {address}:{port}. {exception}");
            StopSession();
        }
    }

    private void StartClientAutoDiscover()
    {
        if (isConnected)
        {
            StopSession();
        }

        FindLocalPlayer();
        if (localPlayer == null)
        {
            Debug.LogError("SimpleLanMultiplayer: No PlayerMovement found.");
            return;
        }

        StopDiscovery();
        runDiscovery = true;
        Status = $"Searching LAN... (Code {sessionCode})";

        discoveryThread = new Thread(DiscoveryLoop) { IsBackground = true };
        discoveryThread.Start();
    }

    private void DiscoveryLoop()
    {
        try
        {
            // Listen on all interfaces on the discovery port
            discoverySocket = new UdpClient(new IPEndPoint(IPAddress.Any, discoveryPort));
            discoverySocket.EnableBroadcast = true;
            discoverySocket.Client.ReceiveTimeout = 10000;

            Debug.Log("SimpleLanMultiplayer: Listening for host beacon...");

            while (runDiscovery)
            {
                try
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = discoverySocket.Receive(ref remote);
                    if (data == null || data.Length == 0)
                    {
                        continue;
                    }

                    string msg = Encoding.UTF8.GetString(data);
                    Debug.Log($"SimpleLanMultiplayer: Received beacon: {msg}");
                    
                    // Format: MIMIC_HOST|<code>|<port>
                    string[] parts = msg.Split('|');
                    if (parts.Length < 3 || parts[0] != "MIMIC_HOST")
                    {
                        continue;
                    }

                    string code = parts[1];
                    if (!string.Equals(code, sessionCode, StringComparison.Ordinal))
                    {
                        Debug.Log($"SimpleLanMultiplayer: Code mismatch (got {code}, expected {sessionCode})");
                        continue;
                    }

                    string hostIp = remote.Address.ToString();
                    Debug.Log($"SimpleLanMultiplayer: Found host at {hostIp}");
                    runDiscovery = false;
                    mainThreadActions.Enqueue(() => ConnectClient(hostIp));
                    return;
                }
                catch (SocketException)
                {
                    // Timeout is OK
                }
            }
        }
        catch (Exception exception)
        {
            if (runDiscovery)
            {
                Debug.LogWarning($"SimpleLanMultiplayer: Discovery failed. {exception.Message}");
                mainThreadActions.Enqueue(() => Status = "No LAN host found" );
            }
        }
        finally
        {
            try { discoverySocket?.Close(); } catch { }
            discoverySocket = null;
            runDiscovery = false;
        }
    }

    private void StopDiscovery()
    {
        runDiscovery = false;
        try { discoverySocket?.Close(); } catch { }
        discoverySocket = null;
        discoveryThread = null;
    }

    private void StartHostBeacon()
    {
        StopHostBeacon();
        runHostBeacon = true;

        hostBeaconThread = new Thread(HostBeaconLoop) { IsBackground = true };
        hostBeaconThread.Start();
    }

    private void HostBeaconLoop()
    {
        try
        {
            hostBeaconSocket = new UdpClient();
            hostBeaconSocket.EnableBroadcast = true;
            
            // Broadcast to all IPs on local subnet (255.255.255.255) and also to multicast
            IPEndPoint broadcast = new IPEndPoint(IPAddress.Broadcast, discoveryPort);

            int attempts = 0;
            while (runHostBeacon)
            {
                string payload = $"MIMIC_HOST|{sessionCode}|{port}";
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                
                try
                {
                    hostBeaconSocket.Send(bytes, bytes.Length, broadcast);
                    if ((attempts++ % 10) == 0)
                    {
                        Debug.Log($"SimpleLanMultiplayer: Broadcasting beacon: {payload}");
                    }
                }
                catch
                {
                    // ignore send errors
                }
                
                Thread.Sleep(Mathf.Max(50, Mathf.RoundToInt(hostBeaconInterval * 1000f)));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"SimpleLanMultiplayer: Beacon thread error: {ex.Message}");
        }
        finally
        {
            try { hostBeaconSocket?.Close(); } catch { }
            hostBeaconSocket = null;
            runHostBeacon = false;
        }
    }

    private void StopHostBeacon()
    {
        runHostBeacon = false;
        try { hostBeaconSocket?.Close(); } catch { }
        hostBeaconSocket = null;
        hostBeaconThread = null;
    }

    private void HandleDebugKeys()
    {
        if (Input.GetKeyDown(KeyCode.F5))
        {
            StartHost();
        }
        else if (Input.GetKeyDown(KeyCode.F6))
        {
            StartClient();
        }
        else if (Input.GetKeyDown(KeyCode.F7))
        {
            StopSession();
        }
    }

    private void FindLocalPlayer()
    {
        if (localPlayer != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        localPlayer = FindAnyObjectByType<PlayerMovement>();
#else
        localPlayer = FindObjectOfType<PlayerMovement>();
#endif
    }

    private void ExecuteMainThreadActions()
    {
        while (mainThreadActions.TryDequeue(out Action action))
        {
            action?.Invoke();
        }
    }

    private void SendLocalStateIfNeeded()
    {
        if (!isConnected || localPlayer == null || localPlayerId < 0)
        {
            return;
        }

        sendTimer += Time.deltaTime;
        if (sendTimer < sendInterval)
        {
            return;
        }

        sendTimer = 0f;
        Vector2 position = localPlayer.transform.position;
        knownPositions[localPlayerId] = position;

        NetMessage stateMessage = new NetMessage
        {
            type = "STATE",
            id = localPlayerId,
            x = position.x,
            y = position.y
        };

        if (isHost)
        {
            BroadcastFromHost(stateMessage, null);
            return;
        }

        SendToClient(stateMessage);
    }

    private void UpdateRemotePlayers()
    {
        foreach (KeyValuePair<int, RemotePlayer> entry in remotePlayers)
        {
            RemotePlayer remote = entry.Value;
            if (remote.transform == null)
            {
                continue;
            }

            Vector3 current = remote.transform.position;
            Vector3 target = remote.targetPosition;
            remote.transform.position = Vector3.Lerp(current, target, 15f * Time.deltaTime);
        }
    }

    private void ServerAcceptLoop()
    {
        while (serverListener != null)
        {
            TcpClient socket;

            try
            {
                socket = serverListener.AcceptTcpClient();
            }
            catch (Exception)
            {
                break;
            }

            int newPlayerId = nextPlayerId++;
            ServerPeer peer = CreatePeer(socket, newPlayerId);

            lock (serverPeers)
            {
                serverPeers.Add(peer);
            }

            // Peer must authenticate by sending a JOIN message with the correct code.
            peer.readThread.Start();
        }
    }

    private ServerPeer CreatePeer(TcpClient socket, int playerId)
    {
        NetworkStream stream = socket.GetStream();
        ServerPeer peer = new ServerPeer
        {
            playerId = playerId,
            socket = socket,
            reader = new StreamReader(stream, Encoding.UTF8),
            writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true },
            authenticated = false
        };

        peer.readThread = new Thread(() => ServerPeerReadLoop(peer)) { IsBackground = true };
        return peer;
    }

    private void ServerPeerReadLoop(ServerPeer peer)
    {
        try
        {
            while (peer.socket != null && peer.socket.Connected)
            {
                string line = peer.reader.ReadLine();
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }

                NetMessage message = JsonUtility.FromJson<NetMessage>(line);
                if (message == null)
                {
                    continue;
                }

                if (!peer.authenticated)
                {
                    if (message.type == "JOIN")
                    {
                        string provided = message.code ?? string.Empty;
                        bool ok = string.Equals(provided.Trim(), sessionCode, StringComparison.Ordinal);
                        if (!ok)
                        {
                            SendToPeer(peer, new NetMessage { type = "REJECT" });
                            break;
                        }

                        peer.authenticated = true;
                        mainThreadActions.Enqueue(() => OnPeerAuthenticated(peer.playerId, peer));
                    }

                    continue;
                }

                if (message.type != "STATE")
                {
                    continue;
                }

                message.id = peer.playerId;
                mainThreadActions.Enqueue(() =>
                {
                    Vector2 position = new Vector2(message.x, message.y);
                    knownPositions[message.id] = position;
                    SetRemoteTarget(message.id, position);
                    BroadcastFromHost(message, peer);
                });
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"SimpleLanMultiplayer: Peer read stopped for player {peer.playerId}. {exception.Message}");
        }

        mainThreadActions.Enqueue(() => HandlePeerDisconnect(peer.playerId));
    }

    private void OnPeerAuthenticated(int playerId, ServerPeer peer)
    {
        Vector2 spawnPos = localPlayer != null ? (Vector2)localPlayer.transform.position : Vector2.zero;
        SpawnRemotePlayer(playerId, spawnPos);
        knownPositions[playerId] = spawnPos;

        SendToPeer(peer, new NetMessage { type = "HELLO", id = playerId });

        foreach (KeyValuePair<int, Vector2> known in knownPositions)
        {
            if (known.Key == playerId)
            {
                continue;
            }

            SendToPeer(peer, new NetMessage { type = "SPAWN", id = known.Key });
            SendToPeer(peer, new NetMessage { type = "STATE", id = known.Key, x = known.Value.x, y = known.Value.y });
        }

        BroadcastFromHost(new NetMessage { type = "SPAWN", id = playerId }, peer);
        Debug.Log($"SimpleLanMultiplayer: Player {playerId} joined (code ok)." );
    }

    private void HandlePeerDisconnect(int playerId)
    {
        lock (serverPeers)
        {
            for (int index = serverPeers.Count - 1; index >= 0; index--)
            {
                if (serverPeers[index].playerId == playerId)
                {
                    serverPeers[index].socket?.Close();
                    serverPeers.RemoveAt(index);
                }
            }
        }

        RemoveRemotePlayer(playerId);
        knownPositions.Remove(playerId);
        BroadcastFromHost(new NetMessage { type = "DESPAWN", id = playerId }, null);
    }

    private void ClientReadLoop()
    {
        try
        {
            while (clientSocket != null && clientSocket.Connected)
            {
                string line = clientReader.ReadLine();
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }

                NetMessage message = JsonUtility.FromJson<NetMessage>(line);
                if (message == null)
                {
                    continue;
                }

                mainThreadActions.Enqueue(() => HandleClientMessage(message));
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"SimpleLanMultiplayer: Client read loop ended. {exception.Message}");
        }

        mainThreadActions.Enqueue(StopSession);
    }

    private void HandleClientMessage(NetMessage message)
    {
        if (message.type == "REJECT")
        {
            Status = "Join rejected (wrong code)";
            Debug.LogError("SimpleLanMultiplayer: Join rejected (wrong code)." );
            StopSession();
            return;
        }

        if (message.type == "CHAT")
        {
            mainThreadActions.Enqueue(() =>
            {
                ChatReceived?.Invoke(message.sender ?? "Unknown", message.text ?? "");
                if (isHost)
                {
                    BroadcastFromHost(message, null);
                }
            });
            return;
        }

        if (message.type == "HELLO")
        {
            localPlayerId = message.id;
            Status = $"Connected (id {localPlayerId})";
            return;
        }

        if (message.type == "SPAWN")
        {
            if (message.id != localPlayerId && !remotePlayers.ContainsKey(message.id))
            {
                SpawnRemotePlayer(message.id, Vector2.zero);
            }

            return;
        }

        if (message.type == "DESPAWN")
        {
            RemoveRemotePlayer(message.id);
            knownPositions.Remove(message.id);
            return;
        }

        if (message.type == "STATE")
        {
            if (message.id == localPlayerId)
            {
                return;
            }

            Vector2 position = new Vector2(message.x, message.y);
            knownPositions[message.id] = position;

            if (!remotePlayers.ContainsKey(message.id))
            {
                SpawnRemotePlayer(message.id, position);
            }
            else
            {
                SetRemoteTarget(message.id, position);
            }
        }
    }

    private void BroadcastFromHost(NetMessage message, ServerPeer exclude)
    {
        string payload = JsonUtility.ToJson(message);

        lock (serverPeers)
        {
            foreach (ServerPeer peer in serverPeers)
            {
                if (peer == exclude || !peer.authenticated)
                {
                    continue;
                }

                try
                {
                    peer.writer.WriteLine(payload);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"SimpleLanMultiplayer: Failed to send to player {peer.playerId}. {exception.Message}");
                }
            }
        }
    }

    private void SendToPeer(ServerPeer peer, NetMessage message)
    {
        if (peer == null || peer.writer == null)
        {
            return;
        }

        try
        {
            peer.writer.WriteLine(JsonUtility.ToJson(message));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"SimpleLanMultiplayer: Failed to send to player {peer.playerId}. {exception.Message}");
        }
    }

    private void SendToClient(NetMessage message)
    {
        if (clientWriter == null)
        {
            return;
        }

        try
        {
            clientWriter.WriteLine(JsonUtility.ToJson(message));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"SimpleLanMultiplayer: Failed to send state to host. {exception.Message}");
        }
    }

    private void SpawnRemotePlayer(int id, Vector2 position)
    {
        if (localPlayer == null || remotePlayers.ContainsKey(id))
        {
            return;
        }

        GameObject remoteObject = Instantiate(localPlayer.gameObject, position, Quaternion.identity);
        remoteObject.name = $"RemotePlayer_{id}";

        PlayerMovement movement = remoteObject.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            Destroy(movement);
        }

        Rigidbody2D remoteBody = remoteObject.GetComponent<Rigidbody2D>();
        if (remoteBody != null)
        {
            remoteBody.linearVelocity = Vector2.zero;
            remoteBody.angularVelocity = 0f;
            remoteBody.bodyType = RigidbodyType2D.Kinematic;
        }

        SpriteRenderer sprite = remoteObject.GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            float hue = Mathf.Repeat(id * 0.173f, 1f);
            sprite.color = Color.HSVToRGB(hue, 0.45f, 1f);
        }

        remotePlayers[id] = new RemotePlayer
        {
            transform = remoteObject.transform,
            targetPosition = position
        };
    }

    private void RemoveRemotePlayer(int id)
    {
        if (!remotePlayers.TryGetValue(id, out RemotePlayer remote))
        {
            return;
        }

        if (remote.transform != null)
        {
            Destroy(remote.transform.gameObject);
        }

        remotePlayers.Remove(id);
    }

    private void SetRemoteTarget(int id, Vector2 position)
    {
        if (!remotePlayers.TryGetValue(id, out RemotePlayer remote))
        {
            SpawnRemotePlayer(id, position);
            return;
        }

        remote.targetPosition = position;
    }
}
