#if ENABLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM_PACKAGE
#define USE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using UnityEngine;

public class MouseLook : MonoBehaviour
{
    private const float InputSystemSensitivityScale = 0.01f;
    private const float LegacyInputReferenceFrameTime = 1f / 60f;

    public float mouseSensitivity = 100f;

    public Transform playerBody;

    public Camera playerCamera;

    public AudioListener audioListener;

    private float xRotation = 0f;

    public float CurrentPitch => xRotation;

    private void Awake()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponent<Camera>();
        }

        if (audioListener == null)
        {
            audioListener = GetComponent<AudioListener>();
        }

        if (playerBody == null && transform.parent != null)
        {
            playerBody = transform.parent;
        }
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!enabled)
        {
            return;
        }

        // If chat is open, ChatManager sets Cursor.visible = true. Don't fight it.
        if (hasFocus && !Cursor.visible)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        if (playerBody == null || !enabled)
        {
            return;
        }

        Vector2 mouseDelta = GetMouseDelta();
        float mouseX = mouseDelta.x;
        float mouseY = mouseDelta.y;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        playerBody.Rotate(Vector3.up * mouseX);
    }

    private Vector2 GetMouseDelta()
    {
#if USE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            return delta * mouseSensitivity * InputSystemSensitivityScale;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * LegacyInputReferenceFrameTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * LegacyInputReferenceFrameTime;
        return new Vector2(mouseX, mouseY);
#else
        return Vector2.zero;
#endif
    }
}
