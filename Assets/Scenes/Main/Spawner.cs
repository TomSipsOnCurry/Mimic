using System.Collections;
using UnityEngine;
using Photon.Pun;

public class CollectibleSpawner : MonoBehaviour
{
    [Header("Collectibles")]
    [SerializeField] private string collectiblePrefabName = "Collectible";

    [Header("Map")]
    [SerializeField] private RoomGridGenerator roomGrid;
    [SerializeField] private float spawnRandomOffset = 6f;
    [SerializeField] private float minimumDistanceFromPlayerSpawn = 8f;

    private void Start()
    {
        StartCoroutine(SpawnAfterMapGenerates());
    }

    private IEnumerator SpawnAfterMapGenerates()
    {
        yield return null;

        if (roomGrid == null)
        {
#if UNITY_2023_1_OR_NEWER
            roomGrid = FindAnyObjectByType<RoomGridGenerator>();
#else
            roomGrid = FindObjectOfType<RoomGridGenerator>();
#endif
        }

        // Only MasterClient (or offline) spawns tokens
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            yield break;
        }

        SpawnTokens();
    }

    private void SpawnTokens()
    {
        int total = GameManager.TotalTokens;
        Debug.Log($"CollectibleSpawner: Spawning {total} global tokens.");

        for (int i = 0; i < total; i++)
        {
            Vector3 pos = GetRandomMapPosition();
            int spriteIndex = Random.Range(0, 1000);

            if (PhotonNetwork.InRoom)
            {
                object[] data = { spriteIndex };
                var go = PhotonNetwork.Instantiate(collectiblePrefabName, pos, Quaternion.identity, 0, data);
                go.name = $"Token_{i + 1}";
            }
            else
            {
                var prefab = Resources.Load<GameObject>(collectiblePrefabName);
                if (prefab == null)
                {
                    Debug.LogError("Collectible prefab not found: " + collectiblePrefabName);
                    return;
                }

                var go = Instantiate(prefab, pos, Quaternion.identity);
                go.name = $"Token_{i + 1}";

                var item = go.GetComponent<CollectibleItem>();
                if (item != null)
                    item.SetupOffline(spriteIndex);
            }
        }
    }

    private Vector3 GetRandomMapPosition()
    {
        if (roomGrid == null) return Vector3.zero;

        for (int attempt = 0; attempt < 40; attempt++)
        {
            int row = Random.Range(0, 5);
            int col = Random.Range(0, 5);
            Vector3 center = roomGrid.GetSlotPosition(row, col);
            Vector2 offset = Random.insideUnitCircle * spawnRandomOffset;
            Vector3 pos = center + new Vector3(offset.x, offset.y, 0f);
            pos.z = 0f;

            if (Vector2.Distance(pos, roomGrid.GetPlayerSpawnPosition()) >= minimumDistanceFromPlayerSpawn)
                return pos;
        }

        return roomGrid.GetSlotPosition(2, 2);
    }
}
