using System;
using System.Collections;
using System.Collections.Generic;
using NavMeshPlus.Components;
using UnityEngine;

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

    public const int GridDimension = 5;
    private const int GridSize = GridDimension;
    private const int MiddleSlot = 2;
    private const float RoomGap = 10f;
    private const string WallsLayerName = "Walls";
    private const int NotWalkableArea = 1;

    [Header("Layout")]
    public Transform slotsRoot;
    public Transform generatedRoomsRoot;
    public Vector2 roomSize = new Vector2(21f, 21f);
    public bool generateOnStart = true;
    public bool clearGeneratedRooms = true;
    public int randomSeed;

    [Header("Rooms")]
    public RoomPrefabFolder[] roomFolders = Array.Empty<RoomPrefabFolder>();

    [Header("Player Spawn")]
    [SerializeField] private Vector3 playerSpawnLocalOffset = Vector3.zero;

    [Header("Navigation")]
    [SerializeField] private bool rebuildNavMeshAfterGeneration = true;
    [SerializeField] private float navMeshRebuildDelay = 0.05f;

    private readonly Dictionary<RoomSection, GameObject[]> runtimePrefabCache = new Dictionary<RoomSection, GameObject[]>();
    private Coroutine navMeshRebuildCoroutine;

    private void Awake()
    {
        EnsureGridLayout();
    }

    private void Start()
    {
        if (generateOnStart)
        {
            GenerateRooms();
        }
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

    public static bool TryGetPlayerSpawnPosition(out Vector3 position)
    {
#if UNITY_2023_1_OR_NEWER
        RoomGridGenerator[] generators = FindObjectsByType<RoomGridGenerator>(FindObjectsInactive.Include);
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
        // row 0 = top row
        // column 2 = middle column
        Transform slot = GetSlotTransform(0, MiddleSlot);
        Vector3 topMiddlePosition = slot != null ? slot.position : GetSlotPosition(0, MiddleSlot);

        return topMiddlePosition + playerSpawnLocalOffset;
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
                    Debug.LogWarning($"RoomGridGenerator: No prefab found for {section} at row {row}, column {column}.");
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

        RequestNavMeshRebuild();
    }

    public Vector3 GetSlotPosition(int row, int column)
    {
        float spacingX = GetRoomSpacingX();
        float spacingY = GetRoomSpacingY();

        float x = (column - MiddleSlot) * spacingX;
        float y = (MiddleSlot - row) * spacingY;

        return transform.TransformPoint(new Vector3(x, y, 0f));
    }

    public bool TryGetNearestRoomIndex(Vector3 worldPosition, out int row, out int column)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        float spacingX = Mathf.Max(0.01f, GetRoomSpacingX());
        float spacingY = Mathf.Max(0.01f, GetRoomSpacingY());

        column = Mathf.RoundToInt(localPosition.x / spacingX + MiddleSlot);
        row = Mathf.RoundToInt(MiddleSlot - localPosition.y / spacingY);

        column = Mathf.Clamp(column, 0, GridSize - 1);
        row = Mathf.Clamp(row, 0, GridSize - 1);
        return true;
    }

    public bool TryGetAdjacentRoomCenterNear(Vector3 targetPosition, Vector3 seekerPosition, out Vector3 roomCenter)
    {
        if (!TryGetNearestRoomIndex(targetPosition, out int targetRow, out int targetColumn))
        {
            roomCenter = Vector3.zero;
            return false;
        }

        TryGetNearestRoomIndex(seekerPosition, out int seekerRow, out int seekerColumn);

        if (IsRoomAdjacent(targetRow, targetColumn, seekerRow, seekerColumn))
        {
            roomCenter = GetSlotPosition(seekerRow, seekerColumn);
            return true;
        }

        int bestRow = -1;
        int bestColumn = -1;
        float bestDistance = float.PositiveInfinity;

        EvaluateAdjacentRoom(targetRow - 1, targetColumn, seekerPosition, ref bestRow, ref bestColumn, ref bestDistance);
        EvaluateAdjacentRoom(targetRow + 1, targetColumn, seekerPosition, ref bestRow, ref bestColumn, ref bestDistance);
        EvaluateAdjacentRoom(targetRow, targetColumn - 1, seekerPosition, ref bestRow, ref bestColumn, ref bestDistance);
        EvaluateAdjacentRoom(targetRow, targetColumn + 1, seekerPosition, ref bestRow, ref bestColumn, ref bestDistance);

        if (bestRow < 0 || bestColumn < 0)
        {
            roomCenter = Vector3.zero;
            return false;
        }

        roomCenter = GetSlotPosition(bestRow, bestColumn);
        return true;
    }

    public bool IsRoomAdjacent(int rowA, int columnA, int rowB, int columnB)
    {
        if (!IsValidRoomIndex(rowA, columnA) || !IsValidRoomIndex(rowB, columnB))
        {
            return false;
        }

        int distance = Mathf.Abs(rowA - rowB) + Mathf.Abs(columnA - columnB);
        return distance == 1;
    }

    public bool IsValidRoomIndex(int row, int column)
    {
        return row >= 0 && row < GridSize && column >= 0 && column < GridSize;
    }

    public float GetRoomSpacingX()
    {
        return roomSize.x + RoomGap;
    }

    public float GetRoomSpacingY()
    {
        return roomSize.y + RoomGap;
    }

    private void EvaluateAdjacentRoom(int row, int column, Vector3 seekerPosition, ref int bestRow, ref int bestColumn, ref float bestDistance)
    {
        if (!IsValidRoomIndex(row, column))
        {
            return;
        }

        Vector3 candidateCenter = GetSlotPosition(row, column);
        float distance = (candidateCenter - seekerPosition).sqrMagnitude;
        if (distance >= bestDistance)
        {
            return;
        }

        bestDistance = distance;
        bestRow = row;
        bestColumn = column;
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

    private static void PrepareRoomTilemaps(GameObject room)
    {
        UnityEngine.Tilemaps.Tilemap[] tilemaps = room.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true);

        for (int i = 0; i < tilemaps.Length; i++)
        {
            tilemaps[i].CompressBounds();
            tilemaps[i].RefreshAllTiles();
            PrepareNavigationModifier(tilemaps[i]);
        }
    }

    private static void PrepareNavigationModifier(UnityEngine.Tilemaps.Tilemap tilemap)
    {
        if (tilemap == null)
        {
            return;
        }

        NavMeshModifier modifier = tilemap.GetComponent<NavMeshModifier>();
        if (modifier == null)
        {
            modifier = tilemap.gameObject.AddComponent<NavMeshModifier>();
        }

        int wallsLayer = LayerMask.NameToLayer(WallsLayerName);
        bool isWall = tilemap.name.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      (wallsLayer >= 0 && tilemap.gameObject.layer == wallsLayer);
        modifier.ignoreFromBuild = false;
        modifier.overrideArea = isWall;
        modifier.area = isWall ? NotWalkableArea : 0;
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

    private void RequestNavMeshRebuild()
    {
        if (!Application.isPlaying || !rebuildNavMeshAfterGeneration)
        {
            return;
        }

        if (navMeshRebuildCoroutine != null)
        {
            StopCoroutine(navMeshRebuildCoroutine);
        }

        navMeshRebuildCoroutine = StartCoroutine(RebuildNavMeshAfterGeneration());
    }

    private IEnumerator RebuildNavMeshAfterGeneration()
    {
        if (navMeshRebuildDelay > 0f)
        {
            yield return new WaitForSeconds(navMeshRebuildDelay);
        }
        else
        {
            yield return null;
        }

        NavMeshSurface[] surfaces = GetComponentsInChildren<NavMeshSurface>(true);
        if (surfaces.Length == 0)
        {
#if UNITY_2023_1_OR_NEWER
            surfaces = FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include);
#else
            surfaces = FindObjectsOfType<NavMeshSurface>(true);
#endif
        }

        for (int i = 0; i < surfaces.Length; i++)
        {
            NavMeshSurface surface = surfaces[i];
            if (surface == null || !surface.isActiveAndEnabled)
            {
                continue;
            }

            surface.BuildNavMesh();
        }

        navMeshRebuildCoroutine = null;
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
