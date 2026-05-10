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
    private bool collected;

    private void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        itemCollider = GetComponent<Collider2D>();
        ReadSpawnData();
    }

    private void ReadSpawnData()
    {
        if (PhotonNetwork.InRoom &&
            photonView != null &&
            photonView.InstantiationData != null &&
            photonView.InstantiationData.Length >= 1)
        {
            int spriteIndex = (int)photonView.InstantiationData[0];
            ApplySprite(spriteIndex);
        }

        if (itemCollider != null)
            itemCollider.isTrigger = true;
    }

    public void SetupOffline(int spriteIndex)
    {
        spriteRenderer = spriteRenderer != null ? spriteRenderer : GetComponentInChildren<SpriteRenderer>();
        itemCollider = itemCollider != null ? itemCollider : GetComponent<Collider2D>();

        ApplySprite(spriteIndex);

        if (itemCollider != null)
            itemCollider.isTrigger = true;
    }

    private void ApplySprite(int spriteIndex)
    {
        if (spriteRenderer == null || possibleSprites == null || possibleSprites.Length == 0) return;
        spriteRenderer.sprite = possibleSprites[Mathf.Abs(spriteIndex) % possibleSprites.Length];
    }

    private void Update()
    {
        if (collected) return;

        Transform localPlayer = FindLocalPlayer();
        bool canCollect = localPlayer != null &&
                          Vector2.Distance(transform.position, localPlayer.position) <= collectDistance;

        UpdateGlow(canCollect);

        if (canCollect && Input.GetKeyDown(KeyCode.Space))
            RequestCollect();
    }

    private static Transform FindLocalPlayer()
    {
#if UNITY_2023_1_OR_NEWER
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
#endif
        foreach (var p in players)
        {
            if (p == null || !p.gameObject.activeInHierarchy) continue;
            PhotonView pv = p.GetComponent<PhotonView>();
            if (pv == null || pv.IsMine) return p.transform;
        }

        return null;
    }

    private void UpdateGlow(bool glowing)
    {
        if (spriteRenderer == null) return;

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
        if (collected) return;
        collected = true;

        if (!PhotonNetwork.InRoom)
        {
            PlayCollectSound();
            if (GameManager.Instance != null) GameManager.Instance.AddToken();
            Destroy(gameObject, destroyDelayAfterCollect);
            return;
        }

        photonView.RPC(nameof(RPC_CollectOnMaster), RpcTarget.MasterClient);
    }

    [PunRPC]
    private void RPC_CollectOnMaster()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        if (GameManager.Instance != null)
            GameManager.Instance.AddToken();

        photonView.RPC(nameof(RPC_PlaySound), RpcTarget.All);
        PhotonNetwork.Destroy(gameObject);
    }

    [PunRPC]
    private void RPC_PlaySound()
    {
        PlayCollectSound();
    }

    private void PlayCollectSound()
    {
        if (collectSound != null)
            AudioSource.PlayClipAtPoint(collectSound, transform.position);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, collectDistance);
    }
}
