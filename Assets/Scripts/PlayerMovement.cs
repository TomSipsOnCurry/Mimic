using UnityEngine;
using TMPro;
using Photon.Pun;


public class PlayerMovement : MonoBehaviour, IPunObservable
{
    private const int PlayerVariantCount = 4;
    private const float PlayerSpriteStartX = 1f;
    private const float PlayerSpriteStrideX = 24f;
    private const float PlayerHeadY = 90f;
    private const float PlayerHeadWidth = 23f;
    private const float PlayerHeadHeight = 9f;
    private const float PlayerBodyY = 64f;
    private const float PlayerBodyWidth = 23f;
    private const float PlayerBodyHeight = 25f;

    public Rigidbody2D body;
    public float speed = 6f;
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private GameObject chatUiRoot;
    [SerializeField] private Transform headTransform;
    [SerializeField] private SpriteRenderer bodyRenderer;
    [SerializeField] private SpriteRenderer headRenderer;
    [SerializeField] private float talkingHeadMinY = 0.16f;
    [SerializeField] private float talkingHeadMaxY = 0.19f;
    [SerializeField] private float talkingHeadResponse = 20f;
    [SerializeField] private float audioToHeadScale = 18f;
    [SerializeField] private float talkingTiltMaxDegrees = 0.8f;
    [SerializeField] private Vector2 talkingTiltRetargetInterval = new Vector2(0.06f, 0.14f);
    [SerializeField] private float talkingTiltResponse = 16f;
    [SerializeField] private Transform mouthTransform;
    [SerializeField] private Vector3 generatedMouthLocalPosition = new Vector3(0f, -0.045f, -0.01f);
    [SerializeField] private Vector3 generatedMouthLocalScale = new Vector3(0.055f, 0.006f, 1f);
    [SerializeField] private float mouthOpenScale = 5f;
    [SerializeField] private float mouthResponse = 24f;
    [SerializeField] private float speechTextWorldYOffset = 1.5f;
    [SerializeField] private Vector2 speechTextSize = new Vector2(280f, 82f);
    [SerializeField] private float speechTextFontSize = 28f;
    [SerializeField] private TMP_FontAsset speechTextFont;
    [SerializeField] private string speechTextFontResourcePath = "Fonts & Materials/BoldPixels SDF";

    private Vector2 moveInput;
    private bool isChatOpen;
    private readonly float[] audioSamples = new float[64];
    private Vector3 baseHeadLocalEuler;
    private Vector3 baseMouthLocalScale;
    private float currentTiltDegrees;
    private float targetTiltDegrees;
    private float tiltRetargetTimer;
    private PhotonMultiplayer netManager;
    private Vector3 networkTargetPosition;
    private PhotonView photonView;
    private Camera mainCamera;
    private Canvas speechCanvas;
    private RectTransform speechTextRect;
    private TextMeshProUGUI speechText;
    private int assignedPlayerId = -1;
    private int appliedPlayerVariantId = -1;
    private static Sprite generatedMouthSprite;
    private static Texture2D cachedPlayerTexture;
    private static Sprite[] cachedBodySprites;
    private static Sprite[] cachedHeadSprites;
    private static PlayerMovement localPhotonPlayer;

    [Header("TTS Audio")]
    [SerializeField] private AudioSource ttsAudioSource;

    [Header("Movement Audio")]
    [SerializeField] private AudioSource movementAudioSource;
    [SerializeField] private AudioClip trolleyMoveClip;
    [SerializeField] private float movementSoundMinInput = 0.1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        localPhotonPlayer = null;
    }

    private void Awake()
{
    photonView = GetComponent<PhotonView>();

    if (DestroyIfDuplicateLocalPhotonPlayer())
    {
        return;
    }

    networkTargetPosition = transform.position;

    if (body == null)
    {
        body = GetComponent<Rigidbody2D>();
    }

    // -----------------------------
    // TTS AUDIO SOURCE
    // This is ONLY for generated speech.
    // The lid/head/mouth animation should read from this source only.
    // -----------------------------
    if (ttsAudioSource == null)
    {
        Transform existingTts = transform.Find("TTS Audio Source");

        if (existingTts != null)
        {
            ttsAudioSource = existingTts.GetComponent<AudioSource>();
        }

        if (ttsAudioSource == null)
        {
            GameObject ttsObject = new GameObject("TTS Audio Source");
            ttsObject.transform.SetParent(transform, false);
            ttsAudioSource = ttsObject.AddComponent<AudioSource>();
        }
    }

    ttsAudioSource.playOnAwake = false;
    ttsAudioSource.loop = false;
    ttsAudioSource.spatialBlend = 0f;

    // -----------------------------
    // MOVEMENT AUDIO SOURCE
    // This is ONLY for trolley/movement loop.
    // Do NOT use this for lid/head animation.
    // -----------------------------
    if (movementAudioSource == null)
    {
        Transform existingMovement = transform.Find("Movement Audio Source");

        if (existingMovement != null)
        {
            movementAudioSource = existingMovement.GetComponent<AudioSource>();
        }

        if (movementAudioSource == null)
        {
            GameObject movementObject = new GameObject("Movement Audio Source");
            movementObject.transform.SetParent(transform, false);
            movementAudioSource = movementObject.AddComponent<AudioSource>();
        }
    }

    movementAudioSource.playOnAwake = false;
    movementAudioSource.loop = true;
    movementAudioSource.spatialBlend = 0f;

    if (trolleyMoveClip != null)
    {
        movementAudioSource.clip = trolleyMoveClip;
    }

    ResolveChatUiReferences();

    if (headTransform == null)
    {
        Transform foundHead = transform.Find("Head");

        if (foundHead != null)
        {
            headTransform = foundHead;
        }
    }

    if (headTransform != null)
    {
        baseHeadLocalEuler = headTransform.localEulerAngles;
    }

    ResolvePlayerVariantRenderers();
    ResolveMouthTransform();
    RefreshPlayerVariantFromIdentity();

    if (IsLocalPlayer())
    {
        SetChatUiVisible(false);
    }
}

    private void Start()
    {
        if (DestroyIfDuplicateLocalPhotonPlayer())
        {
            return;
        }

        // Set player nickname for Photon
        if (string.IsNullOrEmpty(PhotonNetwork.NickName))
        {
            PhotonNetwork.NickName = "Player_" + Random.Range(1000, 9999);
        }

        RefreshPlayerVariantFromIdentity();
    }

    private void Update()
    {
        if (!enabled)
        {
            return;
        }

        RefreshPlayerVariantFromIdentity();

        // Remote player: interpolate to network position
        if (!IsLocalPlayer())
        {
            if (Vector3.Distance(transform.position, networkTargetPosition) > 0.001f)
            {
                transform.position = Vector3.Lerp(transform.position, networkTargetPosition, Time.deltaTime * 8f);
            }
            return;
        }

        // Local player: handle input
        HandleTypingInput();
        moveInput = isChatOpen ? Vector2.zero : GetMoveInput().normalized;
        UpdateMovementAudio();
    }

    private void UpdateMovementAudio()
    {
        if (!IsLocalPlayer())
        {
            return;
        }

        if (movementAudioSource == null || trolleyMoveClip == null)
        {
            return;
        }

        bool isMoving = moveInput.magnitude > movementSoundMinInput;

        if (isMoving)
        {
            if (!movementAudioSource.isPlaying)
            {
                movementAudioSource.Play();
            }
        }
        else
        {
            if (movementAudioSource.isPlaying)
            {
                movementAudioSource.Stop();
            }
        }
    }

    private void FixedUpdate()
    {
        if (!enabled || !IsLocalPlayer())
        {
            return;
        }

        Vector2 displacement = moveInput * speed * Time.fixedDeltaTime;

        if (body != null)
        {
            body.MovePosition(body.position + displacement);
            return;
        }

        transform.position += new Vector3(displacement.x, displacement.y, 0f);
    }

    private void LateUpdate()
    {
        UpdateHeadFromAudio();
        UpdateSpeechTextPosition();
    }

    private bool IsLocalPlayer()
    {
        return photonView == null || photonView.IsMine;
    }

    public void SetPlayerVariant(int playerId)
    {
        assignedPlayerId = Mathf.Max(1, playerId);
        ApplyPlayerVariant(assignedPlayerId);
    }

    public void SetPlayerVariantForActor(int actorNumber)
    {
        ApplyPlayerVariant(GetPlayerNumberForActor(actorNumber));
    }

    private void RefreshPlayerVariantFromIdentity()
    {
        int playerId = PhotonNetwork.InRoom && photonView != null
            ? GetPhotonPlayerId()
            : assignedPlayerId > 0 ? assignedPlayerId : GetPhotonPlayerId();
        ApplyPlayerVariant(playerId);
    }

    private int GetPhotonPlayerId()
    {
        if (photonView != null && photonView.OwnerActorNr > 0)
        {
            return GetPlayerNumberForActor(photonView.OwnerActorNr);
        }

        if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber > 0 && IsLocalPlayer())
        {
            return GetPlayerNumberForActor(PhotonNetwork.LocalPlayer.ActorNumber);
        }

        return 1;
    }

    private static int GetPlayerNumberForActor(int actorNumber)
    {
        if (!PhotonNetwork.InRoom || actorNumber <= 0)
        {
            return Mathf.Max(1, actorNumber);
        }

        int playerNumber = 1;
        for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
        {
            if (PhotonNetwork.PlayerList[i].ActorNumber < actorNumber)
            {
                playerNumber++;
            }
        }

        return playerNumber;
    }

    private bool DestroyIfDuplicateLocalPhotonPlayer()
    {
        if (photonView == null || !PhotonNetwork.InRoom || !photonView.IsMine || photonView.ViewID == 0)
        {
            return false;
        }

        if (localPhotonPlayer == null || localPhotonPlayer == this)
        {
            localPhotonPlayer = this;
            return false;
        }

        enabled = false;
        PhotonNetwork.Destroy(gameObject);
        return true;
    }

    private void ApplyPlayerVariant(int playerId)
    {
        playerId = Mathf.Max(1, playerId);
        if (appliedPlayerVariantId == playerId)
        {
            return;
        }

        ResolvePlayerVariantRenderers();
        if (!EnsurePlayerVariantSprites())
        {
            return;
        }

        int variantIndex = (playerId - 1) % PlayerVariantCount;
        if (bodyRenderer != null)
        {
            bodyRenderer.sprite = cachedBodySprites[variantIndex];
            bodyRenderer.color = Color.white;
        }

        if (headRenderer != null)
        {
            headRenderer.sprite = cachedHeadSprites[variantIndex];
            headRenderer.color = Color.white;
        }

        appliedPlayerVariantId = playerId;
    }

    private void ResolvePlayerVariantRenderers()
    {
        if (bodyRenderer == null)
        {
            Transform bodyTransform = transform.Find("Body");
            if (bodyTransform != null)
            {
                bodyRenderer = bodyTransform.GetComponent<SpriteRenderer>();
            }
        }

        if (headRenderer == null)
        {
            if (headTransform == null)
            {
                Transform foundHead = transform.Find("Head");
                if (foundHead != null)
                {
                    headTransform = foundHead;
                }
            }

            if (headTransform != null)
            {
                headRenderer = headTransform.GetComponent<SpriteRenderer>();
            }
        }

        if (bodyRenderer != null && headRenderer != null)
        {
            DisableUnusedRootRenderer();
            return;
        }

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null)
            {
                continue;
            }

            int spriteHeight = Mathf.RoundToInt(renderer.sprite.rect.height);
            if (bodyRenderer == null && spriteHeight == Mathf.RoundToInt(PlayerBodyHeight))
            {
                bodyRenderer = renderer;
            }
            else if (headRenderer == null && spriteHeight == Mathf.RoundToInt(PlayerHeadHeight))
            {
                headRenderer = renderer;
            }
        }

        DisableUnusedRootRenderer();
    }

    private void DisableUnusedRootRenderer()
    {
        if (bodyRenderer == null || headRenderer == null)
        {
            return;
        }

        SpriteRenderer rootRenderer = GetComponent<SpriteRenderer>();
        if (rootRenderer != null && rootRenderer != bodyRenderer && rootRenderer != headRenderer)
        {
            rootRenderer.enabled = false;
        }
    }

    private bool EnsurePlayerVariantSprites()
    {
        Texture2D texture = GetPlayerVariantTexture();
        if (texture == null)
        {
            return false;
        }

        if (cachedPlayerTexture == texture && cachedBodySprites != null && cachedHeadSprites != null)
        {
            return true;
        }

        cachedPlayerTexture = texture;
        float bodyPixelsPerUnit = bodyRenderer != null && bodyRenderer.sprite != null ? bodyRenderer.sprite.pixelsPerUnit : 100f;
        float headPixelsPerUnit = headRenderer != null && headRenderer.sprite != null ? headRenderer.sprite.pixelsPerUnit : 100f;
        cachedBodySprites = CreatePlayerVariantSprites(texture, PlayerBodyY, PlayerBodyWidth, PlayerBodyHeight, bodyPixelsPerUnit, "PlayerBody");
        cachedHeadSprites = CreatePlayerVariantSprites(texture, PlayerHeadY, PlayerHeadWidth, PlayerHeadHeight, headPixelsPerUnit, "PlayerHead");
        return true;
    }

    private Texture2D GetPlayerVariantTexture()
    {
        if (bodyRenderer != null && bodyRenderer.sprite != null)
        {
            return bodyRenderer.sprite.texture;
        }

        if (headRenderer != null && headRenderer.sprite != null)
        {
            return headRenderer.sprite.texture;
        }

        return null;
    }

    private static Sprite[] CreatePlayerVariantSprites(Texture2D texture, float y, float width, float height, float pixelsPerUnit, string spriteNamePrefix)
    {
        Sprite[] sprites = new Sprite[PlayerVariantCount];
        for (int variantIndex = 0; variantIndex < PlayerVariantCount; variantIndex++)
        {
            Rect rect = new Rect(PlayerSpriteStartX + PlayerSpriteStrideX * variantIndex, y, width, height);
            Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit);
            sprite.name = $"{spriteNamePrefix}_{variantIndex + 1}";
            sprites[variantIndex] = sprite;
        }

        return sprites;
    }

    private void ResolveChatUiReferences()
    {
        if (chatInputField == null)
        {
            chatInputField = GetComponentInChildren<TMP_InputField>(true);
        }

        if (chatInputField == null)
        {
            chatInputField = FindSceneChatInputField();
        }

        if (chatUiRoot == null && chatInputField != null)
        {
            chatUiRoot = chatInputField.gameObject;
        }
    }

    private static TMP_InputField FindSceneChatInputField()
    {
#if UNITY_2023_1_OR_NEWER
        TMP_InputField[] inputFields = FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        TMP_InputField[] inputFields = FindObjectsOfType<TMP_InputField>(true);
#endif
        TMP_InputField fallback = null;

        for (int i = 0; i < inputFields.Length; i++)
        {
            TMP_InputField inputField = inputFields[i];
            if (inputField == null || !inputField.gameObject.scene.IsValid())
            {
                continue;
            }

            if (fallback == null)
            {
                fallback = inputField;
            }

            if (inputField.gameObject.activeInHierarchy)
            {
                return inputField;
            }
        }

        return fallback;
    }

    private void ResolveMouthTransform()
    {
        if (mouthTransform == null)
        {
            Transform foundMouth = transform.Find("Mouth");
            if (foundMouth == null && headTransform != null)
            {
                foundMouth = headTransform.Find("Mouth");
            }

            mouthTransform = foundMouth;
        }

        if (mouthTransform == null && headTransform != null)
        {
            GameObject generatedMouth = new GameObject("Mouth");
            generatedMouth.transform.SetParent(headTransform, false);
            generatedMouth.transform.localPosition = generatedMouthLocalPosition;
            generatedMouth.transform.localScale = generatedMouthLocalScale;

            SpriteRenderer mouthRenderer = generatedMouth.AddComponent<SpriteRenderer>();
            mouthRenderer.sprite = GetGeneratedMouthSprite();
            mouthRenderer.color = Color.black;

            SpriteRenderer headRenderer = headTransform.GetComponent<SpriteRenderer>();
            if (headRenderer != null)
            {
                mouthRenderer.sortingLayerID = headRenderer.sortingLayerID;
                mouthRenderer.sortingOrder = headRenderer.sortingOrder + 1;
            }

            mouthTransform = generatedMouth.transform;
        }

        if (mouthTransform != null)
        {
            baseMouthLocalScale = mouthTransform.localScale;
        }
    }

    private static Sprite GetGeneratedMouthSprite()
    {
        if (generatedMouthSprite != null)
        {
            return generatedMouthSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Point;
        generatedMouthSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return generatedMouthSprite;
    }

    private Vector2 GetMoveInput()
    {
        float horizontal = 0f;
        float vertical = 0f;

        if (Input.GetKey(KeyCode.A))
        {
            horizontal -= 1f;
        }

        if (Input.GetKey(KeyCode.D))
        {
            horizontal += 1f;
        }

        if (Input.GetKey(KeyCode.S))
        {
            vertical -= 1f;
        }

        if (Input.GetKey(KeyCode.W))
        {
            vertical += 1f;
        }

        return new Vector2(horizontal, vertical);
    }

    private void HandleTypingInput()
    {
        if (!isChatOpen)
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                ToggleChat();
            }

            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseChat(clearText: true);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SubmitSpeechFromInputField();
            CloseChat(clearText: true);
            return;
        }
    }

    private void ToggleChat()
    {
        ResolveChatUiReferences();

        if (chatInputField == null)
        {
            Debug.LogWarning("PlayerMovement: No TMP_InputField found for chat.");
            return;
        }

        if (isChatOpen)
        {
            CloseChat(clearText: true);
            return;
        }

        isChatOpen = true;
        SetChatUiVisible(true);

        if (chatInputField != null)
        {
            chatInputField.text = string.Empty;
            chatInputField.Select();
            chatInputField.ActivateInputField();
        }
    }

    private void CloseChat(bool clearText)
    {
        isChatOpen = false;
        SetChatUiVisible(false);

        if (chatInputField != null)
        {
            if (clearText)
            {
                chatInputField.text = string.Empty;
            }

            chatInputField.DeactivateInputField();
        }
    }

    private void SetChatUiVisible(bool visible)
    {
        if (chatUiRoot != null)
        {
            chatUiRoot.SetActive(visible);
        }
    }

    private void SubmitSpeechFromInputField()
    {
        string textToSpeak = chatInputField != null ? chatInputField.text.Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(textToSpeak))
        {
            return;
        }

        // Send chat to network
        if (netManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            netManager = FindAnyObjectByType<PhotonMultiplayer>();
#else
            netManager = FindObjectOfType<PhotonMultiplayer>();
#endif
        }

        if (netManager != null)
        {
            netManager.SendChatMessage(textToSpeak);
        }

        PlaySpeech(textToSpeak);
    }

    private void PlaySpeech(string textToSpeak)
    {
        AudioClip clip = UnitySAMWrapper.GenerateClipFromText(textToSpeak);

        if (clip == null)
        {
            Debug.LogError("PlayerMovement: UnitySAMWrapper failed to generate audio clip.");
            DeleteSpeechText();
            return;
        }

        if (ttsAudioSource == null)
        {
            GameObject ttsObject = new GameObject("TTS Audio Source");
            ttsObject.transform.SetParent(transform, false);
            ttsAudioSource = ttsObject.AddComponent<AudioSource>();
        }

        ttsAudioSource.PlayOneShot(clip);
        ShowSpeechText(textToSpeak);
    }

    private void OnEnable()
    {
        if (netManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            netManager = FindAnyObjectByType<PhotonMultiplayer>();
#else
            netManager = FindObjectOfType<PhotonMultiplayer>();
#endif
        }

        PhotonMultiplayer.ChatReceivedWithActor -= HandleRemoteChat;
        PhotonMultiplayer.ChatReceivedWithActor += HandleRemoteChat;
    }

    private void OnDisable()
    {
        PhotonMultiplayer.ChatReceivedWithActor -= HandleRemoteChat;
        DeleteSpeechText();
    }

    private void HandleRemoteChat(int actorNumber, string sender, string message)
    {
        if (photonView == null || photonView.OwnerActorNr != actorNumber || photonView.IsMine)
        {
            return;
        }

        PlaySpeech(message);
    }

    private void ShowSpeechText(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        EnsureSpeechText();
        if (speechText == null)
        {
            return;
        }

        speechText.text = message.Trim();
        Debug.Log("Speech text created: " + speechText.text);
        Debug.Log("Speech object: " + speechText.gameObject.name);
        Debug.Log("Speech position: " + speechTextRect.position);
        speechText.gameObject.SetActive(true);
        UpdateSpeechTextPosition();
    }

    private void EnsureSpeechText()
    {
        if (speechText != null)
        {
            return;
        }

        if (speechCanvas == null)
        {
            speechCanvas = FindSpeechCanvas();
        }

        if (speechCanvas == null)
        {
            return;
        }

        GameObject textObject = new GameObject($"{name} Speech Text", typeof(RectTransform));
        textObject.transform.SetParent(speechCanvas.transform, false);
        textObject.transform.SetAsLastSibling();

        speechTextRect = textObject.GetComponent<RectTransform>();
        speechTextRect.sizeDelta = speechTextSize;

        speechText = textObject.AddComponent<TextMeshProUGUI>();
        if (speechTextFont == null)
        {
            speechTextFont = Resources.Load<TMP_FontAsset>(speechTextFontResourcePath);
        }

        if (speechTextFont != null)
        {
            speechText.font = speechTextFont;
        }

        speechText.alignment = TextAlignmentOptions.Center;
        speechText.color = Color.white;
        speechText.fontSize = speechTextFontSize;
        speechText.fontStyle = FontStyles.Bold;
        speechText.enableWordWrapping = true;
        speechText.overflowMode = TextOverflowModes.Ellipsis;
        speechText.raycastTarget = false;
        speechText.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        speechText.outlineWidth = 0.18f;
        speechText.gameObject.SetActive(false);
    }

    private Canvas FindSpeechCanvas()
    {
        Canvas canvas = chatInputField != null ? chatInputField.GetComponentInParent<Canvas>() : null;
        if (canvas != null)
        {
            return canvas;
        }

#if UNITY_2023_1_OR_NEWER
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
#endif

        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].gameObject.scene.IsValid())
            {
                return canvases[i];
            }
        }

        GameObject canvasObject = new GameObject("Speech Canvas");
        Canvas createdCanvas = canvasObject.AddComponent<Canvas>();
        createdCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        return createdCanvas;
    }

    private void UpdateSpeechTextPosition()
    {
        if (speechText == null || !speechText.gameObject.activeSelf)
        {
            return;
        }

        if (ttsAudioSource == null || !ttsAudioSource.isPlaying)
        {
            DeleteSpeechText();
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null || speechCanvas == null || speechTextRect == null)
        {
            return;
        }

        Vector2 screenPosition = GetSpeechScreenPosition(mainCamera);
        speechTextRect.pivot = new Vector2(0.5f, 0.5f);

        if (speechCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            speechTextRect.position = screenPosition;
            return;
        }

        RectTransform canvasRect = speechCanvas.transform as RectTransform;
        if (canvasRect == null)
        {
            return;
        }

        Camera canvasCamera = speechCanvas.renderMode == RenderMode.ScreenSpaceCamera ? speechCanvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, canvasCamera, out Vector2 localPoint))
        {
            speechTextRect.anchoredPosition = localPoint;
        }
    }

    private Vector2 GetSpeechScreenPosition(Camera cameraForPosition)
    {
        if (cameraForPosition == null)
        {
            cameraForPosition = Camera.main;
        }

        if (cameraForPosition == null)
        {
            return Vector2.zero;
        }

        Vector3 worldPosition = transform.position + Vector3.up * speechTextWorldYOffset;
        Vector3 screenPosition = cameraForPosition.WorldToScreenPoint(worldPosition);

        return new Vector2(screenPosition.x, screenPosition.y);
    }

    private Vector3 GetSpeechWorldPosition()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        bool hasBounds = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            return transform.position + Vector3.up * speechTextWorldYOffset;
        }

        return new Vector3(bounds.center.x, bounds.max.y + speechTextWorldYOffset, transform.position.z);
    }

    private void DeleteSpeechText()
    {
        if (speechText == null)
        {
            return;
        }

        GameObject speechTextObject = speechText.gameObject;
        speechText = null;
        speechTextRect = null;
        Destroy(speechTextObject);
    }

    private void UpdateHeadFromAudio()
    {
        if (headTransform == null)
        {
            return;
        }

        float targetY = talkingHeadMinY;
        float normalizedLevel = 0f;
        bool isSpeaking = ttsAudioSource != null && ttsAudioSource.isPlaying;

        if (isSpeaking)
        {
            ttsAudioSource.GetOutputData(audioSamples, 0);

            float sumSquares = 0f;
            for (int sampleIndex = 0; sampleIndex < audioSamples.Length; sampleIndex++)
            {
                float sample = audioSamples[sampleIndex];
                sumSquares += sample * sample;
            }

            float rms = Mathf.Sqrt(sumSquares / audioSamples.Length);
            normalizedLevel = Mathf.Clamp01(rms * audioToHeadScale);
            targetY = Mathf.Lerp(talkingHeadMinY, talkingHeadMaxY, normalizedLevel);
        }

        Vector3 nextLocalPosition = headTransform.localPosition;
        nextLocalPosition.y = Mathf.Lerp(nextLocalPosition.y, targetY, talkingHeadResponse * Time.deltaTime);
        headTransform.localPosition = nextLocalPosition;

        if (isSpeaking)
        {
            tiltRetargetTimer -= Time.deltaTime;
            if (tiltRetargetTimer <= 0f)
            {
                tiltRetargetTimer = Random.Range(talkingTiltRetargetInterval.x, talkingTiltRetargetInterval.y);
                float levelScaledTilt = Mathf.Max(0.2f, normalizedLevel) * talkingTiltMaxDegrees;
                targetTiltDegrees = Random.Range(-levelScaledTilt, levelScaledTilt);
            }
        }
        else
        {
            targetTiltDegrees = 0f;
            tiltRetargetTimer = 0f;
        }

        currentTiltDegrees = Mathf.Lerp(currentTiltDegrees, targetTiltDegrees, talkingTiltResponse * Time.deltaTime);
        Vector3 targetEuler = baseHeadLocalEuler;
        targetEuler.z += currentTiltDegrees;
        headTransform.localRotation = Quaternion.Euler(targetEuler);

        UpdateMouthFromAudio(isSpeaking, normalizedLevel);
    }

    private void UpdateMouthFromAudio(bool isSpeaking, float normalizedLevel)
    {
        if (mouthTransform == null)
        {
            return;
        }

        Vector3 targetScale = baseMouthLocalScale;
        if (isSpeaking)
        {
            targetScale.y = baseMouthLocalScale.y * Mathf.Lerp(1f, mouthOpenScale, normalizedLevel);
        }

        mouthTransform.localScale = Vector3.Lerp(mouthTransform.localScale, targetScale, mouthResponse * Time.deltaTime);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Send position to other players
            stream.SendNext(transform.position);
        }
        else
        {
            // Receive position from other players
            networkTargetPosition = (Vector3)stream.ReceiveNext();
        }
    }

    private void OnDestroy()
    {
        PhotonMultiplayer.ChatReceivedWithActor -= HandleRemoteChat;
        if (localPhotonPlayer == this)
        {
            localPhotonPlayer = null;
        }

        if (speechText != null)
        {
            DeleteSpeechText();
        }
    }
}
