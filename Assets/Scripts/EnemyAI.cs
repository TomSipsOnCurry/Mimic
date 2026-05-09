using UnityEngine;
using UnityEngine.AI;
using Photon.Pun;

public class EnemyAI : MonoBehaviour
{
    private enum EnemyState
    {
        Wander,
        Flee,
        Chase
    }

    [Header("Movement")]
    [SerializeField] private float wanderSpeed = 5f;
    [SerializeField] private float fleeSpeed = 12f;
    [SerializeField] private float chaseSpeed = 10f;
    [SerializeField] private float acceleration = 100f;

    [Header("Detection")]
    [SerializeField] private float playerDetectRadius = 15f;
    [SerializeField] private float playerVisionRadius = 15f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask wallLayer;

    [Header("Behaviour")]
    [SerializeField] private int sightingsBeforeAttack = 3;
    [SerializeField] private float fleeDistance = 14f;
    [SerializeField] private float wanderRadius = 25f;
    [SerializeField] private float wanderRetargetTime = 3f;

    [Header("Kill")]
    [SerializeField] private float killDistance = 1f;

    [Header("NavMesh")]
    [SerializeField] private float navMeshSearchRadius = 30f;
    [SerializeField] private float startDelay = 0.5f;

    private EnemyState state = EnemyState.Wander;
    private NavMeshAgent agent;
    private Transform targetPlayer;
    private float timer;
    private int sightingCount;
    private bool navReady;

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

        SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingOrder = 100;
            sr.enabled = true;
        }
    }

    private void Start()
    {
        Invoke(nameof(InitialiseAfterNavMeshBuild), startDelay);
    }

    private void InitialiseAfterNavMeshBuild()
    {
        navReady = TryPlaceOnNavMesh();

        if (!navReady)
        {
            Debug.LogError("EnemyAI: Enemy could not be placed on NavMesh. Check NavMeshSurface baking.");
            return;
        }

        PickRandomWanderPoint();
    }

    private void Update()
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        if (!navReady)
        {
            navReady = TryPlaceOnNavMesh();
            return;
        }

        if (!IsAgentReady())
        {
            navReady = false;
            return;
        }

        Transform seeingPlayer = GetPlayerSeeingEnemy();

        if (seeingPlayer != null && state != EnemyState.Chase)
        {
            sightingCount++;

            if (sightingCount >= sightingsBeforeAttack)
            {
                targetPlayer = seeingPlayer;
                state = EnemyState.Chase;
            }
            else
            {
                targetPlayer = seeingPlayer;
                state = EnemyState.Flee;
                FleeFrom(seeingPlayer);
            }
        }

        switch (state)
        {
            case EnemyState.Wander:
                UpdateWander();
                break;

            case EnemyState.Flee:
                UpdateFlee();
                break;

            case EnemyState.Chase:
                UpdateChase();
                break;
        }
    }

    private void UpdateWander()
    {
        agent.speed = wanderSpeed;
        timer -= Time.deltaTime;

        if ((!agent.pathPending && agent.remainingDistance <= 0.5f) || timer <= 0f)
        {
            PickRandomWanderPoint();
        }
    }

    private void UpdateFlee()
    {
        agent.speed = fleeSpeed;

        if (!agent.pathPending && agent.remainingDistance <= 0.5f)
        {
            state = EnemyState.Wander;
            PickRandomWanderPoint();
        }
    }

    private void UpdateChase()
    {
        if (targetPlayer == null)
        {
            state = EnemyState.Wander;
            PickRandomWanderPoint();
            return;
        }

        agent.speed = chaseSpeed;

        if (TryGetNavMeshPoint(targetPlayer.position, out Vector3 targetPosition))
        {
            agent.SetDestination(targetPosition);
        }

        float distance = Vector2.Distance(transform.position, targetPlayer.position);

        if (distance <= killDistance)
        {
            KillPlayer(targetPlayer);
        }
    }

    private Transform GetPlayerSeeingEnemy()
    {
        Collider2D[] players = Physics2D.OverlapCircleAll(
            transform.position,
            playerDetectRadius,
            playerLayer
        );

        foreach (Collider2D player in players)
        {
            if (player == null)
            {
                continue;
            }

            float distance = Vector2.Distance(transform.position, player.transform.position);

            if (distance > playerVisionRadius)
            {
                continue;
            }

            Vector2 directionToEnemy = transform.position - player.transform.position;

            RaycastHit2D wallHit = Physics2D.Raycast(
                player.transform.position,
                directionToEnemy.normalized,
                distance,
                wallLayer
            );

            if (wallHit.collider != null)
            {
                continue;
            }

            return player.transform;
        }

        return null;
    }

    private void PickRandomWanderPoint()
    {
        timer = wanderRetargetTime;

        for (int i = 0; i < 30; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
            Vector3 randomPoint = transform.position + new Vector3(randomCircle.x, randomCircle.y, 0f);

            if (TryGetNavMeshPoint(randomPoint, out Vector3 navPoint))
            {
                agent.SetDestination(navPoint);
                return;
            }
        }

        Debug.LogWarning("EnemyAI: Could not find random NavMesh wander point.");
    }

    private void FleeFrom(Transform player)
    {
        Vector3 awayDirection = transform.position - player.position;

        if (awayDirection.sqrMagnitude < 0.01f)
        {
            Vector2 randomDirection = Random.insideUnitCircle.normalized;
            awayDirection = new Vector3(randomDirection.x, randomDirection.y, 0f);
        }
        else
        {
            awayDirection.Normalize();
        }

        Vector3 desiredFleePoint = transform.position + awayDirection * fleeDistance;

        if (TryGetNavMeshPoint(desiredFleePoint, out Vector3 navPoint))
        {
            agent.speed = fleeSpeed;
            agent.SetDestination(navPoint);
        }
        else
        {
            PickRandomWanderPoint();
        }
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

        if (agent.isOnNavMesh)
        {
            return true;
        }

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshSearchRadius, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
            transform.position = hit.position;
            Debug.Log("EnemyAI: Placed enemy on NavMesh at " + hit.position);
            return true;
        }

        return false;
    }

    private bool TryGetNavMeshPoint(Vector3 position, out Vector3 navPoint)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSearchRadius, NavMesh.AllAreas))
        {
            navPoint = hit.position;
            return true;
        }

        navPoint = position;
        return false;
    }

    private void KillPlayer(Transform player)
    {
        Debug.Log("Enemy killed player: " + player.name);
        player.gameObject.SetActive(false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, playerDetectRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, playerVisionRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, wanderRadius);
    }
}