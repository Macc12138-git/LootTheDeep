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
            damage = shotDamage;
            if (ownerCollider != null) Physics2D.IgnoreCollision(projectileCollider, ownerCollider);
            body.linearVelocity = direction * speed;
            Destroy(gameObject, lifetime);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (spent || other == ownerCollider || other.isTrigger ||
                other.GetComponent<ShootingProjectile2D>() != null) return;
            spent = true;
            TargetDummy2D target = other.GetComponentInParent<TargetDummy2D>();
            if (target != null) target.TakeDamage(damage);
            Destroy(gameObject);
        }
    }
}
