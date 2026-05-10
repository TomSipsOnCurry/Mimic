using System;
using System.Collections.Generic;
using System.Text;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class MimicEnemy : MonoBehaviour
{
    private const int PlayerVariantCount = 4;
    private const float PlayerSpriteStartX = 1f;
    private const float PlayerSpriteStrideX = 24f;
    private const float PlayerHeadY = 90f;
    private const float PlayerHeadWidth = 23f;
    private const float PlayerHeadHeight = 9f;
    private const float PlayerBodyY = 64f;
    private const float PlayerBodyWidth = 23f;
    private const float PlayerBodyHeight = 25f;
    private const int NotWalkableArea = 1;

    [Header("Disguise")]
    [SerializeField] private string playerPrefabResourceName = "Player";
    [SerializeField] private int playerVariant;
    [SerializeField] private bool applyPlayerScaleOnAwake = true;
    [SerializeField] private Vector3 playerFormScale = new Vector3(9f, 9f, 9f);
    [SerializeField] private int sortingOrderBase = 510;

    [Header("Mimic Appearance")]
    [SerializeField] private bool randomizePlayerVariant = true;

    [Header("VoiceAI")]
    [SerializeField] private float initialSpeechDelay = 3f;
    [SerializeField] private float chatPollInterval = 4f;
    [SerializeField] private float chatPollJitter = 1f;
    [SerializeField] private int minPlayerMessages = 1;
    [SerializeField] private int maxSpeechWords = 12;
    [SerializeField] private float speakWithoutNewChatChance = 0.25f;

    [Header("Spatial Audio")]
    [SerializeField] private bool enableSpatialAudio = true;
    [SerializeField, Range(0f, 1f)] private float voiceSpatialBlend = 1f;
    [SerializeField] private float spatialMinDistance = 1.5f;
    [SerializeField] private float spatialMaxDistance = 80f;
    [SerializeField] private AudioRolloffMode spatialRolloffMode = AudioRolloffMode.Linear;

    [Header("Speech Visuals")]
    [SerializeField] private float speechTextWorldYOffset = 2.5f;
    [SerializeField] private Vector2 speechTextSize = new Vector2(360f, 82f);
    [SerializeField] private float speechTextFontSize = 28f;
    [SerializeField] private TMP_FontAsset speechTextFont;
    [SerializeField] private string speechTextFontResourcePath = "Fonts & Materials/BoldPixels SDF";

    [Header("Talking Animation")]
    [SerializeField] private float talkingHeadMinY = 0.16f;
    [SerializeField] private float talkingHeadMaxY = 0.19f;
    [SerializeField] private float talkingHeadResponse = 20f;
    [SerializeField] private float audioToHeadScale = 18f;
    [SerializeField] private float talkingTiltMaxDegrees = 8f;
    [SerializeField] private Vector2 talkingTiltRetargetInterval = new Vector2(0.06f, 0.14f);
    [SerializeField] private float talkingTiltResponse = 16f;
    [SerializeField] private Vector3 generatedMouthLocalPosition = new Vector3(0f, -0.045f, -0.01f);
    [SerializeField] private Vector3 generatedMouthLocalScale = new Vector3(0.055f, 0.006f, 1f);
    [SerializeField] private float mouthOpenScale = 5f;
    [SerializeField] private float mouthResponse = 24f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float moveAcceleration = 50f;
    [SerializeField] private float roomRoamRadius = 7f;
    [SerializeField, Range(0f, 1f)] private float randomRoomMoveChance = 0.3f;
    [SerializeField] private float retargetInterval = 5f;
    [SerializeField] private float retargetJitter = 2f;
    [SerializeField] private float arrivalDistance = 1f;
    [SerializeField] private float navMeshSearchRadius = 20f;
    [SerializeField] private float moveStartDelay = 0.5f;
    [SerializeField] private float navMeshRetryInterval = 0.25f;
    [SerializeField] private float navMeshPlacementTimeout = 8f;
    [SerializeField] private float roomCenterSampleRadius = 6f;
    [SerializeField] private int destinationProbeAttempts = 20;
    [SerializeField] private float stuckRepathTime = 2f;

    private readonly float[] audioSamples = new float[64];
    private readonly List<string> recentMimicLines = new List<string>();

    private AudioSource voiceSource;
    private SpriteRenderer bodyRenderer;
    private SpriteRenderer headRenderer;
    private Transform headTransform;
    private Transform mouthTransform;
    private Canvas speechCanvas;
    private RectTransform speechTextRect;
    private TextMeshProUGUI speechText;
    private Camera mainCamera;
    private Vector3 baseHeadLocalPosition;
    private Vector3 baseHeadLocalEuler;
    private Vector3 baseMouthLocalScale;
    private float currentTiltDegrees;
    private float targetTiltDegrees;
    private float tiltRetargetTimer;
    private float pollTimer;
    private int lastObservedMessageCount;
    private string lastSpokenLine;
    private NavMeshAgent mimicAgent;
    private RoomGridGenerator roomGrid;
    private Vector3 moveDestination;
    private bool hasDestination;
    private float moveTimer;
    private float navStartTime;
    private float navRetryTimer;
    private float stuckTimer;
    private Vector3 lastMovePosition;
    private bool navReady;
    private bool reportedNavFailure;

    private static Sprite generatedMouthSprite;

    private void Awake()
    {
        if (string.IsNullOrWhiteSpace(gameObject.name) || gameObject.name == "GameObject")
        {
            gameObject.name = "MIMIC";
        }

        // If EnemyAI was left on this object, disable it — MimicEnemy owns all movement here
        EnemyAI conflictingAI = GetComponent<EnemyAI>();
        if (conflictingAI != null)
        {
            conflictingAI.enabled = false;
        }

        EnsureVoiceSource();
        EnsureDisguise();
        InitMimicAgent();
        pollTimer = Mathf.Max(0.25f, initialSpeechDelay);

        // Make our collider a trigger so it detects player overlap
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void Start()
    {
        navStartTime = Time.time;
        navRetryTimer = Mathf.Max(0f, moveStartDelay);
        lastMovePosition = transform.position;
    }

    [Header("Death Detection")]
    [SerializeField] private float touchKillRadius = 1.5f;

    private bool deathTriggered;

    private void Update()
    {
        UpdateMovement();
        CheckMimicTouch();

        pollTimer -= Time.deltaTime;
        if (pollTimer > 0f)
        {
            return;
        }

        pollTimer = Mathf.Max(0.5f, chatPollInterval + UnityEngine.Random.Range(-chatPollJitter, chatPollJitter));

        if (voiceSource != null && voiceSource.isPlaying)
        {
            return;
        }

        TrySpeakFromChatLog();
    }

    private void LateUpdate()
    {
        UpdateHeadFromAudio();
        UpdateSpeechTextPosition();
    }

    private void OnDestroy()
    {
        DeleteSpeechText();
    }

    // ── Movement ─────────────────────────────────────────────────────────────

    private void InitMimicAgent()
    {
        mimicAgent = GetComponent<NavMeshAgent>();
        if (mimicAgent == null)
        {
            mimicAgent = gameObject.AddComponent<NavMeshAgent>();
        }

        // AgentOverride2d (NavMeshPlus) handles updateRotation/updateUpAxis for 2D
        mimicAgent.speed = moveSpeed;
        mimicAgent.acceleration = moveAcceleration;
        mimicAgent.angularSpeed = 0f;
        mimicAgent.stoppingDistance = 0.1f;
        mimicAgent.autoBraking = false;
        mimicAgent.baseOffset = 0f;
        mimicAgent.areaMask = GetWalkableAreaMask();
        mimicAgent.enabled = false;
    }

    private void UpdateMovement()
    {
        // Only the MasterClient runs AI logic — same pattern as EnemyAI
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        if (!navReady)
        {
            navRetryTimer -= Time.deltaTime;
            if (navRetryTimer <= 0f)
            {
                navRetryTimer = Mathf.Max(0.05f, navMeshRetryInterval);
                TryInitNavigation();
            }

            return;
        }

        if (!IsMimicAgentReady())
        {
            navReady = false;
            if (mimicAgent != null)
            {
                mimicAgent.enabled = false;
            }

            return;
        }

        moveTimer -= Time.deltaTime;

        bool arrived = mimicAgent.hasPath && !mimicAgent.pathPending && mimicAgent.remainingDistance <= arrivalDistance;
        bool needsNewDest = moveTimer <= 0f || !hasDestination || arrived;

        if (needsNewDest)
        {
            moveTimer = Mathf.Max(0.5f, retargetInterval + UnityEngine.Random.Range(-retargetJitter, retargetJitter));
            PickMimicDestination();
        }

        DetectMimicStuck();
    }

    private void PickMimicDestination()
    {
        if (!TryGetRoomGrid(out RoomGridGenerator grid))
        {
            TryWanderAroundCurrentPosition();
            return;
        }

        Transform player = FindClosestPlayer();

        // Hard rule: immediately leave player's room if inside it
        if (player != null && IsInPlayerRoom(grid, player))
        {
            TryEscapePlayerRoom(grid, player);
            return;
        }

        if (player != null)
        {
            float roll = UnityEngine.Random.value;

            if (roll < randomRoomMoveChance)
            {
                // Occasionally wander to a random room far from player
                if (TryMoveToRandomNonPlayerRoom(grid, player))
                {
                    return;
                }
            }
            else if (roll < randomRoomMoveChance + 0.4f)
            {
                // Linger near player by targeting a room adjacent to theirs
                if (TryMoveToRoomAdjacentToPlayer(grid, player))
                {
                    return;
                }
            }
        }

        // Default: walk randomly within current room
        TryWanderInCurrentRoom(grid);
    }

    private bool IsInPlayerRoom(RoomGridGenerator grid, Transform player)
    {
        if (!grid.TryGetNearestRoomIndex(player.position, out int playerRow, out int playerCol))
        {
            return false;
        }

        if (!grid.TryGetNearestRoomIndex(transform.position, out int myRow, out int myCol))
        {
            return false;
        }

        return myRow == playerRow && myCol == playerCol;
    }

    private void TryEscapePlayerRoom(RoomGridGenerator grid, Transform player)
    {
        if (!grid.TryGetNearestRoomIndex(player.position, out int playerRow, out int playerCol))
        {
            return;
        }

        int[] dRows = { -1, 1, 0, 0 };
        int[] dCols = { 0, 0, -1, 1 };

        for (int i = 0; i < 4; i++)
        {
            int r = playerRow + dRows[i];
            int c = playerCol + dCols[i];

            if (!grid.IsValidRoomIndex(r, c))
            {
                continue;
            }

            Vector3 center = grid.GetSlotPosition(r, c);
            if (TryMoveToRoomPoint(grid, center))
            {
                return;
            }
        }
    }

    private bool TryMoveToRoomAdjacentToPlayer(RoomGridGenerator grid, Transform player)
    {
        if (!grid.TryGetNearestRoomIndex(player.position, out int playerRow, out int playerCol))
        {
            return false;
        }

        int bestRow = -1;
        int bestCol = -1;
        float bestDist = float.PositiveInfinity;

        int[] dRows = { -1, 1, 0, 0 };
        int[] dCols = { 0, 0, -1, 1 };

        for (int i = 0; i < 4; i++)
        {
            int adjRow = playerRow + dRows[i];
            int adjCol = playerCol + dCols[i];

            if (!grid.IsValidRoomIndex(adjRow, adjCol))
            {
                continue;
            }

            Vector3 center = grid.GetSlotPosition(adjRow, adjCol);
            float dist = (center - transform.position).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestRow = adjRow;
                bestCol = adjCol;
            }
        }

        if (bestRow < 0)
        {
            return false;
        }

        return TryMoveToRoomPoint(grid, grid.GetSlotPosition(bestRow, bestCol));
    }

    private bool TryMoveToRandomNonPlayerRoom(RoomGridGenerator grid, Transform player)
    {
        if (!grid.TryGetNearestRoomIndex(player.position, out int playerRow, out int playerCol))
        {
            return false;
        }

        grid.TryGetNearestRoomIndex(transform.position, out int currentRow, out int currentCol);

        for (int i = 0; i < destinationProbeAttempts; i++)
        {
            int row = UnityEngine.Random.Range(0, RoomGridGenerator.GridDimension);
            int col = UnityEngine.Random.Range(0, RoomGridGenerator.GridDimension);

            if (row == playerRow && col == playerCol)
            {
                continue;
            }

            if (row == currentRow && col == currentCol)
            {
                continue;
            }

            if (TryMoveToRoomPoint(grid, grid.GetSlotPosition(row, col)))
            {
                return true;
            }
        }

        return false;
    }

    private void TryWanderInCurrentRoom(RoomGridGenerator grid)
    {
        grid.TryGetNearestRoomIndex(transform.position, out int currentRow, out int currentCol);
        Vector3 roomCenter = grid.GetSlotPosition(currentRow, currentCol);
        TryMoveToRoomPoint(grid, roomCenter);
    }

    private void TryWanderAroundCurrentPosition()
    {
        float radius = Mathf.Max(1f, roomRoamRadius);
        for (int i = 0; i < destinationProbeAttempts; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
            Vector3 candidate = transform.position + new Vector3(offset.x, offset.y, 0f);
            if (TryGetNavMeshPoint(candidate, 2f, out Vector3 navPoint) && TrySetMimicDestination(navPoint))
            {
                return;
            }
        }
    }

    private bool TryMoveToRoomPoint(RoomGridGenerator grid, Vector3 roomCenter)
    {
        float sampleRadius = GetRoomSampleRadius(grid);

        for (int i = 0; i < destinationProbeAttempts; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * sampleRadius;
            Vector3 candidate = roomCenter + new Vector3(offset.x, offset.y, 0f);

            if (TryGetNavMeshPoint(candidate, Mathf.Min(2f, sampleRadius), out Vector3 navPoint) &&
                TrySetMimicDestination(navPoint))
            {
                return true;
            }
        }

        if (TryGetNavMeshPoint(roomCenter, sampleRadius, out Vector3 centerNavPoint))
        {
            return TrySetMimicDestination(centerNavPoint);
        }

        return false;
    }

    private bool TrySetMimicDestination(Vector3 destination)
    {
        if (!IsMimicAgentReady())
        {
            return false;
        }

        // Validate the destination is on the NavMesh before asking the agent to go there
        if (!NavMesh.SamplePosition(destination, out NavMeshHit hit, 3f, GetWalkableAreaMask()))
        {
            return false;
        }

        mimicAgent.SetDestination(hit.position);
        moveDestination = hit.position;
        hasDestination = true;
        return true;
    }

    private bool TryGetNavMeshPoint(Vector3 position, float sampleRadius, out Vector3 navPoint)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, sampleRadius, GetWalkableAreaMask()))
        {
            navPoint = hit.position;
            return true;
        }

        navPoint = position;
        return false;
    }

    private void TryInitNavigation()
    {
        if (mimicAgent == null)
        {
            return;
        }

        mimicAgent.areaMask = GetWalkableAreaMask();

        if (mimicAgent.enabled && mimicAgent.isOnNavMesh)
        {
            navReady = true;
            reportedNavFailure = false;
            return;
        }

        // Try current position first
        if (TrySampleAndWarpMimic(transform.position, navMeshSearchRadius))
        {
            navReady = true;
            reportedNavFailure = false;
            Debug.Log("MimicEnemy: Placed on NavMesh at current position.");
            return;
        }

        // Try player spawn position
        if (RoomGridGenerator.TryGetPlayerSpawnPosition(out Vector3 spawnPos) &&
            TrySampleAndWarpMimic(spawnPos, navMeshSearchRadius))
        {
            navReady = true;
            reportedNavFailure = false;
            Debug.Log("MimicEnemy: Placed on NavMesh near player spawn.");
            return;
        }

        // Try every room slot as last resort
        if (TryPlaceNearAnyRoom())
        {
            navReady = true;
            reportedNavFailure = false;
            Debug.Log("MimicEnemy: Placed on NavMesh via room scan.");
            return;
        }

        if (!reportedNavFailure && Time.time - navStartTime >= navMeshPlacementTimeout)
        {
            Debug.LogWarning("MimicEnemy: Could not place on NavMesh after timeout. NavMeshSurface may not have built yet.");
            reportedNavFailure = true;
        }
    }

    private bool TryPlaceNearAnyRoom()
    {
        if (!TryGetRoomGrid(out RoomGridGenerator grid))
        {
            return false;
        }

        for (int row = 0; row < RoomGridGenerator.GridDimension; row++)
        {
            for (int col = 0; col < RoomGridGenerator.GridDimension; col++)
            {
                Vector3 slotPos = grid.GetSlotPosition(row, col);
                if (TrySampleAndWarpMimic(slotPos, navMeshSearchRadius))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TrySampleAndWarpMimic(Vector3 position, float radius)
    {
        if (!NavMesh.SamplePosition(position, out NavMeshHit hit, radius, GetWalkableAreaMask()))
        {
            return false;
        }

        // Move transform close to the target first — Warp succeeds more reliably this way
        transform.position = hit.position;

        if (!mimicAgent.enabled)
        {
            mimicAgent.enabled = true;
        }

        mimicAgent.areaMask = GetWalkableAreaMask();

        if (!mimicAgent.Warp(hit.position))
        {
            mimicAgent.enabled = false;
            return false;
        }

        lastMovePosition = hit.position;
        return true;
    }

    private bool IsMimicAgentReady()
    {
        return mimicAgent != null && mimicAgent.enabled && mimicAgent.isOnNavMesh;
    }

    private Transform FindClosestPlayer()
    {
#if UNITY_2023_1_OR_NEWER
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
#else
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
#endif
        Transform closest = null;
        float closestSqr = float.PositiveInfinity;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerMovement player = players[i];
            if (player == null || !player.isActiveAndEnabled || !player.gameObject.scene.IsValid())
            {
                continue;
            }

            float sqr = (player.transform.position - transform.position).sqrMagnitude;
            if (sqr < closestSqr)
            {
                closestSqr = sqr;
                closest = player.transform;
            }
        }

        return closest;
    }

    private void DetectMimicStuck()
    {
        if (mimicAgent == null || mimicAgent.pathPending || !hasDestination)
        {
            lastMovePosition = transform.position;
            stuckTimer = 0f;
            return;
        }

        float moved = Vector3.Distance(transform.position, lastMovePosition);
        lastMovePosition = transform.position;

        if (mimicAgent.remainingDistance <= arrivalDistance || moved > 0.02f)
        {
            stuckTimer = 0f;
            return;
        }

        stuckTimer += Time.deltaTime;
        if (stuckTimer >= stuckRepathTime)
        {
            stuckTimer = 0f;
            hasDestination = false;
            mimicAgent.ResetPath();
            moveTimer = 0f;
        }
    }

    private bool TryGetRoomGrid(out RoomGridGenerator grid)
    {
        if (roomGrid != null && roomGrid.gameObject.scene.IsValid())
        {
            grid = roomGrid;
            return true;
        }

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

            roomGrid = generator;
            grid = generator;
            return true;
        }

        grid = null;
        return false;
    }

    private float GetRoomSampleRadius(RoomGridGenerator grid)
    {
        if (grid == null)
        {
            return Mathf.Max(1f, roomRoamRadius);
        }

        float roomRadius = Mathf.Min(grid.roomSize.x, grid.roomSize.y) * 0.42f;
        return Mathf.Clamp(roomCenterSampleRadius, 1f, roomRadius);
    }

    private static int GetWalkableAreaMask()
    {
        return NavMesh.AllAreas & ~(1 << NotWalkableArea);
    }

    // ── Voice & Disguise ──────────────────────────────────────────────────────

    private void EnsureVoiceSource()
    {
        if (voiceSource == null)
        {
            voiceSource = GetComponent<AudioSource>();
        }

        if (voiceSource == null)
        {
            voiceSource = gameObject.AddComponent<AudioSource>();
        }

        voiceSource.playOnAwake = false;
        voiceSource.loop = false;
        voiceSource.spatialBlend = enableSpatialAudio ? voiceSpatialBlend : 0f;
        voiceSource.rolloffMode = spatialRolloffMode;
        voiceSource.minDistance = Mathf.Max(0.01f, spatialMinDistance);
        voiceSource.maxDistance = Mathf.Max(voiceSource.minDistance + 0.01f, spatialMaxDistance);
        voiceSource.dopplerLevel = 0f;
        voiceSource.spread = 0f;
    }

    private void EnsureDisguise()
    {
        if (applyPlayerScaleOnAwake)
        {
            transform.localScale = playerFormScale;
        }

        GameObject playerPrefab = Resources.Load<GameObject>(playerPrefabResourceName);
        SpriteRenderer prefabBody = FindNamedRenderer(playerPrefab, "Body");
        SpriteRenderer prefabHead = FindNamedRenderer(playerPrefab, "Head");

        bodyRenderer = EnsureRenderer("Body", prefabBody, Vector3.zero, sortingOrderBase);
        headRenderer = EnsureRenderer("Head", prefabHead, new Vector3(0f, talkingHeadMinY, 0f), sortingOrderBase + 1);
        headTransform = headRenderer != null ? headRenderer.transform : null;

        ApplyPlayerVariant();
        EnsureMouthTransform();

        if (headTransform != null)
        {
            baseHeadLocalPosition = headTransform.localPosition;
            baseHeadLocalEuler = headTransform.localEulerAngles;
        }
    }

    private static SpriteRenderer FindNamedRenderer(GameObject root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        Transform child = root.transform.Find(childName);
        return child != null ? child.GetComponent<SpriteRenderer>() : null;
    }

    private SpriteRenderer EnsureRenderer(string childName, SpriteRenderer prefabRenderer, Vector3 fallbackLocalPosition, int sortingOrder)
    {
        Transform child = transform.Find(childName);
        if (child == null)
        {
            GameObject childObject = new GameObject(childName);
            childObject.layer = gameObject.layer;
            childObject.transform.SetParent(transform, false);
            childObject.transform.localPosition = prefabRenderer != null ? prefabRenderer.transform.localPosition : fallbackLocalPosition;
            child = childObject.transform;
        }

        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null)
        {
            renderer = child.gameObject.AddComponent<SpriteRenderer>();
        }

        if (prefabRenderer != null)
        {
            renderer.sprite = prefabRenderer.sprite;
            renderer.sharedMaterial = prefabRenderer.sharedMaterial;
            renderer.color = prefabRenderer.color;
            renderer.sortingLayerID = prefabRenderer.sortingLayerID;
            renderer.flipX = prefabRenderer.flipX;
            renderer.flipY = prefabRenderer.flipY;
        }
        else
        {
            renderer.color = Color.white;
        }

        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    private void ApplyPlayerVariant()
    {
        if (bodyRenderer == null || headRenderer == null || bodyRenderer.sprite == null || headRenderer.sprite == null)
        {
            return;
        }

        // Pick a random player variant (1-4) just like a real player — no tint applied
        int variant = (randomizePlayerVariant || playerVariant <= 0)
            ? UnityEngine.Random.Range(1, PlayerVariantCount + 1)
            : playerVariant;
        variant = ((variant - 1) % PlayerVariantCount) + 1;
        int variantIndex = variant - 1;

        Texture2D texture = bodyRenderer.sprite.texture;
        if (texture == null) return;

        float bodyPPU = bodyRenderer.sprite.pixelsPerUnit;
        float headPPU = headRenderer.sprite.pixelsPerUnit;
        bodyRenderer.sprite = CreatePlayerVariantSprite(texture, PlayerBodyY, PlayerBodyWidth, PlayerBodyHeight, bodyPPU, variantIndex, "MimicBody");
        headRenderer.sprite = CreatePlayerVariantSprite(texture, PlayerHeadY, PlayerHeadWidth, PlayerHeadHeight, headPPU, variantIndex, "MimicHead");

        // Use the player's colours exactly — no tint
        bodyRenderer.color = Color.white;
        headRenderer.color = Color.white;
    }

    private static Sprite CreatePlayerVariantSprite(Texture2D texture, float y, float width, float height, float pixelsPerUnit, int variantIndex, string spriteNamePrefix)
    {
        Rect rect = new Rect(PlayerSpriteStartX + PlayerSpriteStrideX * variantIndex, y, width, height);
        Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit);
        sprite.name = spriteNamePrefix + "_" + (variantIndex + 1);
        return sprite;
    }

    private void EnsureMouthTransform()
    {
        if (headTransform == null)
        {
            return;
        }

        Transform foundMouth = headTransform.Find("Mouth");
        if (foundMouth == null)
        {
            GameObject mouthObject = new GameObject("Mouth");
            mouthObject.transform.SetParent(headTransform, false);
            mouthObject.transform.localPosition = generatedMouthLocalPosition;
            mouthObject.transform.localScale = generatedMouthLocalScale;

            SpriteRenderer mouthRenderer = mouthObject.AddComponent<SpriteRenderer>();
            mouthRenderer.sprite = GetGeneratedMouthSprite();
            mouthRenderer.color = Color.black;
            if (headRenderer != null)
            {
                mouthRenderer.sortingLayerID = headRenderer.sortingLayerID;
                mouthRenderer.sortingOrder = headRenderer.sortingOrder + 1;
            }

            foundMouth = mouthObject.transform;
        }

        mouthTransform = foundMouth;
        baseMouthLocalScale = mouthTransform.localScale;
    }

    private static Sprite GetGeneratedMouthSprite()
    {
        if (generatedMouthSprite != null)
        {
            return generatedMouthSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Point;
        generatedMouthSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return generatedMouthSprite;
    }

    // ── Chat / Speech ──────────────────────────────────────────────────────────

    private void TrySpeakFromChatLog()
    {
        List<ChatLogRecorder.ChatLogMessage> messages = ChatLogRecorder.ReadMessagesSnapshot();
        List<string> playerMessages = new List<string>();

        for (int i = 0; i < messages.Count; i++)
        {
            ChatLogRecorder.ChatLogMessage entry = messages[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.message))
            {
                continue;
            }

            if (string.Equals(entry.sender, "MIMIC", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            playerMessages.Add(entry.message);
        }

        if (playerMessages.Count < minPlayerMessages)
        {
            return;
        }

        bool hasNewChat = playerMessages.Count != lastObservedMessageCount;
        lastObservedMessageCount = playerMessages.Count;

        if (!hasNewChat && UnityEngine.Random.value > speakWithoutNewChatChance)
        {
            return;
        }

        string line = BuildMimicLine(playerMessages);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (string.Equals(line, lastSpokenLine, StringComparison.OrdinalIgnoreCase))
        {
            line = BuildMimicLine(playerMessages);
        }

        Speak(line);
    }

    private string BuildMimicLine(List<string> playerMessages)
    {
        if (playerMessages == null || playerMessages.Count == 0)
        {
            return string.Empty;
        }

        int recentStart = Mathf.Max(0, playerMessages.Count - 20);
        string seed = playerMessages[UnityEngine.Random.Range(recentStart, playerMessages.Count)];
        List<string> seedWords = SplitWords(seed);

        if (seedWords.Count < 2 && playerMessages.Count > 1)
        {
            string secondSeed = playerMessages[UnityEngine.Random.Range(recentStart, playerMessages.Count)];
            seedWords.AddRange(SplitWords(secondSeed));
        }

        if (seedWords.Count == 0)
        {
            return string.Empty;
        }

        List<string> words = PickFragment(seedWords);
        MaybeBlendWithAnotherMessage(words, playerMessages, recentStart);
        MutatePronouns(words);
        MaybeAddWhisper(words);
        TrimWordCount(words, Mathf.Clamp(maxSpeechWords, 3, 20));

        string line = string.Join(" ", words).Trim();
        if (line.Length == 0)
        {
            return string.Empty;
        }

        line = line.ToLowerInvariant();
        if (recentMimicLines.Contains(line))
        {
            line = MakeFallbackLine(playerMessages);
        }

        RememberLine(line);
        return line;
    }

    private List<string> PickFragment(List<string> sourceWords)
    {
        int targetMax = Mathf.Clamp(maxSpeechWords, 3, 20);
        int minimum = Mathf.Min(sourceWords.Count, Mathf.Max(2, Mathf.Min(4, targetMax)));
        int maximum = Mathf.Min(sourceWords.Count, targetMax);
        int length = UnityEngine.Random.Range(minimum, maximum + 1);
        int start = sourceWords.Count > length ? UnityEngine.Random.Range(0, sourceWords.Count - length + 1) : 0;

        List<string> fragment = new List<string>();
        for (int i = 0; i < length; i++)
        {
            fragment.Add(sourceWords[start + i]);
        }

        return fragment;
    }

    private void MaybeBlendWithAnotherMessage(List<string> words, List<string> playerMessages, int recentStart)
    {
        if (words.Count >= maxSpeechWords || playerMessages.Count < 2 || UnityEngine.Random.value > 0.45f)
        {
            return;
        }

        string other = playerMessages[UnityEngine.Random.Range(recentStart, playerMessages.Count)];
        List<string> otherWords = SplitWords(other);
        if (otherWords.Count == 0)
        {
            return;
        }

        int remaining = Mathf.Max(0, maxSpeechWords - words.Count);
        int take = Mathf.Min(remaining, UnityEngine.Random.Range(1, Mathf.Min(4, otherWords.Count) + 1));
        int start = otherWords.Count > take ? UnityEngine.Random.Range(0, otherWords.Count - take + 1) : 0;

        if (UnityEngine.Random.value < 0.5f)
        {
            words.Add("and");
        }

        for (int i = 0; i < take && words.Count < maxSpeechWords; i++)
        {
            words.Add(otherWords[start + i]);
        }
    }

    private static void MutatePronouns(List<string> words)
    {
        for (int i = 0; i < words.Count; i++)
        {
            string word = words[i];
            if (word == "i" && UnityEngine.Random.value < 0.45f)
            {
                words[i] = "you";
            }
            else if (word == "me" && UnityEngine.Random.value < 0.45f)
            {
                words[i] = "you";
            }
            else if (word == "my" && UnityEngine.Random.value < 0.45f)
            {
                words[i] = "your";
            }
            else if (word == "we" && UnityEngine.Random.value < 0.35f)
            {
                words[i] = "you";
            }
            else if (word == "our" && UnityEngine.Random.value < 0.35f)
            {
                words[i] = "your";
            }
        }
    }

    private static void MaybeAddWhisper(List<string> words)
    {
        if (words.Count == 0)
        {
            return;
        }

        float roll = UnityEngine.Random.value;
        if (roll < 0.18f)
        {
            words.Insert(0, "wait");
        }
        else if (roll < 0.28f)
        {
            words.Insert(0, "no");
        }
        else if (roll < 0.36f)
        {
            words.Add("again");
        }
    }

    private static void TrimWordCount(List<string> words, int maxWords)
    {
        while (words.Count > maxWords)
        {
            words.RemoveAt(words.Count - 1);
        }
    }

    private string MakeFallbackLine(List<string> playerMessages)
    {
        string seed = playerMessages[UnityEngine.Random.Range(0, playerMessages.Count)];
        List<string> words = SplitWords(seed);
        if (words.Count == 0)
        {
            return "i heard you";
        }

        int take = Mathf.Min(words.Count, Mathf.Clamp(maxSpeechWords - 2, 1, 10));
        List<string> fallback = new List<string> { "i", "heard" };
        for (int i = 0; i < take; i++)
        {
            fallback.Add(words[i]);
        }

        TrimWordCount(fallback, Mathf.Clamp(maxSpeechWords, 3, 20));
        return string.Join(" ", fallback).ToLowerInvariant();
    }

    private void RememberLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        recentMimicLines.Add(line);
        while (recentMimicLines.Count > 8)
        {
            recentMimicLines.RemoveAt(0);
        }

        lastSpokenLine = line;
    }

    private static List<string> SplitWords(string text)
    {
        List<string> words = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return words;
        }

        StringBuilder current = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = char.ToLowerInvariant(text[i]);
            if (char.IsLetterOrDigit(c) || c == '\'')
            {
                current.Append(c);
                continue;
            }

            AddCurrentWord(words, current);
        }

        AddCurrentWord(words, current);
        return words;
    }

    private static void AddCurrentWord(List<string> words, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        string word = current.ToString().Trim('\'');
        if (!string.IsNullOrWhiteSpace(word))
        {
            words.Add(word);
        }

        current.Length = 0;
    }

    private void Speak(string line)
    {
        line = SanitizeForSpeech(line);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        AudioClip clip = UnitySAMWrapper.GenerateClipFromText(line);
        if (clip == null)
        {
            Debug.LogWarning("MIMIC: Could not generate SAM speech for '" + line + "'.");
            return;
        }

        EnsureVoiceSource();
        voiceSource.PlayOneShot(clip);
        ShowSpeechText(line);
        Debug.Log("MIMIC: " + line);
    }

    private static string SanitizeForSpeech(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        StringBuilder cleaned = new StringBuilder(line.Length);
        bool previousWasSpace = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = char.ToLowerInvariant(line[i]);
            bool keep = char.IsLetterOrDigit(c) || c == '\'' || c == ' ';
            if (!keep)
            {
                continue;
            }

            if (c == ' ')
            {
                if (previousWasSpace)
                {
                    continue;
                }

                previousWasSpace = true;
            }
            else
            {
                previousWasSpace = false;
            }

            cleaned.Append(c);
        }

        return cleaned.ToString().Trim();
    }

    // ── Speech UI ─────────────────────────────────────────────────────────────

    private void ShowSpeechText(string message)
    {
        EnsureSpeechText();
        if (speechText == null)
        {
            return;
        }

        speechText.text = message;
        speechText.gameObject.SetActive(true);
        UpdateSpeechTextPosition();
    }

    private void EnsureSpeechText()
    {
        if (speechText != null)
        {
            return;
        }

        if (speechCanvas == null)
        {
            speechCanvas = FindSpeechCanvas();
        }

        if (speechCanvas == null)
        {
            return;
        }

        GameObject textObject = new GameObject("MIMIC Speech Text", typeof(RectTransform));
        textObject.transform.SetParent(speechCanvas.transform, false);
        textObject.transform.SetAsLastSibling();

        speechTextRect = textObject.GetComponent<RectTransform>();
        speechTextRect.sizeDelta = speechTextSize;

        speechText = textObject.AddComponent<TextMeshProUGUI>();
        if (speechTextFont == null)
        {
            speechTextFont = Resources.Load<TMP_FontAsset>(speechTextFontResourcePath);
        }

        if (speechTextFont != null)
        {
            speechText.font = speechTextFont;
        }

        speechText.alignment = TextAlignmentOptions.Center;
        speechText.color = Color.white;
        speechText.fontSize = speechTextFontSize;
        speechText.fontStyle = FontStyles.Bold;
        speechText.textWrappingMode = TextWrappingModes.Normal;
        speechText.overflowMode = TextOverflowModes.Ellipsis;
        speechText.raycastTarget = false;
        speechText.outlineColor = new Color(0f, 0f, 0f, 0.9f);
        speechText.outlineWidth = 0.18f;
        speechText.gameObject.SetActive(false);
    }

    private static Canvas FindSpeechCanvas()
    {
#if UNITY_2023_1_OR_NEWER
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
#else
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
#endif

        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].gameObject.scene.IsValid())
            {
                return canvases[i];
            }
        }

        GameObject canvasObject = new GameObject("Speech Canvas");
        Canvas createdCanvas = canvasObject.AddComponent<Canvas>();
        createdCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        return createdCanvas;
    }

    private void UpdateSpeechTextPosition()
    {
        if (speechText == null || !speechText.gameObject.activeSelf)
        {
            return;
        }

        if (voiceSource == null || !voiceSource.isPlaying)
        {
            DeleteSpeechText();
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null || speechCanvas == null || speechTextRect == null)
        {
            return;
        }

        Vector2 screenPosition = GetSpeechScreenPosition(mainCamera);
        speechTextRect.pivot = new Vector2(0.5f, 0.5f);

        if (speechCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            speechTextRect.position = screenPosition;
            return;
        }

        RectTransform canvasRect = speechCanvas.transform as RectTransform;
        if (canvasRect == null)
        {
            return;
        }

        Camera canvasCamera = speechCanvas.renderMode == RenderMode.ScreenSpaceCamera ? speechCanvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, canvasCamera, out Vector2 localPoint))
        {
            speechTextRect.anchoredPosition = localPoint;
        }
    }

    private Vector2 GetSpeechScreenPosition(Camera cameraForPosition)
    {
        Vector3 worldPosition = transform.position + Vector3.up * speechTextWorldYOffset;
        Vector3 screenPosition = cameraForPosition.WorldToScreenPoint(worldPosition);
        return new Vector2(screenPosition.x, screenPosition.y);
    }

    private void DeleteSpeechText()
    {
        if (speechText == null)
        {
            return;
        }

        GameObject speechTextObject = speechText.gameObject;
        speechText = null;
        speechTextRect = null;
        Destroy(speechTextObject);
    }

    // ── Head / Mouth Animation ────────────────────────────────────────────────

    private void UpdateHeadFromAudio()
    {
        if (headTransform == null)
        {
            return;
        }

        float targetY = baseHeadLocalPosition.y;
        float normalizedLevel = 0f;
        bool isSpeaking = voiceSource != null && voiceSource.isPlaying;

        if (isSpeaking)
        {
            voiceSource.GetOutputData(audioSamples, 0);

            float sumSquares = 0f;
            for (int sampleIndex = 0; sampleIndex < audioSamples.Length; sampleIndex++)
            {
                float sample = audioSamples[sampleIndex];
                sumSquares += sample * sample;
            }

            float rms = Mathf.Sqrt(sumSquares / audioSamples.Length);
            normalizedLevel = Mathf.Clamp01(rms * audioToHeadScale);
            targetY = Mathf.Lerp(talkingHeadMinY, talkingHeadMaxY, normalizedLevel);
        }

        Vector3 nextLocalPosition = headTransform.localPosition;
        nextLocalPosition.y = Mathf.Lerp(nextLocalPosition.y, targetY, talkingHeadResponse * Time.deltaTime);
        headTransform.localPosition = nextLocalPosition;

        if (isSpeaking)
        {
            tiltRetargetTimer -= Time.deltaTime;
            if (tiltRetargetTimer <= 0f)
            {
                tiltRetargetTimer = UnityEngine.Random.Range(talkingTiltRetargetInterval.x, talkingTiltRetargetInterval.y);
                float levelScaledTilt = Mathf.Max(0.2f, normalizedLevel) * talkingTiltMaxDegrees;
                targetTiltDegrees = UnityEngine.Random.Range(-levelScaledTilt, levelScaledTilt);
            }
        }
        else
        {
            targetTiltDegrees = 0f;
            tiltRetargetTimer = 0f;
        }

        currentTiltDegrees = Mathf.Lerp(currentTiltDegrees, targetTiltDegrees, talkingTiltResponse * Time.deltaTime);
        Vector3 targetEuler = baseHeadLocalEuler;
        targetEuler.z += currentTiltDegrees;
        headTransform.localRotation = Quaternion.Euler(targetEuler);

        UpdateMouthFromAudio(isSpeaking, normalizedLevel);
    }

    private void UpdateMouthFromAudio(bool isSpeaking, float normalizedLevel)
    {
        if (mouthTransform == null)
        {
            return;
        }

        Vector3 targetScale = baseMouthLocalScale;
        if (isSpeaking)
        {
            targetScale.y = baseMouthLocalScale.y * Mathf.Lerp(1f, mouthOpenScale, normalizedLevel);
        }

        mouthTransform.localScale = Vector3.Lerp(mouthTransform.localScale, targetScale, mouthResponse * Time.deltaTime);
    }

    // ── Death on Touch ────────────────────────────────────────────────────────

    private void CheckMimicTouch()
    {
        if (deathTriggered) return;

#if UNITY_2023_1_OR_NEWER
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
#else
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
#endif
        foreach (PlayerMovement player in players)
        {
            if (player == null || !player.isActiveAndEnabled) continue;

            PhotonView pv = player.GetComponent<PhotonView>();
            if (pv != null && !pv.IsMine) continue;

            float dist = Vector2.Distance(transform.position, player.transform.position);
            if (dist <= touchKillRadius)
            {
                deathTriggered = true;
                GameManager.TriggerDeath();
                return;
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null || deathTriggered) return;

        PlayerMovement player = other.GetComponent<PlayerMovement>()
                             ?? other.GetComponentInParent<PlayerMovement>();
        if (player == null || !player.isActiveAndEnabled) return;

        PhotonView pv = player.GetComponent<PhotonView>();
        if (pv != null && !pv.IsMine) return;

        deathTriggered = true;
        GameManager.TriggerDeath();
    }
}
