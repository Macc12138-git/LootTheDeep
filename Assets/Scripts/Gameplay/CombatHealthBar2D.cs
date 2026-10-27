using UnityEngine;

namespace LootTheDeep.Gameplay
{
    public sealed class CombatHealthBar2D : MonoBehaviour
    {
        [SerializeField] private CombatHealth2D health;
        [SerializeField] private Transform fill;
        private Vector3 fullScale;
        private Vector3 fullPosition;

        public void Configure(CombatHealth2D target, Transform bar)
        {
            health = target;
            fill = bar;
        }

        private void Start()
        {
            fullScale = fill.localScale;
            fullPosition = fill.localPosition;
            health.Changed += Refresh;
            Refresh(health);
        }

        private void Refresh(CombatHealth2D target)
        {
            float fraction = (float)target.CurrentHealth / target.MaxHealth;
            fill.localScale = new Vector3(fullScale.x * fraction, fullScale.y, fullScale.z);
            fill.localPosition = fullPosition - Vector3.right * (fullScale.x * (1f - fraction) * .5f);
            gameObject.SetActive(!target.IsDead);
        }

        private void OnDestroy()
        {
            if (health != null) health.Changed -= Refresh;
        }
    }
}
