using UnityEngine;
using Photon.Pun;

public class CollectibleItem : MonoBehaviour
{
    [SerializeField] private float collectDistance = 1.2f;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color glowColor = Color.yellow;
    [SerializeField] private float glowPulseSpeed = 6f;

    private SpriteRenderer spriteRenderer;
    private Transform nearbyLocalPlayer;
    private int ownerActorNumber;

    public int OwnerActorNumber => ownerActorNumber;

    private void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            spriteRenderer.color = normalColor;
        }
    }

    public void Setup(int actorNumber, Sprite sprite)
    {
        ownerActorNumber = actorNumber;

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (spriteRenderer != null && sprite != null)
        {
            spriteRenderer.sprite = sprite;
        }
    }

    private void Update()
    {
        nearbyLocalPlayer = FindLocalPlayer();

        bool canCollect = nearbyLocalPlayer != null &&
                          IsOwnedByLocalPlayer() &&
                          Vector2.Distance(transform.position, nearbyLocalPlayer.position) <= collectDistance;

        UpdateGlow(canCollect);

        if (canCollect && Input.GetKeyDown(KeyCode.Space))
        {
            Collect();
        }
    }

    private bool IsOwnedByLocalPlayer()
    {
        if (!PhotonNetwork.InRoom)
        {
            return true;
        }

        return PhotonNetwork.LocalPlayer != null &&
               PhotonNetwork.LocalPlayer.ActorNumber == ownerActorNumber;
    }

    private Transform FindLocalPlayer()
    {
#if UNITY_2023_1_OR_NEWER
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
#endif

        for (int i = 0; i < players.Length; i++)
        {
            PlayerMovement player = players[i];

            if (player == null || !player.gameObject.activeInHierarchy)
            {
                continue;
            }

            PhotonView view = player.GetComponent<PhotonView>();

            if (view == null || view.IsMine)
            {
                return player.transform;
            }
        }

        return null;
    }

    private void UpdateGlow(bool glowing)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        if (!glowing)
        {
            spriteRenderer.color = normalColor;
            return;
        }

        float pulse = (Mathf.Sin(Time.time * glowPulseSpeed) + 1f) * 0.5f;
        spriteRenderer.color = Color.Lerp(normalColor, glowColor, pulse);
    }

    private void Collect()
    {
        Debug.Log("Collected item for actor: " + ownerActorNumber);

        CollectibleSpawner.NotifyCollected(ownerActorNumber);

        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, collectDistance);
    }
}