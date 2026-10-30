using LootTheDeep.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LootTheDeep.Gameplay
{
    public sealed class ExtractionPoint2D : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float interactDistance = 1.5f;
        [SerializeField, Min(0.1f)] private float holdSeconds = 2f;
        public string displayName = "撤离点";
        public float InteractDistance => interactDistance;
        private float progress;

        public bool CanUse(CombatHealth2D player) => isActiveAndEnabled &&
            Vector2.Distance(transform.position, player.transform.position) <= interactDistance;

        public void TickInteraction(bool selected, CombatPrototypeSession2D session)
        {
            if (!selected) { progress = 0; return; }
            if (Keyboard.current?.eKey.isPressed == true)
            {
                progress += Time.deltaTime / holdSeconds;
                if (progress >= 1f) { session.Finish(true); return; }
            }
            else progress = 0f;
            session.SetExtractionStatus("按住 E 启动撤离 · " + displayName, progress);
        }

        private void OnDrawGizmos()
        { Gizmos.color = Color.green; Gizmos.DrawWireSphere(transform.position, interactDistance); }
    }
}
