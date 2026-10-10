using UnityEngine;

namespace LootTheDeep.Gameplay
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class TargetDummy2D : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 30;
        [SerializeField] private SpriteRenderer body;
        [SerializeField] private SpriteRenderer healthFill;
        [SerializeField] private Color normalColor = new(0.9f, 0.58f, 0.24f);

        private Collider2D hitbox;
        private int health;
        private float flashUntil;

        public int Health => health;

        public void Configure(int maximum, SpriteRenderer targetBody, SpriteRenderer fill, Color color)
        {
            maxHealth = maximum;
            body = targetBody;
            healthFill = fill;
            normalColor = color;
            if (body != null) body.color = color;
        }

        private void Awake()
        {
            hitbox = GetComponent<Collider2D>();
            health = maxHealth;
            RefreshHealthBar();
        }

        public void TakeDamage(int amount)
        {
            if (health <= 0 || amount <= 0) return;
            health = Mathf.Max(0, health - amount);
            flashUntil = Time.time + 0.12f;
            if (body != null) body.color = Color.white;
            RefreshHealthBar();
            if (health == 0)
            {
                hitbox.enabled = false;
                if (body != null) body.color = new Color(0.25f, 0.38f, 0.4f, 0.5f);
            }
        }

        private void Update()
        {
            if (health > 0 && body != null && Time.time >= flashUntil && body.color != normalColor)
                body.color = normalColor;
        }

        private void RefreshHealthBar()
        {
            if (healthFill == null) return;
            float ratio = (float)health / maxHealth;
            healthFill.transform.localScale = new Vector3(0.9f * ratio, 0.08f, 1f);
            healthFill.transform.localPosition = new Vector3(-0.45f + 0.45f * ratio, 0.63f, 0f);
        }
    }
}
