using UnityEngine;

public class PlayerAvatar : MonoBehaviour
{
    private const string LocalPlayerLayerName = "Player";
    private const string RemotePlayerLayerName = "Default";

    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private MouseLook mouseLook;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;
    [SerializeField] private float remotePositionLerpSpeed = 18f;
    [SerializeField] private float remoteRotationLerpSpeed = 18f;
    [SerializeField] private float remoteSnapDistance = 5f;

    public int PlayerId { get; private set; }

    public bool IsLocalPlayer { get; private set; }

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool hasRemoteTarget;

    private void Awake()
    {
        CacheReferences();
        ResetRemoteTarget();
    }

    private void Update()
    {
        if (IsLocalPlayer || !hasRemoteTarget)
        {
            return;
        }

        float snapDistanceSquared = remoteSnapDistance * remoteSnapDistance;
        if ((targetPosition - transform.position).sqrMagnitude > snapDistanceSquared)
        {
            transform.SetPositionAndRotation(targetPosition, targetRotation);
            return;
        }

        float positionBlend = 1f - Mathf.Exp(-remotePositionLerpSpeed * Time.deltaTime);
        float rotationBlend = 1f - Mathf.Exp(-remoteRotationLerpSpeed * Time.deltaTime);

        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, targetPosition, positionBlend),
            Quaternion.Slerp(transform.rotation, targetRotation, rotationBlend));
    }

    public void Configure(int playerId, bool isLocalPlayer)
    {
        CacheReferences();

        PlayerId = playerId;
        IsLocalPlayer = isLocalPlayer;
        ResetRemoteTarget();
        ConfigureLayers();

        if (mouseLook != null)
        {
            mouseLook.playerBody = transform;
            mouseLook.playerCamera = playerCamera;
            mouseLook.audioListener = audioListener;
        }

        if (playerMovement != null)
        {
            playerMovement.controller = GetComponent<CharacterController>();
        }

        if (playerMovement != null)
        {
            playerMovement.enabled = isLocalPlayer;
        }

        if (mouseLook != null)
        {
            mouseLook.enabled = isLocalPlayer;
        }

        if (playerCamera != null)
        {
            playerCamera.enabled = isLocalPlayer;
        }

        if (audioListener != null)
        {
            audioListener.enabled = isLocalPlayer;
        }

        if (isLocalPlayer)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void ApplySnapshot(Vector3 position, Quaternion rotation)
    {
        if (IsLocalPlayer)
        {
            return;
        }

        targetPosition = position;
        targetRotation = rotation;
        hasRemoteTarget = true;
    }

    private void CacheReferences()
    {
        if (playerMovement == null)
        {
            playerMovement = GetComponent<PlayerMovement>();
        }

        if (mouseLook == null)
        {
            mouseLook = GetComponentInChildren<MouseLook>(true);
        }

        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }

        if (audioListener == null)
        {
            audioListener = GetComponentInChildren<AudioListener>(true);
        }
    }

    private void ResetRemoteTarget()
    {
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        hasRemoteTarget = !IsLocalPlayer;
    }

    private void ConfigureLayers()
    {
        int localPlayerLayer = LayerMask.NameToLayer(LocalPlayerLayerName);
        int remotePlayerLayer = LayerMask.NameToLayer(RemotePlayerLayerName);

        if (localPlayerLayer < 0)
        {
            Debug.LogWarning($"Layer '{LocalPlayerLayerName}' does not exist. Local avatar will remain visible.");
            return;
        }

        if (IsLocalPlayer)
        {
            SetLayerRecursively(transform, localPlayerLayer);

            if (playerCamera != null)
            {
                playerCamera.cullingMask &= ~(1 << localPlayerLayer);
            }

            return;
        }

        if (remotePlayerLayer >= 0)
        {
            SetLayerRecursively(transform, remotePlayerLayer);
        }
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;

        for (int childIndex = 0; childIndex < root.childCount; childIndex++)
        {
            SetLayerRecursively(root.GetChild(childIndex), layer);
        }
    }
}
