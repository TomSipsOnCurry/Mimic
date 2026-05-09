using UnityEngine;
using TMPro;

public class PlayerMovement : MonoBehaviour
{
    public Rigidbody2D body;
    public float speed = 6f;
    public AudioSource ttsAudioSource;
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private GameObject chatUiRoot;
    [SerializeField] private Transform headTransform;
    [SerializeField] private float talkingHeadMinY = 0.16f;
    [SerializeField] private float talkingHeadMaxY = 0.19f;
    [SerializeField] private float talkingHeadResponse = 20f;
    [SerializeField] private float audioToHeadScale = 18f;
    [SerializeField] private float talkingTiltMaxDegrees = 0.8f;
    [SerializeField] private Vector2 talkingTiltRetargetInterval = new Vector2(0.06f, 0.14f);
    [SerializeField] private float talkingTiltResponse = 16f;

    private Vector2 moveInput;
    private bool isChatOpen;
    private readonly float[] audioSamples = new float[64];
    private Vector3 baseHeadLocalEuler;
    private float currentTiltDegrees;
    private float targetTiltDegrees;
    private float tiltRetargetTimer;

    private void Awake()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
        }

        if (ttsAudioSource == null)
        {
            ttsAudioSource = GetComponent<AudioSource>();
        }

        if (chatInputField == null)
        {
            chatInputField = GetComponentInChildren<TMP_InputField>(true);
        }

        if (chatUiRoot == null && chatInputField != null)
        {
            chatUiRoot = chatInputField.gameObject;
        }

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

        SetChatUiVisible(false);
    }

    private void Update()
    {
        if (!enabled)
        {
            return;
        }

        HandleTypingInput();
        moveInput = isChatOpen ? Vector2.zero : GetMoveInput().normalized;
    }

    private void FixedUpdate()
    {
        if (!enabled)
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
        if (Input.GetKeyDown(KeyCode.T))
        {
            ToggleChat();
            return;
        }

        if (!isChatOpen)
        {
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

        AudioClip clip = UnitySAMWrapper.GenerateClipFromText(textToSpeak);
        if (clip == null)
        {
            Debug.LogError("PlayerMovement: UnitySAMWrapper failed to generate audio clip.");
            return;
        }

        if (ttsAudioSource == null)
        {
            ttsAudioSource = GetComponent<AudioSource>();
        }

        if (ttsAudioSource == null)
        {
            ttsAudioSource = gameObject.AddComponent<AudioSource>();
        }

        ttsAudioSource.PlayOneShot(clip);
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
    }
}
