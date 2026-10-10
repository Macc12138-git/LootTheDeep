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

        private Rigidbody2D body;
        private Collider2D hitbox;
        private CombatHealth2D health;
        private CombatHealth2D player;
        private Vector2 home;
        private Vector2 lastKnown;
        private float alertUntil;
        private float nextAttackAt;
        private float windupRemaining;
        private float lungeRemaining;
        private Vector2 lungeDirection;
        private bool lungeHit;

        public void Configure(AttackKind kind, ShootingProjectile2D shot, SpriteRenderer marker,
            SearchableContainer loot, float range, float speed, float reach, int attackDamage)
        {
            attackKind = kind; projectilePrefab = shot; warning = marker; remains = loot;
            sightRange = range; moveSpeed = speed; attackRange = reach; damage = attackDamage;
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
            var shooter = FindFirstObjectByType<PlayerShooter2D>();
            if (shooter != null) player = shooter.GetComponent<CombatHealth2D>();
        }

        public void Hear(Vector2 source, float radius)
        {
            if (health.IsDead || Vector2.Distance(transform.position, source) > radius) return;
            lastKnown = source;
            alertUntil = Time.time + 3f;
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
            if (health.IsDead || player == null || player.IsDead) return;
            bool visible = CanSeePlayer();
            if (visible)
            {
                lastKnown = player.transform.position;
                alertUntil = Time.time + 2.5f;
            }
            if (windupRemaining <= 0f)
            {
                if (visible && Vector2.Distance(transform.position, player.transform.position) <= attackRange &&
                    Time.time >= nextAttackAt)
                {
                    windupRemaining = windupSeconds;
                    if (warning != null) warning.gameObject.SetActive(true);
                }
            }
            else
            {
                windupRemaining -= Time.deltaTime;
                if (windupRemaining <= 0f)
                {
                    if (warning != null) warning.gameObject.SetActive(false);
                    if (CanSeePlayer()) Attack();
                    nextAttackAt = Time.time + attackInterval;
                }
            }
            if (lungeRemaining > 0f)
            {
                lungeRemaining -= Time.deltaTime;
                if (!lungeHit && Vector2.Distance(transform.position, player.transform.position) < 1f)
                {
                    player.TakeDamage(damage);
                    lungeHit = true;
                }
            }
        }

        private void FixedUpdate()
        {
            if (health.IsDead || player == null || player.IsDead) { body.linearVelocity = Vector2.zero; return; }
            if (lungeRemaining > 0f) { body.linearVelocity = lungeDirection * moveSpeed * 3f; return; }
            if (windupRemaining > 0f) { body.linearVelocity = Vector2.zero; return; }
            Vector2 goal = Time.time < alertUntil ? lastKnown : home;
            Vector2 offset = goal - body.position;
            float desired = Time.time < alertUntil && attackKind == AttackKind.Ranged ? attackRange * 0.7f : 0.3f;
            body.linearVelocity = offset.magnitude > desired ? offset.normalized * moveSpeed : Vector2.zero;
        }

        private void Attack()
        {
            Vector2 direction = ((Vector2)player.transform.position - (Vector2)transform.position).normalized;
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
            body.linearVelocity = Vector2.zero;
            hitbox.enabled = false;
            if (warning != null) warning.gameObject.SetActive(false);
            if (remains == null) return;
            remains.gameObject.SetActive(true);
            FindFirstObjectByType<InventorySession>()?.RegisterPrototypeContainer(remains);
        }

        private void OnDestroy()
        {
            if (health != null) health.Died -= OnDeath;
        }
    }
}
