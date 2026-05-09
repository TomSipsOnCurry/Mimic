using UnityEngine;
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
    [SerializeField] private float wanderSpeed = 2f;
    [SerializeField] private float fleeSpeed = 7f;
    [SerializeField] private float chaseSpeed = 5.5f;

    [Header("Detection")]
    [SerializeField] private float playerDetectRadius = 10f;
    [SerializeField] private float playerVisionRadius = 8f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask wallLayer;

    [Header("Behaviour")]
    [SerializeField] private int sightingsBeforeAttack = 3;
    [SerializeField] private float fleeDistance = 8f;
    [SerializeField] private float fleeTime = 1.5f;
    [SerializeField] private float wanderRetargetTime = 2f;
    [SerializeField] private float wanderDistance = 5f;

    [Header("Kill")]
    [SerializeField] private float killDistance = 0.6f;

    private EnemyState state = EnemyState.Wander;
    private Rigidbody2D rb;
    private Transform targetPlayer;
    private Vector3 moveTarget;
    private float stateTimer;
    private int sightingCount;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void Start()
    {
        PickRandomWanderTarget();
    }

    private void Update()
    {
        // Only host/master controls the enemy in multiplayer
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
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
                StartFleeingFrom(seeingPlayer);
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
        stateTimer -= Time.deltaTime;

        MoveTowards(moveTarget, wanderSpeed);

        if (Vector2.Distance(transform.position, moveTarget) < 0.3f || stateTimer <= 0f)
        {
            PickRandomWanderTarget();
        }
    }

    private void UpdateFlee()
    {
        stateTimer -= Time.deltaTime;

        MoveTowards(moveTarget, fleeSpeed);

        if (stateTimer <= 0f || Vector2.Distance(transform.position, moveTarget) < 0.3f)
        {
            state = EnemyState.Wander;
            PickRandomWanderTarget();
        }
    }

    private void UpdateChase()
    {
        if (targetPlayer == null)
        {
            state = EnemyState.Wander;
            PickRandomWanderTarget();
            return;
        }

        MoveTowards(targetPlayer.position, chaseSpeed);

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

    private void StartFleeingFrom(Transform player)
    {
        state = EnemyState.Flee;
        stateTimer = fleeTime;

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

        moveTarget = transform.position + awayDirection * fleeDistance;
    }

    private void PickRandomWanderTarget()
    {
        stateTimer = wanderRetargetTime;

        Vector2 randomOffset = Random.insideUnitCircle * wanderDistance;
        moveTarget = transform.position + new Vector3(randomOffset.x, randomOffset.y, 0f);
    }

    private void MoveTowards(Vector3 destination, float speed)
    {
        Vector3 nextPosition = Vector3.MoveTowards(
            transform.position,
            destination,
            speed * Time.deltaTime
        );

        if (rb != null)
        {
            rb.MovePosition(nextPosition);
        }
        else
        {
            transform.position = nextPosition;
        }
    }

    private void KillPlayer(Transform player)
    {
        Debug.Log("Enemy killed player: " + player.name);

        // Temporary kill behaviour
        player.gameObject.SetActive(false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, playerDetectRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, playerVisionRadius);
    }
}