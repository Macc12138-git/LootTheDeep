using LootTheDeep.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LootTheDeep.Gameplay
{
    public sealed class ExtractionPoint2D : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float interactDistance = 1.5f;
        [SerializeField, Min(0.1f)] private float holdSeconds = 2f;
        private CombatPrototypeSession2D session;
        private CombatHealth2D player;
        private InventorySession inventory;
        private float progress;

        private void Start()
        {
            session = FindFirstObjectByType<CombatPrototypeSession2D>();
            player = FindFirstObjectByType<PlayerShooter2D>()?.GetComponent<CombatHealth2D>();
            inventory = FindFirstObjectByType<InventorySession>();
        }

        private void Update()
        {
            if (session == null || session.IsFinished || player == null || player.IsDead) return;
            bool nearby = Vector2.Distance(transform.position, player.transform.position) <= interactDistance;
            if (!nearby || inventory != null && inventory.IsOpen)
            {
                progress = 0f;
                session.SetExtractionStatus(null, 0f);
                return;
            }
            if (Keyboard.current?.eKey.isPressed == true)
            {
                progress += Time.deltaTime / holdSeconds;
                if (progress >= 1f) { session.Finish(true); return; }
            }
            else progress = 0f;
            session.SetExtractionStatus("按住 E 启动撤离", progress);
        }
    }
}
