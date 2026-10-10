using System;
using UnityEngine;

namespace LootTheDeep.Gameplay
{
    public enum CombatTeam { Player, Hostile }

    public sealed class CombatHealth2D : MonoBehaviour
    {
        [SerializeField] private CombatTeam team;
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private SpriteRenderer visual;
        private int currentHealth;
        private Color baseColor;
        private float flashUntil;

        public event Action<CombatHealth2D> Changed;
        public event Action<CombatHealth2D> Died;
        public CombatTeam Team => team;
        public int CurrentHealth => currentHealth;
        public int MaxHealth => maxHealth;
        public bool IsDead => currentHealth <= 0;

        public void Configure(CombatTeam faction, int maximum, SpriteRenderer renderer)
        {
            team = faction;
            maxHealth = maximum;
            visual = renderer;
        }

        private void Awake()
        {
            currentHealth = maxHealth;
            if (visual != null) baseColor = visual.color;
        }

        public void TakeDamage(int amount)
        {
            if (amount <= 0 || IsDead) return;
            currentHealth = Mathf.Max(0, currentHealth - amount);
            if (visual != null) { visual.color = Color.white; flashUntil = Time.time + 0.12f; }
            Changed?.Invoke(this);
            if (IsDead) Died?.Invoke(this);
        }

        private void Update()
        {
            if (visual != null && Time.time >= flashUntil && visual.color != baseColor)
                visual.color = IsDead ? new Color(baseColor.r, baseColor.g, baseColor.b, 0.4f) : baseColor;
        }
    }
}
