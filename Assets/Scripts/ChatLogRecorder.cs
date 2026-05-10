using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public sealed class ChatLogRecorder : MonoBehaviour
{
    public const string ChatLogFileName = "chat_log.json";

    private const string VoiceAIDirectoryName = "VoiceAI";
    private const int MaxSavedMessages = 400;
    private const float DuplicateWindowSeconds = 0.75f;

    private static readonly ChatLogFile runtimeLog = new ChatLogFile();
    private static ChatLogRecorder instance;

    private string logPath;
    private string lastSignature;
    private float lastSignatureTime = -100f;

    [Serializable]
    public sealed class ChatLogFile
    {
        public List<ChatLogMessage> messages = new List<ChatLogMessage>();
    }

    [Serializable]
    public sealed class ChatLogMessage
    {
        public string sender;
        public string message;
        public string source;
        public string timestampUtc;
        public float gameTime;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
#if UNITY_2023_1_OR_NEWER
        if (FindAnyObjectByType<ChatLogRecorder>() != null)
#else
        if (FindObjectOfType<ChatLogRecorder>() != null)
#endif
        {
            return;
        }

        GameObject recorderObject = new GameObject("VoiceAI Chat Log Recorder");
        DontDestroyOnLoad(recorderObject);
        recorderObject.AddComponent<ChatLogRecorder>();
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
        logPath = GetChatLogPath();
        LoadExistingLog();
    }

    private void OnEnable()
    {
        PhotonMultiplayer.ChatReceived -= HandlePhotonChat;
        PhotonMultiplayer.ChatReceived += HandlePhotonChat;
        SimpleLanMultiplayer.ChatReceived -= HandleLanChat;
        SimpleLanMultiplayer.ChatReceived += HandleLanChat;
    }

    private void OnDisable()
    {
        PhotonMultiplayer.ChatReceived -= HandlePhotonChat;
        SimpleLanMultiplayer.ChatReceived -= HandleLanChat;
    }

    public static string GetChatLogPath()
    {
        return Path.Combine(GetVoiceAIDirectory(), ChatLogFileName);
    }

    public static List<ChatLogMessage> ReadMessagesSnapshot()
    {
        List<ChatLogMessage> snapshot = ReadMessagesFromDisk();
        if (snapshot.Count > 0)
        {
            return snapshot;
        }

        lock (runtimeLog)
        {
            for (int i = 0; i < runtimeLog.messages.Count; i++)
            {
                snapshot.Add(runtimeLog.messages[i]);
            }
        }

        return snapshot;
    }

    public static void RecordLocalChat(string sender, string message)
    {
        EnsureInstance();

        if (instance == null)
        {
            return;
        }

        instance.RecordChat(sender, message, "local");
    }

    private static List<ChatLogMessage> ReadMessagesFromDisk()
    {
        List<ChatLogMessage> messages = new List<ChatLogMessage>();
        string path = GetChatLogPath();

        if (!File.Exists(path))
        {
            return messages;
        }

        try
        {
            string json = File.ReadAllText(path);
            ChatLogFile loaded = JsonUtility.FromJson<ChatLogFile>(json);
            if (loaded != null && loaded.messages != null)
            {
                messages.AddRange(loaded.messages);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("ChatLogRecorder: Could not read chat log. " + exception.Message);
        }

        return messages;
    }

    private void HandlePhotonChat(string sender, string message)
    {
        RecordChat(sender, message, "photon");
    }

    private void HandleLanChat(string sender, string message)
    {
        RecordChat(sender, message, "lan");
    }

    private void RecordChat(string sender, string message, string source)
    {
        sender = CleanField(sender, "Unknown");
        message = CleanField(message, string.Empty);

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (string.Equals(sender, "MIMIC", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string signature = sender + "|" + message;
        float now = Time.unscaledTime;
        if (signature == lastSignature && now - lastSignatureTime <= DuplicateWindowSeconds)
        {
            return;
        }

        lastSignature = signature;
        lastSignatureTime = now;

        ChatLogMessage entry = new ChatLogMessage
        {
            sender = sender,
            message = message,
            source = source,
            timestampUtc = DateTime.UtcNow.ToString("o"),
            gameTime = Time.time
        };

        lock (runtimeLog)
        {
            runtimeLog.messages.Add(entry);
            while (runtimeLog.messages.Count > MaxSavedMessages)
            {
                runtimeLog.messages.RemoveAt(0);
            }
        }

        PersistLog();
    }

    private void LoadExistingLog()
    {
        List<ChatLogMessage> existingMessages = ReadMessagesFromDisk();

        lock (runtimeLog)
        {
            runtimeLog.messages.Clear();
            int startIndex = Mathf.Max(0, existingMessages.Count - MaxSavedMessages);
            for (int i = startIndex; i < existingMessages.Count; i++)
            {
                runtimeLog.messages.Add(existingMessages[i]);
            }
        }
    }

    private void PersistLog()
    {
        try
        {
            string directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = logPath + ".tmp";
            string json;
            lock (runtimeLog)
            {
                json = JsonUtility.ToJson(runtimeLog, true);
            }

            File.WriteAllText(tempPath, json);
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }

            File.Move(tempPath, logPath);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("ChatLogRecorder: Could not write chat log. " + exception.Message);
        }
    }

    private static string CleanField(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim();
    }

    private static string GetVoiceAIDirectory()
    {
#if UNITY_EDITOR
        string editorDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", VoiceAIDirectoryName));
        Directory.CreateDirectory(editorDirectory);
        return editorDirectory;
#else
        string buildRoot = GetBuildRootDirectory();
        string buildDirectory = Path.Combine(buildRoot, VoiceAIDirectoryName);
        if (TryPrepareWritableDirectory(buildDirectory))
        {
            return buildDirectory;
        }

        string persistentDirectory = Path.Combine(Application.persistentDataPath, VoiceAIDirectoryName);
        Directory.CreateDirectory(persistentDirectory);
        return persistentDirectory;
#endif
    }

#if !UNITY_EDITOR
    private static string GetBuildRootDirectory()
    {
        string dataPath = Application.dataPath;
        string normalizedDataPath = dataPath.Replace('\\', '/');
        DirectoryInfo dataDirectory = Directory.GetParent(dataPath);

        if (normalizedDataPath.EndsWith(".app/Contents", StringComparison.OrdinalIgnoreCase) &&
            dataDirectory != null && dataDirectory.Parent != null)
        {
            return dataDirectory.Parent.FullName;
        }

        return dataDirectory != null ? dataDirectory.FullName : Application.persistentDataPath;
    }

    private static bool TryPrepareWritableDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string probePath = Path.Combine(directory, ".write_test");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
#endif
}
