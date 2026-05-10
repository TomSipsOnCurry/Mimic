using Photon.Pun;
using UnityEngine;

public class LocalPlayerCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);
    [SerializeField] private float followSpeed = 12f;
    [SerializeField] private bool placeAudioListenerOnLocalPlayer = true;
    [SerializeField] private Vector3 audioListenerOffset = Vector3.zero;

    private Transform currentTarget;
    private AudioListener cameraAudioListener;
    private AudioListener localPlayerAudioListener;

    private void Awake()
    {
        cameraAudioListener = GetComponent<AudioListener>();
    }

    private void LateUpdate()
    {
        Transform followTarget = ResolveTarget();
        if (followTarget == null)
        {
            SetCameraAudioListenerEnabled(true);
            return;
        }

        Vector3 targetPosition = followTarget.position + offset;
        if (followSpeed <= 0f || currentTarget != followTarget)
        {
            transform.position = targetPosition;
        }
        else
        {
            float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPosition, t);
        }

        currentTarget = followTarget;
        UpdateLocalPlayerAudioListener(followTarget);
    }

    private void OnDisable()
    {
        SetCameraAudioListenerEnabled(true);
        DestroyLocalPlayerAudioListener();
    }

    private void OnDestroy()
    {
        SetCameraAudioListenerEnabled(true);
        DestroyLocalPlayerAudioListener();
    }

    private Transform ResolveTarget()
    {
        if (IsValidTarget(target))
        {
            return target;
        }

        target = FindLocalPlayerTarget();
        return target;
    }

    private static Transform FindLocalPlayerTarget()
    {
#if UNITY_2023_1_OR_NEWER
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
#else
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
#endif

        Transform fallback = null;
        for (int i = 0; i < players.Length; i++)
        {
            PlayerMovement player = players[i];
            if (player == null || !player.gameObject.activeInHierarchy)
            {
                continue;
            }

            PhotonView playerView = player.GetComponent<PhotonView>();
            if (playerView != null)
            {
                if (playerView.IsMine)
                {
                    return player.transform;
                }

                continue;
            }

            if (fallback == null)
            {
                fallback = player.transform;
            }
        }

        return fallback;
    }

    private static bool IsValidTarget(Transform candidate)
    {
        if (candidate == null || !candidate.gameObject.activeInHierarchy)
        {
            return false;
        }

        PhotonView playerView = candidate.GetComponent<PhotonView>();
        return playerView == null || playerView.IsMine;
    }

    private void UpdateLocalPlayerAudioListener(Transform followTarget)
    {
        if (!placeAudioListenerOnLocalPlayer)
        {
            SetCameraAudioListenerEnabled(true);
            DestroyLocalPlayerAudioListener();
            return;
        }

        EnsureLocalPlayerAudioListener();
        if (localPlayerAudioListener == null)
        {
            SetCameraAudioListenerEnabled(true);
            return;
        }

        SetCameraAudioListenerEnabled(false);

        Transform listenerTransform = localPlayerAudioListener.transform;
        listenerTransform.position = followTarget.position + audioListenerOffset;
        listenerTransform.rotation = Quaternion.identity;
    }

    private void EnsureLocalPlayerAudioListener()
    {
        if (localPlayerAudioListener != null)
        {
            return;
        }

        GameObject listenerObject = new GameObject("Local Player Audio Listener");
        listenerObject.transform.SetParent(transform, true);
        localPlayerAudioListener = listenerObject.AddComponent<AudioListener>();
    }

    private void DestroyLocalPlayerAudioListener()
    {
        if (localPlayerAudioListener == null)
        {
            return;
        }

        GameObject listenerObject = localPlayerAudioListener.gameObject;
        localPlayerAudioListener = null;

        if (Application.isPlaying)
        {
            Destroy(listenerObject);
        }
        else
        {
            DestroyImmediate(listenerObject);
        }
    }

    private void SetCameraAudioListenerEnabled(bool enabled)
    {
        if (cameraAudioListener != null)
        {
            cameraAudioListener.enabled = enabled;
        }
    }
}
