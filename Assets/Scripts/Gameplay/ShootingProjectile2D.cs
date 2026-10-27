using UnityEngine;

namespace LootTheDeep.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class ShootingProjectile2D : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float lifetime = 2.5f;

        private Rigidbody2D body;
        private Collider2D projectileCollider;
        private Collider2D ownerCollider;
        private CombatHealth2D ownerHealth;
        private int damage;
        private bool spent;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            projectileCollider = GetComponent<Collider2D>();
        }

        public void Fire(Vector2 direction, float speed, int shotDamage, Collider2D owner)
        {
            ownerCollider = owner;
            ownerHealth = owner == null ? null : owner.GetComponentInParent<CombatHealth2D>();
            damage = shotDamage;
            body.rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (ownerCollider != null)
                foreach (Collider2D hitbox in ownerCollider.GetComponentsInChildren<Collider2D>())
                    Physics2D.IgnoreCollision(projectileCollider, hitbox);
            body.linearVelocity = direction * speed;
            Destroy(gameObject, lifetime);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (spent || other == ownerCollider || other.isTrigger ||
                other.GetComponent<ShootingProjectile2D>() != null) return;
            CombatHealth2D victim = other.GetComponentInParent<CombatHealth2D>();
            if (victim != null && ownerHealth != null && victim.Team == ownerHealth.Team) return;
            spent = true;
            if (victim != null) victim.TakeDamage(damage);
            else if (ownerHealth == null || ownerHealth.Team == CombatTeam.Player)
            {
                TargetDummy2D target = other.GetComponentInParent<TargetDummy2D>();
                if (target != null) target.TakeDamage(damage);
            }
            Destroy(gameObject);
        }
    }
}
