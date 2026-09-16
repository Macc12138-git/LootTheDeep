using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LootTheDeep.Inventory
{
    public sealed class InventoryGridView : MonoBehaviour
    {
        public string displayName;
        [Min(1)] public int columns = 5;
        [Min(1)] public int rows = 10;
        [SerializeField, Min(20)] private float cellSize = 56;
        [SerializeField] private RectTransform bounds;
        [SerializeField] private RectTransform clipViewport;
        [SerializeField] private Image cellPrefab;
        [SerializeField] private RectTransform cells;
        [SerializeField] private RectTransform cards;
        [SerializeField] private InventoryItemView itemPrefab;
        private readonly Dictionary<LootItem, InventoryItemView> views = new();
        private InventoryView page;
        private bool initialized;
        public GridInventory Model { get; private set; }
        public RectTransform Bounds => bounds;
        public float CellSize => cellSize;

        public void Bind(GridInventory model, InventoryView owner)
        {
            if (Model != model)
            {
                foreach (var view in views.Values) Destroy(view.gameObject);
                views.Clear();
            }
            Model = model; page = owner;
            if (!initialized)
            {
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < columns; x++)
                    {
                        var cell = Instantiate(cellPrefab, cells);
                        cell.rectTransform.anchoredPosition = new Vector2(x * cellSize, -y * cellSize);
                        cell.rectTransform.sizeDelta = Vector2.one * cellSize;
                    }
                initialized = true;
            }
            Refresh();
        }
        public void Refresh()
        {
            var removed = new List<LootItem>();
            foreach (var pair in views)
                if (pair.Key.Owner != Model) { Destroy(pair.Value.gameObject); removed.Add(pair.Key); }
            foreach (var item in removed) views.Remove(item);
            if (Model == null) return;
            foreach (var item in Model.Items)
            {
                if (!views.TryGetValue(item, out var card))
                {
                    card = Instantiate(itemPrefab, cards);
                    views.Add(item, card);
                }
                card.Bind(item, page, cellSize);
            }
        }
        public void UpdateSearch(SearchableContainer container)
        {
            foreach (var pair in views)
                pair.Value.SetSearch(container != null && container.Searching && pair.Key == container.Current,
                    container != null ? container.Progress : 0);
        }
        public bool Hit(Vector2 screen, out Vector2 local)
        {
            local = default;
            if (!gameObject.activeInHierarchy || Model == null) return false;
            if (clipViewport != null && !RectTransformUtility.RectangleContainsScreenPoint(clipViewport, screen)) return false;
            if (!RectTransformUtility.RectangleContainsScreenPoint(bounds, screen)) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds, screen, null, out local);
        }
    }
}
