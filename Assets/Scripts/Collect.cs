using UnityEngine;
using Photon.Pun;

public class CollectibleItem : MonoBehaviourPun
{
    [Header("Collect")]
    [SerializeField] private float collectDistance = 1.5f;

    [Header("Sprites")]
    [SerializeField] private Sprite[] possibleSprites;

    [Header("Glow")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color glowColor = Color.yellow;
    [SerializeField] private float glowPulseSpeed = 6f;

    [Header("Audio")]
    [SerializeField] private AudioClip collectSound;
    [SerializeField] private float destroyDelayAfterCollect = 0.25f;

    private SpriteRenderer spriteRenderer;
    private Collider2D itemCollider;
    private int ownerActorNumber = -1;
    private bool collected;

    private void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        itemCollider = GetComponent<Collider2D>();

        ReadPhotonSpawnData();
        ApplySpriteFromIndex();
        ApplyLocalVisibility();
    }

    private void ReadPhotonSpawnData()
    {
        if (PhotonNetwork.InRoom &&
            photonView != null &&
            photonView.InstantiationData != null &&
            photonView.InstantiationData.Length >= 2)
        {
            ownerActorNumber = (int)photonView.InstantiationData[0];
            int spriteIndex = (int)photonView.InstantiationData[1];
            ApplySprite(spriteIndex);
        }
        else if (!PhotonNetwork.InRoom)
        {
            ownerActorNumber = 1;
        }

        Debug.Log($"{name} belongs to actor {ownerActorNumber}. Local actor is {(PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1)}");
    }

    public void SetupOffline(int actorNumber, int spriteIndex)
    {
        ownerActorNumber = actorNumber;

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (itemCollider == null)
        {
            itemCollider = GetComponent<Collider2D>();
        }

        ApplySprite(spriteIndex);
        ApplyLocalVisibility();
    }

    private void ApplySpriteFromIndex()
    {
        if (PhotonNetwork.InRoom &&
            photonView != null &&
            photonView.InstantiationData != null &&
            photonView.InstantiationData.Length >= 2)
        {
            int spriteIndex = (int)photonView.InstantiationData[1];
            ApplySprite(spriteIndex);
        }
    }

    private void ApplySprite(int spriteIndex)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        if (possibleSprites == null || possibleSprites.Length == 0)
        {
            return;
        }

        int safeIndex = Mathf.Abs(spriteIndex) % possibleSprites.Length;
        spriteRenderer.sprite = possibleSprites[safeIndex];
    }

    private void ApplyLocalVisibility()
    {
        bool visibleToMe = IsOwnedByLocalPlayer();

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = visibleToMe;
            spriteRenderer.color = normalColor;
        }

        if (itemCollider != null)
        {
            itemCollider.enabled = visibleToMe;
            itemCollider.isTrigger = true;
        }
    }

    private void Update()
    {
        if (collected)
        {
            return;
        }

        if (!IsOwnedByLocalPlayer())
        {
            return;
        }

        Transform localPlayer = FindLocalPlayer();

        if (localPlayer == null)
        {
            UpdateGlow(false);
            return;
        }

        float distance = Vector2.Distance(transform.position, localPlayer.position);
        bool canCollect = distance <= collectDistance;

        UpdateGlow(canCollect);

        if (canCollect && Input.GetKeyDown(KeyCode.Space))
        {
            RequestCollect();
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

        foreach (PlayerMovement player in players)
        {
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

    private void RequestCollect()
    {
        if (collected)
        {
            return;
        }

        collected = true;

        if (!PhotonNetwork.InRoom)
        {
            CollectibleSpawner.NotifyCollected(ownerActorNumber);
            PlayCollectSound();
            Destroy(gameObject, destroyDelayAfterCollect);
            return;
        }

        photonView.RPC(nameof(CollectOnMaster), RpcTarget.MasterClient, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    private void PlayCollectSound()
    {
        if (collectSound == null)
        {
            return;
        }

        AudioSource.PlayClipAtPoint(collectSound, transform.position);
    }

    [PunRPC]
    private void PlayCollectSoundRpc()
    {
        PlayCollectSound();
    }

    [PunRPC]
    private void CollectOnMaster(int requestingActorNumber)
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            return;
        }

        if (requestingActorNumber != ownerActorNumber)
        {
            Debug.LogWarning($"Actor {requestingActorNumber} tried to collect actor {ownerActorNumber}'s item.");
            return;
        }

        CollectibleSpawner.NotifyCollected(ownerActorNumber);
        photonView.RPC(nameof(PlayCollectSoundRpc), RpcTarget.All);
        PhotonNetwork.Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, collectDistance);
    }
}