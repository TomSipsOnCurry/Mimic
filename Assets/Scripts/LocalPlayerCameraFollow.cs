using Photon.Pun;
using UnityEngine;

public class LocalPlayerCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);
    [SerializeField] private float followSpeed = 12f;

    private Transform currentTarget;

    private void LateUpdate()
    {
        Transform followTarget = ResolveTarget();
        if (followTarget == null)
        {
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
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
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
}
