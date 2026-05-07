using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class ChatManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;

    [Header("TTS")]
    [SerializeField] private AudioSource ttsAudioSource;

    [Header("Remote subtitles")]
    [SerializeField] private float remoteSubtitleLifetimeSeconds = 4f;

    private bool chatOpen;
    private PlayerMovement playerMovement;
    private MouseLook mouseLook;
    private PlayerAvatar avatar;

    private MultiplayerBootstrap multiplayer;

    private void Awake()
    {
        avatar = GetComponent<PlayerAvatar>();
        playerMovement = GetComponent<PlayerMovement>();
        mouseLook = GetComponentInChildren<MouseLook>(true);

        if (inputField == null)
        {
            Debug.LogError("ChatManager: inputField NOT assigned in Inspector!");
        }
        else
        {
            inputField.gameObject.SetActive(false);
        }

        Debug.Log("ChatManager: SAM TTS ready.");
    }

    private void Start()
    {
        // This component exists on the Player template and is cloned.
        // Don't disable it here (disabled state would get cloned). Just no-op on non-local avatars.
        if (avatar != null && !avatar.IsLocalPlayer)
        {
            return;
        }

        RemoteSubtitleStack.Ensure();

        multiplayer = MultiplayerBootstrap.Instance != null
            ? MultiplayerBootstrap.Instance
            : FindBootstrap();

        if (multiplayer != null)
        {
            multiplayer.ChatReceived += OnChatReceived;
        }
    }

    private static MultiplayerBootstrap FindBootstrap()
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<MultiplayerBootstrap>();
#else
        return Object.FindObjectOfType<MultiplayerBootstrap>();
#endif
    }

    private void OnDestroy()
    {
        if (multiplayer != null)
        {
            multiplayer.ChatReceived -= OnChatReceived;
        }
    }

    private void Update()
    {
        if (!ShouldProcessLocalInput())
        {
            return;
        }

        if (TogglePressed())
        {
            ToggleChat();
        }

        if (chatOpen && SubmitPressed())
        {
            if (inputField != null && !string.IsNullOrWhiteSpace(inputField.text))
            {
                string message = inputField.text.Trim();
                SendChat(message);
                inputField.text = "";
                inputField.ActivateInputField();
                inputField.Select();
            }
        }

        if (chatOpen && CancelPressed())
        {
            ToggleChat();
        }
    }

    private bool ShouldProcessLocalInput()
    {
        // If this ChatManager lives on a PlayerAvatar, only the local one should process input.
        if (avatar != null)
        {
            return avatar.IsLocalPlayer;
        }

        return true;
    }

    private void ToggleChat()
    {
        chatOpen = !chatOpen;

        if (inputField != null)
        {
            inputField.gameObject.SetActive(chatOpen);
        }

        SetPlayerControlEnabled(!chatOpen);

        if (chatOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (inputField != null)
            {
                inputField.ActivateInputField();
                inputField.Select();
            }
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void SetPlayerControlEnabled(bool enabled)
    {
        if (playerMovement != null)
        {
            playerMovement.enabled = enabled;
        }

        if (mouseLook != null)
        {
            mouseLook.enabled = enabled;
        }
    }

    private static bool TogglePressed()
    {
        bool newInput = Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
        bool oldInput = Input.GetKeyDown(KeyCode.T);
        return newInput || oldInput;
    }

    private static bool SubmitPressed()
    {
        bool newInput = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
        bool oldInput = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        return newInput || oldInput;
    }

    private static bool CancelPressed()
    {
        bool newInput = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool oldInput = Input.GetKeyDown(KeyCode.Escape);
        return newInput || oldInput;
    }

    private void SendChat(string message)
    {
        // Local playback (immediate)
        Speak(message);

        // Network: send only the text; each client synthesizes audio locally.
        if (multiplayer != null && multiplayer.IsConnected)
        {
            multiplayer.SendChat(message);
        }
    }

    private void OnChatReceived(int senderId, string message)
    {
        if (!ShouldProcessLocalInput()) return;

        if (multiplayer != null && senderId == multiplayer.LocalPlayerId)
        {
            // We already played locally; host is configured to not echo, but keep this guard anyway.
            return;
        }

        RemoteSubtitleStack.Instance?.Push(message, remoteSubtitleLifetimeSeconds);
        Speak(message);
    }

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var clip = UnitySAMWrapper.GenerateClipFromText(text);
        if (clip == null)
        {
            Debug.LogError("ChatManager: UnitySAMWrapper failed to generate audio clip.");
            return;
        }

        if (ttsAudioSource == null)
        {
            ttsAudioSource = gameObject.AddComponent<AudioSource>();
        }

        ttsAudioSource.PlayOneShot(clip);
    }
}
