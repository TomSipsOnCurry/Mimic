using System;
using System.Collections.Generic;
using UnityEngine;
//using UnityEngine.AI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(Grid))]
public class RoomGridGenerator : MonoBehaviour
{
    public enum RoomSection
    {
        TopLeft,
        TopMiddle,
        TopRight,
        MiddleLeft,
        MiddleMiddle,
        MiddleRight,
        BottomLeft,
        BottomMiddle,
        BottomRight
    }

    [Serializable]
    public sealed class RoomPrefabFolder
    {
        public RoomSection section;
        public string folderPath;
        public GameObject[] prefabs;
    }

    private const int GridSize = 5;
    private const int MiddleSlot = 2;
    private const string WallsLayerName = "Walls";

    [SerializeField] private string enemyPrefabName = "Enemy";
    //[SerializeField] private GameObject navigationObject;

    [Header("Layout")]
    public Transform slotsRoot;
    public Transform generatedRoomsRoot;
    public Vector2 roomSize = new Vector2(21f, 20f);
    public bool generateOnStart = true;
    public bool clearGeneratedRooms = true;
    public int randomSeed;

    [Header("Rooms")]
    public RoomPrefabFolder[] roomFolders = Array.Empty<RoomPrefabFolder>();

    private readonly Dictionary<RoomSection, GameObject[]> runtimePrefabCache = new Dictionary<RoomSection, GameObject[]>();

    private void Awake()
    {
        EnsureGridLayout();
    }

    private void Start()
    {
        if (!generateOnStart)
        {
            return;
        }

        GenerateRooms();

        /***
        if (navigationObject == null)
        {
            GameObject found = GameObject.Find("Navigation");

            if (found != null)
            {
                navigationObject = found;
            }
        }

        if (navigationObject == null)
        {
            Debug.LogError("Navigation object is not assigned/found. Enemy will not spawn.");
            return;
        }

        navigationObject.SendMessage("BuildNavMesh", SendMessageOptions.DontRequireReceiver);
        Debug.Log("Tried to build NavMeshPlus surface after room generation.");

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        Debug.Log("NavMesh vertices: " + triangulation.vertices.Length);

        if (triangulation.vertices.Length == 0)
        {
            Debug.LogError("NavMesh has 0 vertices. Enemy will not spawn.");
            return;
        }

        SpawnEnemy();
        **/
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying || roomFolders == null)
        {
            return;
        }

        for (int i = 0; i < roomFolders.Length; i++)
        {
            RoomPrefabFolder folder = roomFolders[i];
            if (folder == null || string.IsNullOrWhiteSpace(folder.folderPath))
            {
                continue;
            }

            GameObject[] prefabs = LoadPrefabsFromFolder(folder);
            if (!HaveSamePrefabs(folder.prefabs, prefabs))
            {
                folder.prefabs = prefabs;
            }
        }
    }
#endif

    private void EnsureGridLayout()
    {
        Grid grid = GetComponent<Grid>();
        if (grid == null)
        {
            return;
        }

        grid.cellSize = Vector3.one;
        grid.cellGap = Vector3.zero;
        grid.cellLayout = GridLayout.CellLayout.Rectangle;
        grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;
    }

    private void SpawnEnemy()
    {
        // Only host/master spawns enemy
        if (Photon.Pun.PhotonNetwork.InRoom &&
            !Photon.Pun.PhotonNetwork.IsMasterClient)
        {
            return;
        }

        Vector3 spawnPosition = GetSlotPosition(2, 2);

        if (Photon.Pun.PhotonNetwork.InRoom)
        {
            Photon.Pun.PhotonNetwork.Instantiate(
                enemyPrefabName,
                spawnPosition,
                Quaternion.identity
            );
        }
        else
        {
            GameObject enemyPrefab =
                Resources.Load<GameObject>(enemyPrefabName);

            if (enemyPrefab != null)
            {
                Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
            }
        }
    }

    public static bool TryGetPlayerSpawnPosition(out Vector3 position)
    {
#if UNITY_2023_1_OR_NEWER
        RoomGridGenerator[] generators = FindObjectsByType<RoomGridGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        RoomGridGenerator[] generators = FindObjectsOfType<RoomGridGenerator>(true);
#endif

        for (int i = 0; i < generators.Length; i++)
        {
            RoomGridGenerator generator = generators[i];
            if (generator == null || !generator.gameObject.scene.IsValid())
            {
                continue;
            }

            position = generator.GetPlayerSpawnPosition();
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    public Vector3 GetPlayerSpawnPosition()
    {
        Transform slot = GetSlotTransform(MiddleSlot, GridSize - 1);
        return slot != null ? slot.position : GetSlotPosition(MiddleSlot, GridSize - 1);
    }

    public void GenerateRooms()
    {
        EnsureGeneratedRoomsRoot();

        if (clearGeneratedRooms)
        {
            ClearGeneratedRooms();
        }

        runtimePrefabCache.Clear();

        UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
        if (randomSeed != 0)
        {
            UnityEngine.Random.InitState(randomSeed);
        }

        for (int row = 0; row < GridSize; row++)
        {
            for (int column = 0; column < GridSize; column++)
            {
                RoomSection section = GetSection(row, column);
                GameObject prefab = GetRandomPrefab(section);
                if (prefab == null)
                {
                    Debug.LogWarning($"RoomGridGenerator: No prefab found for {section}.");
                    continue;
                }

                Transform slot = GetSlotTransform(row, column);
                Vector3 position = slot != null ? slot.position : GetSlotPosition(row, column);
                GameObject room = Instantiate(prefab, position, Quaternion.identity, generatedRoomsRoot);
                room.name = $"Room_{row}_{column}_{section}_{prefab.name}";
                ApplyWallsLayer(room);
                PrepareRoomTilemaps(room);
            }
        }

        if (randomSeed != 0)
        {
            UnityEngine.Random.state = previousRandomState;
        }
    }

    public Vector3 GetSlotPosition(int row, int column)
    {
        float roomGap = 10f;

        float spacingX = roomSize.x + roomGap;
        float spacingY = roomSize.y + roomGap;

        float x = (column - MiddleSlot) * spacingX;
        float y = (MiddleSlot - row) * spacingY;

        return transform.TransformPoint(new Vector3(x, y, 0f));
    }

    private static RoomSection GetSection(int row, int column)
    {
        bool top = row == 0;
        bool bottom = row == GridSize - 1;
        bool left = column == 0;
        bool right = column == GridSize - 1;

        if (top && left) return RoomSection.TopLeft;
        if (top && right) return RoomSection.TopRight;
        if (top) return RoomSection.TopMiddle;
        if (bottom && left) return RoomSection.BottomLeft;
        if (bottom && right) return RoomSection.BottomRight;
        if (bottom) return RoomSection.BottomMiddle;
        if (left) return RoomSection.MiddleLeft;
        if (right) return RoomSection.MiddleRight;
        return RoomSection.MiddleMiddle;
    }

    private Transform GetSlotTransform(int row, int column)
    {
        if (slotsRoot == null)
        {
            return null;
        }

        string slotName = GetSlotName(row, column);
        Transform slot = slotsRoot.Find(slotName);
        return slot != null ? slot : null;
    }

    public static string GetSlotName(int row, int column)
    {
        return $"RoomSlot_{row}_{column}";
    }

    private GameObject GetRandomPrefab(RoomSection section)
    {
        GameObject[] candidates = GetPrefabs(section);
        if (candidates == null || candidates.Length == 0)
        {
            return null;
        }

        return candidates[UnityEngine.Random.Range(0, candidates.Length)];
    }

    private static void PrepareRoomTilemaps(GameObject room)
    {
        UnityEngine.Tilemaps.Tilemap[] tilemaps = room.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true);
        for (int i = 0; i < tilemaps.Length; i++)
        {
            tilemaps[i].CompressBounds();
            tilemaps[i].RefreshAllTiles();
        }
    }

    private static void ApplyWallsLayer(GameObject room)
    {
        int wallsLayer = LayerMask.NameToLayer(WallsLayerName);
        if (wallsLayer < 0)
        {
            Debug.LogWarning($"RoomGridGenerator: Layer '{WallsLayerName}' was not found.");
            return;
        }

        UnityEngine.Tilemaps.Tilemap[] tilemaps = room.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true);
        for (int i = 0; i < tilemaps.Length; i++)
        {
            UnityEngine.Tilemaps.Tilemap tilemap = tilemaps[i];
            if (tilemap.name.IndexOf("wall", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            tilemap.gameObject.layer = wallsLayer;
        }
    }

    private GameObject[] GetPrefabs(RoomSection section)
    {
        if (runtimePrefabCache.TryGetValue(section, out GameObject[] cached))
        {
            return cached;
        }

        RoomPrefabFolder folder = GetFolder(section);
        GameObject[] prefabs = LoadPrefabsFromFolder(folder);
        runtimePrefabCache[section] = prefabs;
        return prefabs;
    }

    private RoomPrefabFolder GetFolder(RoomSection section)
    {
        for (int i = 0; i < roomFolders.Length; i++)
        {
            RoomPrefabFolder folder = roomFolders[i];
            if (folder != null && folder.section == section)
            {
                return folder;
            }
        }

        return null;
    }

    private static GameObject[] LoadPrefabsFromFolder(RoomPrefabFolder folder)
    {
        if (folder == null)
        {
            return Array.Empty<GameObject>();
        }

#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(folder.folderPath))
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder.folderPath });
            List<GameObject> editorPrefabs = new List<GameObject>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    editorPrefabs.Add(prefab);
                }
            }

            if (editorPrefabs.Count > 0)
            {
                return editorPrefabs.ToArray();
            }
        }
#endif

        return folder.prefabs ?? Array.Empty<GameObject>();
    }

    private static bool HaveSamePrefabs(GameObject[] a, GameObject[] b)
    {
        int aLength = a != null ? a.Length : 0;
        int bLength = b != null ? b.Length : 0;
        if (aLength != bLength)
        {
            return false;
        }

        for (int i = 0; i < aLength; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private void EnsureGeneratedRoomsRoot()
    {
        if (generatedRoomsRoot != null)
        {
            return;
        }

        GameObject roomsRoot = new GameObject("Generated Rooms");
        roomsRoot.transform.SetParent(transform, false);
        generatedRoomsRoot = roomsRoot.transform;
    }

    private void ClearGeneratedRooms()
    {
        if (generatedRoomsRoot == null)
        {
            return;
        }

        for (int i = generatedRoomsRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = generatedRoomsRoot.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        for (int row = 0; row < GridSize; row++)
        {
            for (int column = 0; column < GridSize; column++)
            {
                Vector3 position = GetSlotPosition(row, column);
                Gizmos.DrawWireCube(position, new Vector3(roomSize.x, roomSize.y, 0f));
            }
        }

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(GetPlayerSpawnPosition(), 0.75f);
    }
}
