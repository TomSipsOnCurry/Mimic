using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class CollectibleSpawner : MonoBehaviour
{
    [Header("Collectibles")]
    [SerializeField] private string collectiblePrefabName = "Collectible";
    [SerializeField] private Sprite[] possibleSprites;
    [SerializeField] private int collectiblesPerPlayer = 2;

    [Header("Map")]
    [SerializeField] private RoomGridGenerator roomGrid;
    [SerializeField] private float spawnRandomOffset = 6f;
    [SerializeField] private float minimumDistanceFromPlayerSpawn = 8f;

    private static readonly Dictionary<int, int> collectedCounts = new Dictionary<int, int>();

    private void Start()
    {
        if (roomGrid == null)
        {
#if UNITY_2023_1_OR_NEWER
            roomGrid = FindAnyObjectByType<RoomGridGenerator>();
#else
            roomGrid = FindObjectOfType<RoomGridGenerator>();
#endif
        }

        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        SpawnCollectibles();
    }

    private void SpawnCollectibles()
    {
        if (PhotonNetwork.InRoom)
        {
            Player[] players = PhotonNetwork.PlayerList;

            for (int i = 0; i < players.Length; i++)
            {
                SpawnForActor(players[i].ActorNumber);
            }
        }
        else
        {
            SpawnForActor(1);
        }
    }

    private void SpawnForActor(int actorNumber)
    {
        collectedCounts[actorNumber] = 0;

        for (int i = 0; i < collectiblesPerPlayer; i++)
        {
            Vector3 spawnPosition = GetRandomMapPosition();

            GameObject item;

            if (PhotonNetwork.InRoom)
            {
                item = PhotonNetwork.Instantiate(
                    collectiblePrefabName,
                    spawnPosition,
                    Quaternion.identity
                );
            }
            else
            {
                GameObject prefab = Resources.Load<GameObject>(collectiblePrefabName);

                if (prefab == null)
                {
                    Debug.LogError("Collectible prefab not found in Resources: " + collectiblePrefabName);
                    return;
                }

                item = Instantiate(prefab, spawnPosition, Quaternion.identity);
            }

            CollectibleItem collectible = item.GetComponent<CollectibleItem>();

            if (collectible != null)
            {
                Sprite sprite = GetRandomSprite();
                collectible.Setup(actorNumber, sprite);
            }

            item.name = $"Collectible_Actor_{actorNumber}_{i + 1}";
        }
    }

    private Vector3 GetRandomMapPosition()
    {
        if (roomGrid == null)
        {
            return Vector3.zero;
        }

        for (int attempt = 0; attempt < 40; attempt++)
        {
            int row = Random.Range(0, 5);
            int column = Random.Range(0, 5);

            Vector3 roomCenter = roomGrid.GetSlotPosition(row, column);
            Vector2 randomOffset = Random.insideUnitCircle * spawnRandomOffset;

            Vector3 position = roomCenter + new Vector3(randomOffset.x, randomOffset.y, 0f);

            if (Vector2.Distance(position, roomGrid.GetPlayerSpawnPosition()) >= minimumDistanceFromPlayerSpawn)
            {
                return position;
            }
        }

        return roomGrid.GetSlotPosition(2, 2);
    }

    private Sprite GetRandomSprite()
    {
        if (possibleSprites == null || possibleSprites.Length == 0)
        {
            return null;
        }

        return possibleSprites[Random.Range(0, possibleSprites.Length)];
    }

    public static void NotifyCollected(int actorNumber)
    {
        if (!collectedCounts.ContainsKey(actorNumber))
        {
            collectedCounts[actorNumber] = 0;
        }

        collectedCounts[actorNumber]++;

        Debug.Log($"Player {actorNumber} collected {collectedCounts[actorNumber]}/2 items.");

        if (collectedCounts[actorNumber] >= 2)
        {
            Debug.Log($"Player {actorNumber} collected all required items!");
        }
    }
}