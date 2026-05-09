using System;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuMultiplayerLobby : MonoBehaviour
{
    [SerializeField] private GameObject playMenu;

    private PhotonMultiplayer net;
    private TMP_InputField codeInput;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI roomCodeText;
    private TextMeshProUGUI playersText;
    private TextMeshProUGUI startHintText;
    private Button hostButton;
    private Button joinButton;
    private Button startButton;
    private bool built;
    private TMP_FontAsset font;

    private static readonly Color MenuTextColor = new Color(0.03529412f, 0.17254902f, 0.48235294f, 1f);
    private static readonly Color SecondaryTextColor = new Color(0.23137255f, 0.30588236f, 0.5137255f, 1f);
    private static readonly Color InputBackgroundColor = new Color(1f, 1f, 1f, 0.65f);

    public void Configure(GameObject playMenuObject)
    {
        playMenu = playMenuObject;
        EnsureBuilt();
    }

    private void Awake()
    {
        ResolveNetwork();
    }

    private void OnEnable()
    {
        PhotonMultiplayer.RoomStateChanged -= Refresh;
        PhotonMultiplayer.RoomStateChanged += Refresh;
    }

    private void OnDisable()
    {
        PhotonMultiplayer.RoomStateChanged -= Refresh;
    }

    private void Update()
    {
        if (playMenu != null && playMenu.activeInHierarchy)
        {
            Refresh();
        }
    }

    public void Open()
    {
        ResolveNetwork();
        EnsureBuilt();

        if (playMenu != null)
        {
            playMenu.SetActive(true);
        }

        Refresh();
    }

    public void CloseAndLeave()
    {
        if (net != null && net.IsConnected)
        {
            net.StopSession();
        }

        if (playMenu != null)
        {
            playMenu.SetActive(false);
        }
    }

    private void Host()
    {
        ResolveNetwork();
        if (net == null)
        {
            return;
        }

        net.StartHost();
        if (codeInput != null)
        {
            codeInput.text = net.SessionCode;
        }

        Refresh();
    }

    private void Join()
    {
        ResolveNetwork();
        if (net == null)
        {
            return;
        }

        string enteredCode = codeInput != null ? codeInput.text : string.Empty;
        if (PhotonMultiplayer.IsSoloCode(enteredCode))
        {
            if (codeInput != null)
            {
                codeInput.text = "SOLO";
            }

            net.StartSolo();
        }
        else
        {
            net.SetSessionCode(enteredCode);
            net.StartClient();
        }

        Refresh();
    }

    private void StartGame()
    {
        if (net != null)
        {
            net.StartGame();
        }
    }

    private void Refresh()
    {
        ResolveNetwork();
        bool inRoom = net != null && net.IsConnected;
        bool readyForRoomAction = !inRoom && PhotonNetwork.IsConnectedAndReady;

        if (statusText != null)
        {
            statusText.text = net != null ? net.Status : "Connecting...";
        }

        if (roomCodeText != null)
        {
            string code = net != null ? net.SessionCode : string.Empty;
            roomCodeText.text = string.IsNullOrWhiteSpace(code) ? "Room code: ----" : $"Room code: {code}";
        }

        if (playersText != null)
        {
            playersText.text = BuildPlayersText();
        }

        if (codeInput != null)
        {
            codeInput.interactable = !inRoom;
        }

        if (hostButton != null)
        {
            hostButton.gameObject.SetActive(!inRoom);
            hostButton.interactable = readyForRoomAction;
        }

        if (joinButton != null)
        {
            joinButton.gameObject.SetActive(!inRoom);
            joinButton.interactable = readyForRoomAction && codeInput != null && !string.IsNullOrWhiteSpace(codeInput.text);
        }

        if (startButton != null)
        {
            startButton.gameObject.SetActive(inRoom);
            startButton.interactable = net != null && net.CanStartGame;
        }

        if (startHintText != null)
        {
            startHintText.text = BuildStartHint(inRoom);
        }
    }

    private string BuildPlayersText()
    {
        if (net == null || !net.IsConnected)
        {
            return "Players:\n-";
        }

        Player[] players = net.Players;
        Array.Sort(players, (left, right) => left.ActorNumber.CompareTo(right.ActorNumber));

        StringBuilder builder = new StringBuilder("Players:\n");
        for (int i = 0; i < players.Length; i++)
        {
            Player player = players[i];
            string name = string.IsNullOrWhiteSpace(player.NickName) ? $"Player {i + 1}" : player.NickName;
            string host = player.IsMasterClient ? " (host)" : string.Empty;
            builder.Append(i + 1).Append(". ").Append(name).Append(host).Append('\n');
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildStartHint(bool inRoom)
    {
        if (!inRoom || net == null)
        {
            return "Enter a code to join, or host to make a new room.";
        }

        int playerCount = net.Players.Length;
        if (!PhotonNetwork.IsMasterClient)
        {
            return "Waiting for the host to start.";
        }

        if (playerCount < net.MinPlayersToStart)
        {
            return $"Need {net.MinPlayersToStart} players to start ({playerCount}/{net.MinPlayersToStart}).";
        }

        return "Ready to start.";
    }

    private void ResolveNetwork()
    {
        if (net != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        net = FindAnyObjectByType<PhotonMultiplayer>();
#else
        net = FindObjectOfType<PhotonMultiplayer>();
#endif
        if (net != null)
        {
            return;
        }

        GameObject netObject = new GameObject(nameof(PhotonMultiplayer));
        DontDestroyOnLoad(netObject);
        net = netObject.AddComponent<PhotonMultiplayer>();
    }

    private void EnsureBuilt()
    {
        if (built)
        {
            return;
        }

        if (playMenu == null)
        {
            GameObject found = GameObject.Find("PlayMenu");
            playMenu = found;
        }

        if (playMenu == null)
        {
            return;
        }

        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/BoldPixels SDF");
        HideExistingPlayMenuChildren();
        BuildLobbyHierarchy(playMenu.transform);
        built = true;
    }

    private void HideExistingPlayMenuChildren()
    {
        for (int i = 0; i < playMenu.transform.childCount; i++)
        {
            Transform child = playMenu.transform.GetChild(i);
            if (child.name == "PlayPanel")
            {
                child.gameObject.SetActive(true);
                child.SetAsFirstSibling();
            }
            else if (child.name != "MultiplayerLobby")
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private void BuildLobbyHierarchy(Transform parent)
    {
        GameObject root = new GameObject("MultiplayerLobby", typeof(RectTransform), typeof(VerticalLayoutGroup));
        root.layer = playMenu.layer;
        root.transform.SetParent(parent, false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = new Vector2(640f, 600f);

        VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(54, 54, 30, 30);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateText(root.transform, "Multiplayer", 72f, 78f, MenuTextColor);
        statusText = CreateText(root.transform, "Connecting...", 30f, 38f, SecondaryTextColor);
        roomCodeText = CreateText(root.transform, "Room code: ----", 38f, 44f, MenuTextColor);
        codeInput = CreateInput(root.transform);

        Transform row = CreateRow(root.transform, 86f);
        hostButton = CreateButton(row, "Host", Host);
        joinButton = CreateButton(row, "Join", Join);

        playersText = CreateText(root.transform, "Players:\n-", 30f, 154f, MenuTextColor);
        startHintText = CreateText(root.transform, "Enter a code to join, or host to make a new room.", 24f, 34f, SecondaryTextColor);
        startButton = CreateButton(root.transform, "Start Game", StartGame);
        CreateButton(root.transform, "Back", CloseAndLeave);
    }

    private Transform CreateRow(Transform parent, float height)
    {
        GameObject row = new GameObject("HostJoinRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.layer = playMenu.layer;
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = height;

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 28f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        return row.transform;
    }

    private TextMeshProUGUI CreateText(Transform parent, string text, float size, float height, Color color)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.layer = playMenu.layer;
        textObject.transform.SetParent(parent, false);

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.font = font;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.enableWordWrapping = true;

        LayoutElement layout = textObject.GetComponent<LayoutElement>();
        layout.preferredHeight = height;
        return label;
    }

    private TMP_InputField CreateInput(Transform parent)
    {
        GameObject inputObject = new GameObject("RoomCodeInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
        inputObject.layer = playMenu.layer;
        inputObject.transform.SetParent(parent, false);

        Image background = inputObject.GetComponent<Image>();
        background.color = InputBackgroundColor;

        LayoutElement layout = inputObject.GetComponent<LayoutElement>();
        layout.preferredHeight = 78f;
        layout.preferredWidth = 360f;

        RectTransform rect = inputObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(360f, 78f);

        GameObject viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        viewport.layer = playMenu.layer;
        viewport.transform.SetParent(inputObject.transform, false);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(18f, 8f);
        viewportRect.offsetMax = new Vector2(-18f, -8f);

        TextMeshProUGUI placeholder = CreateInputText(viewport.transform, "CODE");
        placeholder.color = new Color(MenuTextColor.r, MenuTextColor.g, MenuTextColor.b, 0.55f);

        TextMeshProUGUI text = CreateInputText(viewport.transform, string.Empty);
        text.color = MenuTextColor;

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = viewportRect;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.characterLimit = 4;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterValidation = TMP_InputField.CharacterValidation.None;
        input.text = string.Empty;
        return input;
    }

    private TextMeshProUGUI CreateInputText(Transform parent, string text)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.layer = playMenu.layer;
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.font = font;
        label.fontSize = 44f;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        return label;
    }

    private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.layer = playMenu.layer;
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
        layout.preferredHeight = 72f;
        layout.minWidth = 220f;

        SmoothButton smoothButton = buttonObject.AddComponent<SmoothButton>();
        smoothButton.hoverScale = new Vector3(1.04f, 1.04f, 1.04f);
        smoothButton.pressedScale = new Vector3(0.96f, 0.96f, 0.96f);

        TextMeshProUGUI text = CreateText(buttonObject.transform, label, 44f, 72f, MenuTextColor);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }
}
