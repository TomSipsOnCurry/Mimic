using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class MultiplayerBootstrap : MonoBehaviour
{
    [SerializeField] private GameObject playerTemplate;
    [SerializeField] private string connectAddress = "127.0.0.1";
    [SerializeField] private ushort port = 7777;

    private readonly Dictionary<int, PlayerAvatar> playerAvatars = new Dictionary<int, PlayerAvatar>();
    private readonly Dictionary<int, NetworkMessage> playerStates = new Dictionary<int, NetworkMessage>();
    private readonly List<ClientConnection> clientConnections = new List<ClientConnection>();
    private readonly ConcurrentQueue<QueuedMessage> incomingMessages = new ConcurrentQueue<QueuedMessage>();

    private CancellationTokenSource cancellationSource;
    private TcpListener listener;
    private ClientConnection localConnection;
    private GameObject fallbackCameraObject;
    private Camera fallbackCamera;
    private bool initialized;
    private bool isHosting;
    private bool isConnected;
    private int localPlayerId = -1;
    private int nextPlayerId = 1;
    private float snapshotTimer;
    private float smoothedDeltaTime;
    private string status = "Offline";

    private const float SnapshotInterval = 0.05f;
    private const float FrameRateSmoothing = 0.1f;

    [Serializable]
    private class NetworkMessage
    {
        public string type;
        public int id;
        public Vector3 position;
        public Quaternion rotation;
    }

    private sealed class ClientConnection
    {
        public int playerId = -1;
        public TcpClient client;
        public StreamReader reader;
        public StreamWriter writer;
        public readonly object sendLock = new object();
    }

    private sealed class QueuedMessage
    {
        public ClientConnection connection;
        public NetworkMessage message;
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        ForceWindowedMode();
        InitializeNetworking();
    }

    private void ForceWindowedMode()
    {
        Screen.fullScreenMode = FullScreenMode.Windowed;
        Screen.fullScreen = false;

        if (Screen.currentResolution.width > 0 && Screen.currentResolution.height > 0)
        {
            int width = Mathf.Min(1280, Screen.currentResolution.width);
            int height = Mathf.Min(720, Screen.currentResolution.height);
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
        }
    }

    private void Update()
    {
        UpdateFrameRate();

        if (!initialized)
        {
            return;
        }

        ProcessIncomingMessages();
        UpdateLocalState();
        PumpSnapshots();
    }

    private void OnDestroy()
    {
        ShutdownSession();
    }

    private void InitializeNetworking()
    {
        if (initialized)
        {
            return;
        }

        if (playerTemplate == null)
        {
            playerTemplate = GameObject.Find("Player");
        }

        if (playerTemplate == null)
        {
            Debug.LogError("MultiplayerBootstrap could not find the Player template in the scene.");
            enabled = false;
            return;
        }

        playerTemplate.SetActive(false);
        EnsureFallbackCamera();
        initialized = true;
    }

    private void EnsureFallbackCamera()
    {
        if (fallbackCamera != null)
        {
            fallbackCamera.enabled = true;
            return;
        }

        GameObject cameraSource = null;
        Camera templateCamera = playerTemplate != null ? playerTemplate.GetComponentInChildren<Camera>(true) : null;
        if (templateCamera != null)
        {
            cameraSource = templateCamera.gameObject;
        }

        fallbackCameraObject = new GameObject("Fallback Camera");
        fallbackCamera = fallbackCameraObject.AddComponent<Camera>();
        fallbackCamera.tag = "MainCamera";

        if (cameraSource != null)
        {
            fallbackCamera.CopyFrom(templateCamera);
            fallbackCameraObject.transform.SetPositionAndRotation(cameraSource.transform.position, cameraSource.transform.rotation);
        }
        else if (playerTemplate != null)
        {
            fallbackCameraObject.transform.SetPositionAndRotation(playerTemplate.transform.position + new Vector3(0f, 0.85f, 0f), playerTemplate.transform.rotation);
        }

        fallbackCameraObject.AddComponent<AudioListener>();
    }

    private void SetFallbackCameraEnabled(bool isEnabled)
    {
        if (fallbackCamera != null)
        {
            fallbackCamera.enabled = isEnabled;
        }

        AudioListener fallbackAudio = fallbackCameraObject != null ? fallbackCameraObject.GetComponent<AudioListener>() : null;
        if (fallbackAudio != null)
        {
            fallbackAudio.enabled = isEnabled;
        }
    }

    private void OnGUI()
    {
        DrawFrameRateCounter();

        const int panelWidth = 340;
        const int panelHeight = 190;

        Rect panelRect = new Rect(16, 16, panelWidth, panelHeight);
        GUI.Box(panelRect, "Multiplayer");

        GUILayout.BeginArea(new Rect(panelRect.x + 12, panelRect.y + 28, panelRect.width - 24, panelRect.height - 40));
        GUILayout.Label(status);
        GUILayout.Label("Host address");
        connectAddress = GUILayout.TextField(connectAddress);
        GUILayout.Label($"Port: {port}");

        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Host", GUILayout.Height(32)))
            {
                StartHost();
            }

            if (GUILayout.Button("Join", GUILayout.Height(32)))
            {
                StartClient();
            }
        }

        if (isConnected)
        {
            if (GUILayout.Button("Disconnect", GUILayout.Height(28)))
            {
                ShutdownSession();
                status = "Offline";
            }
        }

        GUILayout.EndArea();
    }

    private void UpdateFrameRate()
    {
        float currentDeltaTime = Time.unscaledDeltaTime;
        if (currentDeltaTime <= 0f)
        {
            return;
        }

        smoothedDeltaTime = smoothedDeltaTime <= 0f
            ? currentDeltaTime
            : Mathf.Lerp(smoothedDeltaTime, currentDeltaTime, FrameRateSmoothing);
    }

    private void DrawFrameRateCounter()
    {
        if (smoothedDeltaTime <= 0f)
        {
            return;
        }

        int framesPerSecond = Mathf.RoundToInt(1f / smoothedDeltaTime);
        GUI.Box(new Rect(Screen.width - 96, 16, 80, 24), $"{framesPerSecond} FPS");
    }

    private void StartHost()
    {
        InitializeNetworking();
        if (!initialized || isConnected)
        {
            return;
        }

        ResetSessionState();
        SetFallbackCameraEnabled(false);
        isHosting = true;
        isConnected = true;
        localPlayerId = 0;
        cancellationSource = new CancellationTokenSource();

        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        PlayerAvatar localAvatar = CreateAvatar(0, true, playerTemplate.transform.position, playerTemplate.transform.rotation);
        playerAvatars[0] = localAvatar;
        playerStates[0] = CreateMessage("STATE", 0, localAvatar.transform.position, localAvatar.transform.rotation);

        _ = Task.Run(() => AcceptClientsLoop(cancellationSource.Token));
        status = $"Hosting on port {port}";
    }

    private async void StartClient()
    {
        InitializeNetworking();
        if (!initialized || isConnected)
        {
            return;
        }

        ResetSessionState();
        SetFallbackCameraEnabled(false);
        isHosting = false;
        isConnected = true;
        cancellationSource = new CancellationTokenSource();

        try
        {
            TcpClient client = new TcpClient();
            client.NoDelay = true;
            await client.ConnectAsync(connectAddress, port);

            localConnection = CreateConnection(client);
            StartReadLoop(localConnection, cancellationSource.Token);
            SendMessage(localConnection, CreateMessage("HELLO", 0, Vector3.zero, Quaternion.identity));
            status = $"Connecting to {connectAddress}:{port}...";
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to connect: {exception.Message}");
            ShutdownSession();
            status = "Offline";
        }
    }

    private async Task AcceptClientsLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (token.IsCancellationRequested)
            {
                break;
            }

            client.NoDelay = true;
            ClientConnection connection = CreateConnection(client);

            lock (clientConnections)
            {
                clientConnections.Add(connection);
            }

            connection.playerId = nextPlayerId++;
            StartReadLoop(connection, token);
            SendMessage(connection, CreateMessage("WELCOME", connection.playerId, Vector3.zero, Quaternion.identity));
            incomingMessages.Enqueue(new QueuedMessage
            {
                connection = connection,
                message = CreateMessage("HOST_JOIN", connection.playerId, Vector3.zero, Quaternion.identity)
            });
        }
    }

    private void StartReadLoop(ClientConnection connection, CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && connection.client != null && connection.client.Connected)
                {
                    string line = await connection.reader.ReadLineAsync();
                    if (line == null)
                    {
                        break;
                    }

                    NetworkMessage message = JsonUtility.FromJson<NetworkMessage>(line);
                    if (message != null)
                    {
                        incomingMessages.Enqueue(new QueuedMessage
                        {
                            connection = connection,
                            message = message
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Connection closed or interrupted.
            }
            finally
            {
                incomingMessages.Enqueue(new QueuedMessage
                {
                    connection = connection,
                    message = CreateMessage("DISCONNECT", connection.playerId, Vector3.zero, Quaternion.identity)
                });
            }
        }, token);
    }

    private void ProcessIncomingMessages()
    {
        while (incomingMessages.TryDequeue(out QueuedMessage queuedMessage))
        {
            HandleNetworkMessage(queuedMessage.connection, queuedMessage.message);
        }
    }

    private void HandleNetworkMessage(ClientConnection connection, NetworkMessage message)
    {
        switch (message.type)
        {
            case "HOST_JOIN":
                if (isHosting)
                {
                    HandleHostJoin(connection, message.id);
                }
                break;
            case "WELCOME":
                HandleWelcome(message.id);
                break;
            case "SPAWN":
                HandleSpawn(message);
                break;
            case "STATE":
                HandleState(message);
                break;
            case "DISCONNECT":
                HandleDisconnect(connection, message.id);
                break;
        }
    }

    private void HandleHostJoin(ClientConnection connection, int playerId)
    {
        connection.playerId = playerId;

        PlayerAvatar avatar = CreateAvatar(playerId, false, playerTemplate.transform.position, playerTemplate.transform.rotation);
        playerAvatars[playerId] = avatar;
        playerStates[playerId] = CreateMessage("STATE", playerId, avatar.transform.position, avatar.transform.rotation);

        foreach (KeyValuePair<int, NetworkMessage> entry in playerStates)
        {
            SendMessage(connection, CreateMessage("SPAWN", entry.Key, entry.Value.position, entry.Value.rotation));
        }

        BroadcastToClients(CreateMessage("SPAWN", playerId, avatar.transform.position, avatar.transform.rotation), connection);
        status = $"Player {playerId} joined";
    }

    private void HandleWelcome(int playerId)
    {
        localPlayerId = playerId;

        if (!playerAvatars.ContainsKey(playerId))
        {
            PlayerAvatar localAvatar = CreateAvatar(playerId, true, playerTemplate.transform.position, playerTemplate.transform.rotation);
            playerAvatars[playerId] = localAvatar;
            playerStates[playerId] = CreateMessage("STATE", playerId, localAvatar.transform.position, localAvatar.transform.rotation);
        }
        else
        {
            playerAvatars[playerId].Configure(playerId, true);
        }

        status = $"Connected as player {playerId}";
    }

    private void HandleSpawn(NetworkMessage message)
    {
        if (message.id == localPlayerId)
        {
            if (playerAvatars.TryGetValue(message.id, out PlayerAvatar existingAvatar))
            {
                existingAvatar.Configure(message.id, true);
                existingAvatar.transform.SetPositionAndRotation(message.position, message.rotation);
            }
            return;
        }

        if (!playerAvatars.ContainsKey(message.id))
        {
            PlayerAvatar remoteAvatar = CreateAvatar(message.id, false, message.position, message.rotation);
            playerAvatars[message.id] = remoteAvatar;
        }
        else
        {
            playerAvatars[message.id].ApplySnapshot(message.position, message.rotation);
        }

        playerStates[message.id] = CreateMessage("STATE", message.id, message.position, message.rotation);
    }

    private void HandleState(NetworkMessage message)
    {
        playerStates[message.id] = message;

        if (message.id == localPlayerId)
        {
            return;
        }

        if (playerAvatars.TryGetValue(message.id, out PlayerAvatar avatar))
        {
            avatar.ApplySnapshot(message.position, message.rotation);
        }
    }

    private void HandleDisconnect(ClientConnection connection, int playerId)
    {
        if (playerId >= 0)
        {
            RemoveAvatar(playerId);
            playerStates.Remove(playerId);

            if (isHosting)
            {
                BroadcastToClients(CreateMessage("DESPAWN", playerId, Vector3.zero, Quaternion.identity), connection);
            }
        }

        if (connection != null)
        {
            CleanupConnection(connection);
        }
    }

    private void UpdateLocalState()
    {
        if (!isConnected || localPlayerId < 0)
        {
            return;
        }

        if (!playerAvatars.TryGetValue(localPlayerId, out PlayerAvatar localAvatar))
        {
            return;
        }

        playerStates[localPlayerId] = CreateMessage("STATE", localPlayerId, localAvatar.transform.position, localAvatar.transform.rotation);
    }

    private void PumpSnapshots()
    {
        if (!isConnected)
        {
            return;
        }

        snapshotTimer += Time.deltaTime;
        if (snapshotTimer < SnapshotInterval)
        {
            return;
        }

        snapshotTimer = 0f;

        if (isHosting)
        {
            BroadcastStates();
        }
        else if (localConnection != null && localPlayerId >= 0 && playerStates.TryGetValue(localPlayerId, out NetworkMessage localState))
        {
            SendMessage(localConnection, localState);
        }
    }

    private void BroadcastStates()
    {
        lock (clientConnections)
        {
            for (int connectionIndex = clientConnections.Count - 1; connectionIndex >= 0; connectionIndex--)
            {
                ClientConnection connection = clientConnections[connectionIndex];
                if (connection == null || connection.client == null || !connection.client.Connected)
                {
                    clientConnections.RemoveAt(connectionIndex);
                    continue;
                }

                foreach (KeyValuePair<int, NetworkMessage> entry in playerStates)
                {
                    if (entry.Key < 0)
                    {
                        continue;
                    }

                    SendMessage(connection, CreateMessage("STATE", entry.Key, entry.Value.position, entry.Value.rotation));
                }
            }
        }
    }

    private void BroadcastToClients(NetworkMessage message, ClientConnection excludeConnection)
    {
        lock (clientConnections)
        {
            foreach (ClientConnection connection in clientConnections)
            {
                if (connection == null || connection == excludeConnection)
                {
                    continue;
                }

                SendMessage(connection, message);
            }
        }
    }

    private void RemoveAvatar(int playerId)
    {
        if (playerAvatars.TryGetValue(playerId, out PlayerAvatar avatar))
        {
            if (avatar != null)
            {
                Destroy(avatar.gameObject);
            }

            playerAvatars.Remove(playerId);
        }
    }

    private PlayerAvatar CreateAvatar(int playerId, bool isLocalPlayer, Vector3 position, Quaternion rotation)
    {
        GameObject avatarObject = Instantiate(playerTemplate, position, rotation);
        avatarObject.name = isLocalPlayer ? $"Player_{playerId}_Local" : $"Player_{playerId}";

        PlayerAvatar avatar = avatarObject.GetComponent<PlayerAvatar>();
        if (avatar == null)
        {
            avatar = avatarObject.AddComponent<PlayerAvatar>();
        }

        avatarObject.SetActive(false);
        avatar.Configure(playerId, isLocalPlayer);
        avatarObject.SetActive(true);
        return avatar;
    }

    private ClientConnection CreateConnection(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        return new ClientConnection
        {
            client = client,
            reader = new StreamReader(stream, Encoding.UTF8),
            writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true }
        };
    }

    private NetworkMessage CreateMessage(string type, int id, Vector3 position, Quaternion rotation)
    {
        return new NetworkMessage
        {
            type = type,
            id = id,
            position = position,
            rotation = rotation
        };
    }

    private void SendMessage(ClientConnection connection, NetworkMessage message)
    {
        if (connection == null || connection.writer == null)
        {
            return;
        }

        try
        {
            string serializedMessage = JsonUtility.ToJson(message);
            lock (connection.sendLock)
            {
                connection.writer.WriteLine(serializedMessage);
            }
        }
        catch (Exception)
        {
            CleanupConnection(connection);
        }
    }

    private void CleanupConnection(ClientConnection connection)
    {
        if (connection == null)
        {
            return;
        }

        lock (clientConnections)
        {
            clientConnections.Remove(connection);
        }

        if (connection.client != null)
        {
            try
            {
                connection.client.Close();
            }
            catch (Exception)
            {
                // Ignore close failures.
            }
        }
    }

    private void ResetSessionState()
    {
        ShutdownSession();
        playerAvatars.Clear();
        playerStates.Clear();
        nextPlayerId = 1;
        snapshotTimer = 0f;
        status = "Offline";
    }

    private void ShutdownSession()
    {
        if (cancellationSource != null)
        {
            cancellationSource.Cancel();
            cancellationSource.Dispose();
            cancellationSource = null;
        }

        if (listener != null)
        {
            try
            {
                listener.Stop();
            }
            catch (Exception)
            {
                // Ignore listener shutdown issues.
            }

            listener = null;
        }

        if (localConnection != null)
        {
            CleanupConnection(localConnection);
            localConnection = null;
        }

        lock (clientConnections)
        {
            for (int connectionIndex = clientConnections.Count - 1; connectionIndex >= 0; connectionIndex--)
            {
                CleanupConnection(clientConnections[connectionIndex]);
            }

            clientConnections.Clear();
        }

        foreach (KeyValuePair<int, PlayerAvatar> avatarEntry in playerAvatars)
        {
            if (avatarEntry.Value != null)
            {
                Destroy(avatarEntry.Value.gameObject);
            }
        }

        playerAvatars.Clear();
        playerStates.Clear();
        localPlayerId = -1;
        isHosting = false;
        isConnected = false;

        if (fallbackCamera != null)
        {
            SetFallbackCameraEnabled(true);
        }
    }
}
