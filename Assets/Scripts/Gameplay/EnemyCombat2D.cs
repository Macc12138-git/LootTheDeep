using System.Collections.Generic;
using LootTheDeep.Inventory;
using UnityEngine;

namespace LootTheDeep.Gameplay
{
    public static class CombatNoise2D
    {
        public static void Emit(Vector2 position, float radius)
        {
            foreach (EnemyCombat2D enemy in Object.FindObjectsByType<EnemyCombat2D>(FindObjectsSortMode.None))
                enemy.Hear(position, radius);
        }
    }

    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(CombatHealth2D))]
    public sealed class EnemyCombat2D : MonoBehaviour
    {
        public enum AttackKind { Ranged, Lunge }
        [SerializeField] private AttackKind attackKind;
        [SerializeField] private ShootingProjectile2D projectilePrefab;
        [SerializeField] private SpriteRenderer warning;
        [SerializeField] private SearchableContainer remains;
        [SerializeField, Min(0.1f)] private float sightRange = 8f;
        [SerializeField, Min(0.1f)] private float moveSpeed = 2f;
        [SerializeField, Min(0.1f)] private float attackRange = 5f;
        [SerializeField, Min(0.1f)] private float windupSeconds = 0.6f;
        [SerializeField, Min(0.1f)] private float attackInterval = 2f;
        [SerializeField, Min(1)] private int damage = 12;
        [SerializeField, Min(0.1f)] private float chaseDistance = 14f;
        [SerializeField, Min(0.1f)] private float investigationSeconds = 8f;
        [SerializeField, Min(0.1f)] private float repathSeconds = .3f;
        [SerializeField] private bool directionalWarning;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;

        private Rigidbody2D body;
        private Collider2D hitbox;
        private CombatHealth2D health;
        private CombatHealth2D player;
        private Collider2D playerHitbox;
        private Vector2 home;
        private Vector2 lastKnown;
        private float alertUntil;
        private float nextAttackAt;
        private float windupRemaining;
        private float lungeRemaining;
        private Vector2 lungeDirection;
        private bool lungeHit;
        private bool playerVisible;
        private Vector2 attackDirection;
        private TilemapPathfinder2D navigation;
        private readonly List<Vector2> path = new();
        private int pathIndex, pathRevision;
        private Vector2 pathGoal, progressPosition;
        private float nextPathAt;
        private bool pathRequested, tryingToMove;

        public bool IsWindingUp => windupRemaining > 0f;
        public Vector2 AttackDirection => attackDirection;

        public void ConfigureNavigation(TilemapPathfinder2D value)
        {
            navigation = value;
            ClearPath();
        }

        public void Configure(AttackKind kind, ShootingProjectile2D shot, SpriteRenderer marker,
            SearchableContainer loot, float range, float speed, float reach, int attackDamage)
        {
            attackKind = kind; projectilePrefab = shot; warning = marker; remains = loot;
            sightRange = range; moveSpeed = speed; attackRange = reach; damage = attackDamage;
        }

        public void ConfigurePresentation(Transform visual, Animator animation, bool showDirection)
        {
            visualRoot = visual;
            animator = animation;
            directionalWarning = showDirection;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            hitbox = GetComponent<Collider2D>();
            health = GetComponent<CombatHealth2D>();
            home = transform.position;
            health.Died += OnDeath;
            if (warning != null) warning.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (navigation == null) navigation = FindFirstObjectByType<TilemapPathfinder2D>();
            var shooter = FindFirstObjectByType<PlayerShooter2D>();
            if (shooter != null)
            {
                player = shooter.GetComponent<CombatHealth2D>();
                playerHitbox = shooter.GetComponent<Collider2D>();
            }
        }

        public void Hear(Vector2 source, float radius)
        {
            if (health.IsDead || Vector2.Distance(transform.position, source) > radius) return;
            if (Vector2.Distance(home, source) > chaseDistance) return;
            lastKnown = source;
            alertUntil = Time.time + investigationSeconds;
        }

        private bool CanSeePlayer()
        {
            if (player == null || player.IsDead ||
                Vector2.Distance(transform.position, player.transform.position) > sightRange) return false;
            Vector2 origin = transform.position;
            Vector2 target = player.transform.position;
            foreach (RaycastHit2D hit in Physics2D.LinecastAll(origin, target))
            {
                if (hit.collider.isTrigger || hit.collider.transform.IsChildOf(transform)) continue;
                return hit.collider.GetComponentInParent<CombatHealth2D>() == player;
            }
            return false;
        }

        private void Update()
        {
            // Update still runs at timeScale zero; do not start attacks or apply lunge contact damage.
            if (Time.timeScale == 0f) return;
            if (health.IsDead || player == null || player.IsDead)
            {
                CancelAttack();
                return;
            }
            playerVisible = Vector2.Distance(home, player.transform.position) <= chaseDistance && CanSeePlayer();
            if (playerVisible)
            {
                lastKnown = player.transform.position;
                alertUntil = Time.time + investigationSeconds;
            }
            else if (Time.time < alertUntil && Vector2.Distance(body.position, lastKnown) < .4f)
                alertUntil = Mathf.Min(alertUntil, Time.time + 1f);
            if (windupRemaining <= 0f)
            {
                if (playerVisible && lungeRemaining <= 0f &&
                    Vector2.Distance(transform.position, player.transform.position) <= attackRange && Time.time >= nextAttackAt)
                {
                    attackDirection = ((Vector2)player.transform.position - body.position).normalized;
                    windupRemaining = windupSeconds;
                    ClearPath();
                    if (warning != null) warning.gameObject.SetActive(true);
                    RefreshWarning();
                }
            }
            else
            {
                windupRemaining -= Time.deltaTime;
                if (windupRemaining <= 0f)
                {
                    if (warning != null) warning.gameObject.SetActive(false);
                    if (playerVisible) Attack();
                    nextAttackAt = Time.time + attackInterval;
                }
                else RefreshWarning();
            }
            if (lungeRemaining > 0f)
            {
                lungeRemaining -= Time.deltaTime;
                if (!lungeHit && playerHitbox != null &&
                    hitbox.Distance(playerHitbox).distance <= .08f && CanSeePlayer())
                {
                    player.TakeDamage(damage);
                    lungeHit = true;
                }
            }
        }

        private void FixedUpdate()
        {
            if (health.IsDead || player == null || player.IsDead)
            {
                body.linearVelocity = Vector2.zero;
                RefreshPresentation(Vector2.zero);
                return;
            }
            if (lungeRemaining > 0f)
            {
                body.linearVelocity = lungeDirection * moveSpeed * 3f;
                RefreshPresentation(body.linearVelocity);
                return;
            }
            if (windupRemaining > 0f)
            {
                body.linearVelocity = Vector2.zero;
                RefreshPresentation(attackDirection);
                if (animator != null) animator.SetBool("IsMoving", false);
                return;
            }
            Vector2 goal = Time.time < alertUntil ? lastKnown : home;
            Vector2 offset = goal - body.position;
            float desired = playerVisible && attackKind == AttackKind.Ranged ? attackRange * 0.7f : 0.3f;
            if (offset.magnitude <= desired)
            {
                ClearPath();
                body.linearVelocity = Vector2.zero;
            }
            else body.linearVelocity = MoveVelocity(goal);
            RefreshPresentation(body.linearVelocity);
        }

        private Vector2 MoveVelocity(Vector2 goal)
        {
            if (navigation == null) return (goal - body.position).normalized * moveSpeed;
            AdvancePath();
            bool blocked = pathIndex < path.Count && !navigation.CanTraverse(body.position, path[pathIndex]);
            if (Time.time >= nextPathAt)
            {
                bool stalled = tryingToMove && (body.position - progressPosition).sqrMagnitude < .0025f;
                bool goalMoved = (goal - pathGoal).sqrMagnitude > .0625f;
                if (!pathRequested || goalMoved || pathRevision != navigation.Revision || blocked || stalled)
                {
                    navigation.TryFindPath(body.position, goal, path);
                    pathIndex = 0;
                    pathGoal = goal;
                    pathRevision = navigation.Revision;
                    pathRequested = true;
                    AdvancePath();
                    blocked = pathIndex < path.Count && !navigation.CanTraverse(body.position, path[pathIndex]);
                }
                nextPathAt = Time.time + repathSeconds;
                progressPosition = body.position;
            }
            tryingToMove = !blocked && pathIndex < path.Count;
            return tryingToMove
                ? Vector2.ClampMagnitude((path[pathIndex] - body.position) / Time.fixedDeltaTime, moveSpeed)
                : Vector2.zero;
        }

        private void AdvancePath()
        {
            // Switch corners only when the full body can take the next segment from its actual position.
            for (int i = path.Count - 1; i > pathIndex; i--)
                if (navigation.CanTraverse(body.position, path[i]))
                {
                    pathIndex = i;
                    break;
                }
            while (pathIndex < path.Count && Vector2.Distance(body.position, path[pathIndex]) < .01f) pathIndex++;
        }

        private void ClearPath()
        {
            path.Clear();
            pathIndex = 0;
            pathRequested = tryingToMove = false;
            nextPathAt = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 previous = transform.position;
            for (int i = pathIndex; i < path.Count; i++)
            {
                Vector3 next = path[i];
                Gizmos.DrawLine(previous, next);
                Gizmos.DrawWireSphere(next, .08f);
                previous = next;
            }
        }

        private void RefreshPresentation(Vector2 direction)
        {
            if (animator != null) animator.SetBool("IsMoving", body.linearVelocity.sqrMagnitude > .01f);
            if (visualRoot == null || Mathf.Abs(direction.x) < .01f) return;
            Vector3 scale = visualRoot.localScale;
            scale.x = Mathf.Abs(scale.x) * Mathf.Sign(direction.x);
            visualRoot.localScale = scale;
        }

        private void RefreshWarning()
        {
            if (!directionalWarning || warning == null) return;
            float reach = attackKind == AttackKind.Ranged ? attackRange : moveSpeed * 3f * .35f;
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(body.position, attackDirection, reach))
            {
                if (hit.collider.isTrigger || hit.collider.transform.IsChildOf(transform) ||
                    hit.collider.GetComponentInParent<CombatHealth2D>() != null) continue;
                reach = hit.distance;
                break;
            }
            warning.transform.position = body.position + attackDirection * reach * .5f;
            warning.transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(attackDirection.y, attackDirection.x) * Mathf.Rad2Deg);
            warning.transform.localScale = new Vector3(reach, .06f, 1f);
            warning.color = Color.Lerp(new Color(1f, .8f, .2f, .65f), new Color(1f, .25f, .15f, .9f),
                1f - windupRemaining / windupSeconds);
        }

        private void CancelAttack()
        {
            windupRemaining = 0f;
            lungeRemaining = 0f;
            if (warning != null) warning.gameObject.SetActive(false);
        }

        private void Attack()
        {
            Vector2 direction = attackDirection;
            if (attackKind == AttackKind.Lunge)
            {
                lungeDirection = direction;
                lungeRemaining = 0.35f;
                lungeHit = false;
            }
            else if (projectilePrefab != null)
            {
                Vector2 origin = (Vector2)transform.position + direction * 0.65f;
                ShootingProjectile2D shot = Instantiate(projectilePrefab, origin, Quaternion.identity);
                shot.Fire(direction, 8f, damage, hitbox);
            }
        }

        private void OnDeath(CombatHealth2D dead)
        {
            ClearPath();
            body.linearVelocity = Vector2.zero;
            hitbox.enabled = false;
            CancelAttack();
            RefreshPresentation(Vector2.zero);
            if (animator != null) animator.enabled = false;
            if (remains == null) return;
            remains.gameObject.SetActive(true);
            FindFirstObjectByType<InventorySession>()?.RegisterPrototypeContainer(remains);
        }

        private void OnDestroy()
        {
            if (health != null) health.Died -= OnDeath;
        }

        private void OnDisable()
        {
            ClearPath();
            CancelAttack();
            if (body != null) body.linearVelocity = Vector2.zero;
        }
    }
}
