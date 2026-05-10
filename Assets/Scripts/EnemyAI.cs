using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;

public class EnemyAI : MonoBehaviour
{
    private const int NotWalkableArea = 1;

    [Header("Movement")]
    [SerializeField] private float wanderSpeed = 5f;
    [SerializeField] private float chaseSpeed = 10f;
    [SerializeField] private float acceleration = 100f;

    [Header("Targeting")]
    [SerializeField] private float playerDetectRadius = 90f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask wallLayer;

    [Header("Lure Behaviour")]
    [SerializeField] private float lureRetargetTime = 3f;
    [SerializeField] private float lureRetargetJitter = 0.75f;
    [SerializeField] private float lureArrivalDistance = 1.25f;
    [SerializeField] private float minimumPlayerScanRadius = 90f;

    [Header("Room Movement")]
    [SerializeField] private float roomRoamRadius = 7f;
    [SerializeField, Range(0f, 1f)] private float randomRoomMoveChance = 0.4f;
    [SerializeField] private int randomRoomMinimumDistanceFromPlayer = 2;
    [SerializeField] private int randomRoomMinimumDistanceFromCurrent = 2;

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 25f;
    [SerializeField] private float wanderRetargetTime = 3f;

    [Header("NavMesh")]
    [SerializeField] private float navMeshSearchRadius = 30f;
    [SerializeField] private float startDelay = 0.5f;
    [SerializeField] private float navMeshRetryInterval = 0.25f;
    [SerializeField] private float navMeshPlacementTimeout = 8f;
    [SerializeField] private float roomCenterSampleRadius = 8f;
    [SerializeField] private int destinationProbeAttempts = 24;

    [Header("Collision Safety")]
    [SerializeField] private float wallClearanceRadius = 0.45f;
    [SerializeField] private float stuckRepathTime = 1.5f;
    [SerializeField] private bool allowUnseenRoomRelocation = true;

    private NavMeshAgent agent;
    private RoomGridGenerator roomGrid;
    private Transform targetPlayer;
    private Vector3 lastPosition;
    private Vector3 currentDestination;
    private float timer;
    private float navMeshStartTime;
    private float navMeshRetryTimer;
    private float stuckTimer;
    private bool navReady;
    private bool reportedNavMeshFailure;
    private bool hasCurrentDestination;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        if (agent == null)
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
        }

        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.speed = wanderSpeed;
        agent.acceleration = acceleration;
        agent.angularSpeed = 0f;
        agent.stoppingDistance = 0.15f;
        agent.autoBraking = false;
        agent.areaMask = GetWalkableAreaMask();
        agent.enabled = false;

        SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingOrder = 100;
            sr.enabled = true;
        }
    }

    private void Start()
    {
        navMeshStartTime = Time.time;
        navMeshRetryTimer = Mathf.Max(0f, startDelay);
        lastPosition = transform.position;
    }

    private void TryInitialiseNavigation()
    {
        if (!TryPlaceOnNavMesh())
        {
            if (!reportedNavMeshFailure && Time.time - navMeshStartTime >= navMeshPlacementTimeout)
            {
                Debug.LogWarning("EnemyAI: Waiting for runtime NavMesh. If this repeats, check the NavMeshSurface on the generated map.");
                reportedNavMeshFailure = true;
            }

            return;
        }

        navReady = true;
        reportedNavMeshFailure = false;
        timer = 0f;
    }

    private void Update()
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        if (!navReady)
        {
            navMeshRetryTimer -= Time.deltaTime;
            if (navMeshRetryTimer <= 0f)
            {
                navMeshRetryTimer = Mathf.Max(0.05f, navMeshRetryInterval);
                TryInitialiseNavigation();
            }

            return;
        }

        if (!IsAgentReady())
        {
            navReady = false;
            if (agent != null)
            {
                agent.enabled = false;
            }

            return;
        }

        ResolveWallOverlap();

        targetPlayer = FindClosestPlayer();
        if (targetPlayer != null)
        {
            UpdateLure();
        }
        else
        {
            HoldPosition();
        }

        DetectStuckMovement();
    }

    private void UpdateLure()
    {
        agent.speed = chaseSpeed;
        timer -= Time.deltaTime;

        bool arrived = agent.hasPath && !agent.pathPending && agent.remainingDistance <= lureArrivalDistance;
        bool needsNewDestination = timer <= 0f || !hasCurrentDestination || arrived;

        if (!needsNewDestination)
        {
            return;
        }

        timer = GetRetargetDelay(lureRetargetTime, lureRetargetJitter);

        if (TryMoveToAdjacentPlayerRoom(targetPlayer))
        {
            return;
        }

        HoldPosition();
    }

    private void UpdateWander()
    {
        agent.speed = wanderSpeed;
        timer -= Time.deltaTime;

        if ((!agent.pathPending && agent.remainingDistance <= 0.5f) || timer <= 0f || !hasCurrentDestination)
        {
            PickRandomWanderPoint();
        }
    }

    private bool TryMoveToAdjacentPlayerRoom(Transform player)
    {
        if (player == null || !TryGetRoomGrid(out RoomGridGenerator grid))
        {
            return false;
        }

        bool chooseRandomRoom = Random.value < randomRoomMoveChance;
        if (chooseRandomRoom && TryGetRandomRoomCenterAwayFromPlayer(grid, player, out Vector3 randomRoomCenter) &&
            TryMoveToRoomPoint(grid, randomRoomCenter))
        {
            return true;
        }

        if (!grid.TryGetAdjacentRoomCenterNear(player.position, transform.position, out Vector3 roomCenter))
        {
            return false;
        }

        return TryMoveToRoomPoint(grid, roomCenter);
    }

    private bool TryMoveToRoomPoint(RoomGridGenerator grid, Vector3 roomCenter)
    {
        float sampleRadius = Mathf.Min(GetRoomDestinationSampleRadius(grid), Mathf.Max(0.5f, roomRoamRadius));
        if (!TryFindRandomSafeNavMeshPointAround(roomCenter, sampleRadius, out Vector3 destination))
        {
            return false;
        }

        if (!TrySetSafeDestination(destination) && !TryRelocateToUnseenRoom(destination))
        {
            return false;
        }

        return true;
    }

    private bool TryGetRandomRoomCenterAwayFromPlayer(RoomGridGenerator grid, Transform player, out Vector3 roomCenter)
    {
        roomCenter = Vector3.zero;
        if (grid == null || player == null ||
            !grid.TryGetNearestRoomIndex(player.position, out int playerRow, out int playerColumn))
        {
            return false;
        }

        grid.TryGetNearestRoomIndex(transform.position, out int currentRow, out int currentColumn);

        for (int i = 0; i < destinationProbeAttempts; i++)
        {
            int row = Random.Range(0, RoomGridGenerator.GridDimension);
            int column = Random.Range(0, RoomGridGenerator.GridDimension);

            if (!IsAllowedRandomRoom(row, column, playerRow, playerColumn, currentRow, currentColumn, true))
            {
                continue;
            }

            roomCenter = grid.GetSlotPosition(row, column);
            return true;
        }

        for (int i = 0; i < destinationProbeAttempts; i++)
        {
            int row = Random.Range(0, RoomGridGenerator.GridDimension);
            int column = Random.Range(0, RoomGridGenerator.GridDimension);

            if (!IsAllowedRandomRoom(row, column, playerRow, playerColumn, currentRow, currentColumn, false))
            {
                continue;
            }

            roomCenter = grid.GetSlotPosition(row, column);
            return true;
        }

        return false;
    }

    private bool IsAllowedRandomRoom(int row, int column, int playerRow, int playerColumn, int currentRow, int currentColumn, bool requireDistance)
    {
        if (row == playerRow && column == playerColumn)
        {
            return false;
        }

        if (!requireDistance)
        {
            return true;
        }

        int distanceFromPlayer = Mathf.Abs(row - playerRow) + Mathf.Abs(column - playerColumn);
        if (distanceFromPlayer < Mathf.Max(1, randomRoomMinimumDistanceFromPlayer))
        {
            return false;
        }

        int distanceFromCurrent = Mathf.Abs(row - currentRow) + Mathf.Abs(column - currentColumn);
        return distanceFromCurrent >= Mathf.Max(1, randomRoomMinimumDistanceFromCurrent);
    }

    private Transform FindClosestPlayer()
    {
        Transform closestPlayer = null;
        float maxDistance = Mathf.Max(playerDetectRadius, minimumPlayerScanRadius);
        float maxDistanceSqr = maxDistance * maxDistance;
        float closestDistanceSqr = float.PositiveInfinity;

#if UNITY_2023_1_OR_NEWER
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
#else
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
#endif

        for (int i = 0; i < players.Length; i++)
        {
            PlayerMovement player = players[i];
            if (player == null || !player.isActiveAndEnabled || !player.gameObject.scene.IsValid())
            {
                continue;
            }

            float distanceSqr = (player.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr > maxDistanceSqr || distanceSqr >= closestDistanceSqr)
            {
                continue;
            }

            closestDistanceSqr = distanceSqr;
            closestPlayer = player.transform;
        }

        if (closestPlayer != null || playerLayer.value == 0)
        {
            return closestPlayer;
        }

        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, maxDistance, playerLayer);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D playerCollider = colliders[i];
            if (playerCollider == null || !playerCollider.gameObject.scene.IsValid())
            {
                continue;
            }

            float distanceSqr = (playerCollider.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr >= closestDistanceSqr)
            {
                continue;
            }

            closestDistanceSqr = distanceSqr;
            closestPlayer = playerCollider.transform;
        }

        return closestPlayer;
    }

    private void PickRandomWanderPoint()
    {
        timer = GetRetargetDelay(wanderRetargetTime, 0.5f);
        hasCurrentDestination = false;

        for (int i = 0; i < destinationProbeAttempts; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
            Vector3 randomPoint = transform.position + new Vector3(randomCircle.x, randomCircle.y, 0f);

            if (TryFindSafeNavMeshPointAround(randomPoint, 2f, out Vector3 navPoint) &&
                TrySetSafeDestination(navPoint))
            {
                currentDestination = navPoint;
                hasCurrentDestination = true;
                return;
            }
        }

        Debug.LogWarning("EnemyAI: Could not find a collider-safe NavMesh wander point.");
    }

    private void HoldPosition()
    {
        timer = 0f;
        hasCurrentDestination = false;
        stuckTimer = 0f;

        if (agent == null)
        {
            return;
        }

        agent.speed = 0f;
        if (agent.hasPath)
        {
            agent.ResetPath();
        }
    }

    private bool TryFindSafeNavMeshPointAround(Vector3 center, float sampleRadius, out Vector3 navPoint)
    {
        sampleRadius = Mathf.Max(0.25f, sampleRadius);

        if (TryGetNavMeshPoint(center, sampleRadius, out navPoint))
        {
            return true;
        }

        for (int i = 0; i < destinationProbeAttempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * sampleRadius;
            Vector3 candidate = center + new Vector3(offset.x, offset.y, 0f);

            if (TryGetNavMeshPoint(candidate, Mathf.Min(2f, sampleRadius), out navPoint))
            {
                return true;
            }
        }

        navPoint = center;
        return false;
    }

    private bool TryFindRandomSafeNavMeshPointAround(Vector3 center, float sampleRadius, out Vector3 navPoint)
    {
        sampleRadius = Mathf.Max(0.25f, sampleRadius);

        for (int i = 0; i < destinationProbeAttempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * sampleRadius;
            Vector3 candidate = center + new Vector3(offset.x, offset.y, 0f);

            if (!TryGetNavMeshPoint(candidate, Mathf.Min(1.5f, sampleRadius), out Vector3 candidateNavPoint))
            {
                continue;
            }

            if (Vector2.Distance(candidateNavPoint, center) > sampleRadius + 1f)
            {
                continue;
            }

            navPoint = candidateNavPoint;
            return true;
        }

        return TryFindSafeNavMeshPointAround(center, sampleRadius, out navPoint);
    }

    private bool TrySetSafeDestination(Vector3 destination)
    {
        if (!IsAgentReady())
        {
            return false;
        }

        if (IsPositionBlockedByWall(destination))
        {
            return false;
        }

        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(destination, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        if (PathCrossesWall(path))
        {
            return false;
        }

        bool accepted = agent.SetPath(path);
        if (accepted)
        {
            currentDestination = destination;
            hasCurrentDestination = true;
        }

        return accepted;
    }

    private bool TryRelocateToUnseenRoom(Vector3 destination)
    {
        if (!allowUnseenRoomRelocation || !IsAgentReady())
        {
            return false;
        }

        if (IsPositionBlockedByWall(destination) || HasPlayerLineOfSight(transform.position) || HasPlayerLineOfSight(destination))
        {
            return false;
        }

        agent.ResetPath();
        bool warped = agent.Warp(destination);
        if (!warped)
        {
            return false;
        }

        transform.position = destination;
        currentDestination = destination;
        hasCurrentDestination = true;
        lastPosition = destination;
        stuckTimer = 0f;
        return true;
    }

    private bool HasPlayerLineOfSight(Vector3 position)
    {
#if UNITY_2023_1_OR_NEWER
        PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
#else
        PlayerMovement[] players = FindObjectsOfType<PlayerMovement>();
#endif

        for (int i = 0; i < players.Length; i++)
        {
            PlayerMovement player = players[i];
            if (player == null || !player.isActiveAndEnabled || !player.gameObject.scene.IsValid())
            {
                continue;
            }

            Vector2 playerPosition = player.transform.position;
            Vector2 targetPosition = position;
            float distance = Vector2.Distance(playerPosition, targetPosition);
            if (distance > minimumPlayerScanRadius)
            {
                continue;
            }

            if (wallLayer.value == 0)
            {
                return true;
            }

            RaycastHit2D wallHit = Physics2D.Linecast(playerPosition, targetPosition, wallLayer);
            if (wallHit.collider == null)
            {
                return true;
            }
        }

        return false;
    }

    private bool PathCrossesWall(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length == 0)
        {
            return true;
        }

        Vector3 previous = transform.position;
        for (int i = 0; i < path.corners.Length; i++)
        {
            Vector3 next = path.corners[i];
            if (SegmentCrossesWall(previous, next))
            {
                return true;
            }

            previous = next;
        }

        return false;
    }

    private bool SegmentCrossesWall(Vector3 start, Vector3 end)
    {
        if (wallLayer.value == 0)
        {
            return false;
        }

        Vector2 start2D = start;
        Vector2 end2D = end;
        Vector2 direction = end2D - start2D;
        float distance = direction.magnitude;

        if (distance <= 0.01f)
        {
            return IsPositionBlockedByWall(start);
        }

        float castRadius = Mathf.Max(0.05f, GetWallClearanceRadius() * 0.65f);
        RaycastHit2D hit = Physics2D.CircleCast(start2D, castRadius, direction.normalized, distance, wallLayer);
        return hit.collider != null;
    }

    private bool IsPositionBlockedByWall(Vector3 position)
    {
        if (wallLayer.value == 0)
        {
            return false;
        }

        return Physics2D.OverlapCircle(position, GetWallClearanceRadius(), wallLayer) != null;
    }

    private void ResolveWallOverlap()
    {
        if (wallLayer.value == 0 || agent == null)
        {
            return;
        }

        Collider2D overlap = Physics2D.OverlapCircle(transform.position, GetWallClearanceRadius(), wallLayer);
        if (overlap == null)
        {
            return;
        }

        Vector2 current = transform.position;
        Vector2 closest = overlap.ClosestPoint(current);
        Vector2 away = current - closest;

        if (away.sqrMagnitude < 0.001f)
        {
            away = agent.velocity.sqrMagnitude > 0.001f ? -(Vector2)agent.velocity.normalized : Random.insideUnitCircle.normalized;
        }

        Vector3 correctedPosition = transform.position + (Vector3)(away.normalized * GetWallClearanceRadius());
        if (TryGetNavMeshPoint(correctedPosition, navMeshSearchRadius, out Vector3 navPoint))
        {
            agent.ResetPath();
            agent.Warp(navPoint);
            transform.position = navPoint;
            hasCurrentDestination = false;
        }
    }

    private void DetectStuckMovement()
    {
        if (agent == null || agent.pathPending || !hasCurrentDestination)
        {
            lastPosition = transform.position;
            stuckTimer = 0f;
            return;
        }

        float movedDistance = Vector3.Distance(transform.position, lastPosition);
        lastPosition = transform.position;

        if (agent.remainingDistance <= lureArrivalDistance || movedDistance > 0.02f)
        {
            stuckTimer = 0f;
            return;
        }

        stuckTimer += Time.deltaTime;
        if (stuckTimer < stuckRepathTime)
        {
            return;
        }

        stuckTimer = 0f;
        hasCurrentDestination = false;
        agent.ResetPath();
        timer = 0f;
    }

    private bool IsAgentReady()
    {
        return agent != null && agent.enabled && agent.isOnNavMesh;
    }

    private bool TryPlaceOnNavMesh()
    {
        if (agent == null)
        {
            return false;
        }

        agent.areaMask = GetWalkableAreaMask();

        if (agent.enabled && agent.isOnNavMesh && !IsPositionBlockedByWall(transform.position))
        {
            return true;
        }

        if (TrySampleAndWarp(transform.position, navMeshSearchRadius))
        {
            return true;
        }

        if (RoomGridGenerator.TryGetPlayerSpawnPosition(out Vector3 playerSpawnPosition) &&
            TrySampleAndWarp(playerSpawnPosition, navMeshSearchRadius))
        {
            return true;
        }

        return TryPlaceNearGeneratedRoom();
    }

    private bool TryGetNavMeshPoint(Vector3 position, float sampleRadius, out Vector3 navPoint)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, sampleRadius, GetWalkableAreaMask()) &&
            !IsPositionBlockedByWall(hit.position))
        {
            navPoint = hit.position;
            return true;
        }

        navPoint = position;
        return false;
    }

    private bool TryPlaceNearGeneratedRoom()
    {
        if (!TryGetRoomGrid(out RoomGridGenerator grid))
        {
            return false;
        }

        for (int row = 0; row < RoomGridGenerator.GridDimension; row++)
        {
            for (int column = 0; column < RoomGridGenerator.GridDimension; column++)
            {
                Vector3 slotPosition = grid.GetSlotPosition(row, column);
                if (TrySampleAndWarp(slotPosition, GetRoomDestinationSampleRadius(grid)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TrySampleAndWarp(Vector3 position, float sampleRadius)
    {
        if (!TryFindSafeNavMeshPointAround(position, sampleRadius, out Vector3 navPoint))
        {
            return false;
        }

        transform.position = navPoint;
        if (!agent.enabled)
        {
            agent.enabled = true;
        }

        agent.areaMask = GetWalkableAreaMask();
        if (agent.Warp(navPoint))
        {
            transform.position = navPoint;
            lastPosition = navPoint;
            hasCurrentDestination = false;
            return true;
        }

        agent.enabled = false;
        return false;
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

    private float GetRoomDestinationSampleRadius(RoomGridGenerator grid)
    {
        if (grid == null)
        {
            return Mathf.Max(1f, roomCenterSampleRadius);
        }

        float roomRadius = Mathf.Min(grid.roomSize.x, grid.roomSize.y) * 0.42f;
        return Mathf.Clamp(roomCenterSampleRadius, 1f, roomRadius);
    }

    private float GetWallClearanceRadius()
    {
        float radius = Mathf.Max(0.05f, wallClearanceRadius);

        if (agent != null)
        {
            radius = Mathf.Max(radius, agent.radius);
        }

        Collider2D bodyCollider = GetComponent<Collider2D>();
        if (bodyCollider != null)
        {
            Vector3 extents = bodyCollider.bounds.extents;
            radius = Mathf.Max(radius, Mathf.Min(extents.x, extents.y));
        }

        return radius;
    }

    private static int GetWalkableAreaMask()
    {
        return NavMesh.AllAreas & ~(1 << NotWalkableArea);
    }

    private static float GetRetargetDelay(float baseDelay, float jitter)
    {
        float delay = Mathf.Max(0.1f, baseDelay);
        if (jitter <= 0f)
        {
            return delay;
        }

        return Mathf.Max(0.1f, delay + Random.Range(-jitter, jitter));
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(playerDetectRadius, minimumPlayerScanRadius));

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, wanderRadius);

        if (hasCurrentDestination)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(currentDestination, 0.75f);
        }
    }
}
