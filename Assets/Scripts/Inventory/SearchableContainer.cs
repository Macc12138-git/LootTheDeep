using UnityEngine;

namespace LootTheDeep.Inventory
{
    public sealed class SearchableContainer : MonoBehaviour
    {
        public string displayName = "沉船维修箱";
        [Min(0.1f)] public float searchSeconds = 1.5f;
        [SerializeField, Min(1)] private int columns = 6;
        [SerializeField, Min(1)] private int rows = 4;
        [SerializeField] private InventorySeed[] initialItems;
        [SerializeField] private SpriteRenderer visual;
        public GridInventory Contents { get; private set; }
        public int FoundCount { get; private set; }
        public float Progress { get; private set; }
        public bool Searching { get; private set; }
        public LootItem Current => Contents?.NextUnknown();
        private Color originalColor;

        public void Initialize()
        {
            if (Contents != null) return;
            Contents = new GridInventory(displayName, columns, rows);
            foreach (var seed in initialItems)
                Contents.Seed(new LootItem(seed.definition, false), seed.position.x, seed.position.y);
            if (visual != null) originalColor = visual.color;
        }
        public void Highlight(bool value)
        {
            if (visual != null) visual.color = value ? new Color(1f, .87f, .49f) : originalColor;
        }
        public void BeginSearch() { Searching = Current != null; Progress = 0f; }
        public void PauseSearch() { Searching = false; Progress = 0f; }
        public bool Tick(float deltaTime)
        {
            if (!Searching || Current == null) return false;
            Progress += deltaTime / searchSeconds;
            if (Progress < 1f) return false;
            Current.Revealed = true;
            FoundCount++;
            Progress = 0f;
            Searching = Current != null;
            return true;
        }
        private void OnDisable() { PauseSearch(); Highlight(false); }
    }
}
