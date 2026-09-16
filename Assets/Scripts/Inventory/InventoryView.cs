using UnityEngine;
using UnityEngine.UI;

namespace LootTheDeep.Inventory
{
    public sealed class InventoryView : MonoBehaviour
    {
        [Header("Prefab references")]
        [SerializeField] private GameObject window;
        [SerializeField] private InventoryGridView[] playerGrids;
        [SerializeField] private InventoryGridView containerGrid;
        [SerializeField] private GameObject containerPanel;
        [SerializeField] private GameObject emptyContainer;
        [SerializeField] private Text containerTitle;
        [SerializeField] private Text searchStatus;
        [SerializeField] private Text searchButtonLabel;
        [SerializeField] private Text capacity;
        [SerializeField] private Text detail;
        [SerializeField] private Text notification;
        [SerializeField] private Text prompt;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button searchButton;
        [SerializeField] private Button transferButton;
        [SerializeField] private ScrollRect storageScroll;
        [Header("Drag presentation")]
        [SerializeField] private RectTransform dragLayer;
        [SerializeField] private Image ghost;
        [SerializeField] private Image ghostIcon;
        [SerializeField] private Image placement;
        [Header("Common / Uncommon / Rare / Epic")]
        [SerializeField] private Color[] qualityColors;
        private InventorySession session;
        private LootItem selected;
        private LootItem dragging;
        private bool rotated;
        private Vector2 grabOffset;
        private Vector2 pointer;
        private InventoryGridView destination;
        private int dropX, dropY;
        public InventoryGridView[] PlayerGrids => playerGrids;
        public bool IsDragging => dragging != null;
        public bool HasContainer { get; private set; }

        public void Bind(InventorySession owner)
        {
            session = owner;
            for (int i = 0; i < playerGrids.Length; i++) playerGrids[i].Bind(session.Storage[i], this);
            closeButton.onClick.AddListener(session.Close);
            searchButton.onClick.AddListener(session.ToggleSearch);
            transferButton.onClick.AddListener(TransferSelected);
            CancelDrag();
        }
        public void Show(bool value)
        {
            window.SetActive(value);
            prompt.gameObject.SetActive(!value);
            if (value) { selected = null; detail.text = "选择物品查看详情"; notification.text = ""; }
        }
        public void Refresh()
        {
            HasContainer = session.Target != null;
            containerPanel.SetActive(HasContainer);
            emptyContainer.SetActive(!HasContainer);
            if (HasContainer)
            {
                containerTitle.text = session.Target.displayName;
                containerGrid.Bind(session.Target.Contents, this);
            }
            int occupied = 0, total = 0;
            foreach (var grid in playerGrids) { grid.Refresh(); occupied += grid.Model.Occupied; total += grid.Model.Columns * grid.Model.Rows; }
            capacity.text = occupied + " / " + total + " 格";
            transferButton.interactable = selected != null && HasContainer;
            if (selected != null) Select(selected);
            UpdateSearch();
        }
        public void UpdateSearch()
        {
            if (session.Target == null) return;
            var target = session.Target;
            containerGrid.UpdateSearch(target);
            searchStatus.text = target.Current == null ? "搜索完成" : target.Searching ? "逐件搜索中 · 已发现 " + target.FoundCount : "搜索已暂停";
            searchButtonLabel.text = target.Current == null ? "已搜索完毕" : target.Searching ? "暂停搜索" : "继续搜索";
            searchButton.interactable = target.Current != null;
        }
        public void SetPrompt(string text) { prompt.text = text; }
        public void Notify(string text) { notification.text = text; }
        public Color QualityColor(int quality) { return qualityColors[Mathf.Clamp(quality, 0, qualityColors.Length - 1)]; }
        public void Select(LootItem item)
        {
            selected = item;
            detail.text = item.Definition.displayName + "   " + item.Width + " × " + item.Height + "\n" + item.Definition.description;
            transferButton.interactable = HasContainer;
        }
        public void Transfer(LootItem item) { session.QuickTransfer(item); }
        private void TransferSelected() { if (selected != null) Transfer(selected); }
        public void BeginDrag(LootItem item, Vector2 screen, RectTransform card)
        {
            dragging = item; rotated = item.Rotated;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(card, screen, null, out var local);
            grabOffset = new Vector2(local.x, -local.y);
            ghost.gameObject.SetActive(true);
            ghostIcon.sprite = item.Definition.icon;
            Select(item);
            MoveDrag(screen);
        }
        public void MoveDrag(Vector2 screen)
        {
            if (dragging == null) return;
            pointer = screen;
            float cellSize = playerGrids[0].CellSize;
            var size = new Vector2(rotated ? dragging.Definition.height : dragging.Definition.width,
                rotated ? dragging.Definition.width : dragging.Definition.height) * cellSize;
            grabOffset = Vector2.Min(grabOffset, size - Vector2.one);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(dragLayer, screen, null, out var local);
            ghost.rectTransform.anchoredPosition = local - new Vector2(grabOffset.x, -grabOffset.y);
            ghost.rectTransform.sizeDelta = size;
            ghostIcon.rectTransform.localEulerAngles = new Vector3(0, 0, rotated ? -90 : 0);
            destination = null;
            foreach (var grid in playerGrids) if (grid.Hit(screen, out var hit)) { FindDrop(grid, hit); break; }
            if (destination == null && HasContainer && containerGrid.Hit(screen, out var containerHit)) FindDrop(containerGrid, containerHit);
            placement.gameObject.SetActive(destination != null);
            bool valid = destination != null && destination.Model.CanPlace(dragging, dropX, dropY, rotated);
            ghost.color = valid ? new Color(.30f, .65f, .52f, .65f) : new Color(.70f, .28f, .25f, .65f);
            if (destination != null)
            {
                placement.rectTransform.SetParent(destination.Bounds, false);
                placement.rectTransform.anchoredPosition = new Vector2(dropX * cellSize, -dropY * cellSize);
                placement.rectTransform.sizeDelta = size;
                placement.color = valid ? new Color(.25f, .85f, .60f, .35f) : new Color(.95f, .30f, .25f, .35f);
            }
        }
        private void FindDrop(InventoryGridView grid, Vector2 hit)
        {
            destination = grid;
            dropX = Mathf.FloorToInt(hit.x / grid.CellSize) - Mathf.FloorToInt(grabOffset.x / grid.CellSize);
            dropY = Mathf.FloorToInt(-hit.y / grid.CellSize) - Mathf.FloorToInt(grabOffset.y / grid.CellSize);
        }
        public void Rotate()
        {
            if (dragging == null)
            {
                if (selected == null) return;
                bool changed = selected.Owner.TryPlace(selected, selected.X, selected.Y, !selected.Rotated);
                Notify(changed ? "已旋转物品" : "当前位置没有足够空间旋转");
                Refresh();
                return;
            }
            rotated = !rotated;
            grabOffset = new Vector2(grabOffset.y, grabOffset.x);
            MoveDrag(pointer);
        }
        public void EndDrag(Vector2 screen)
        {
            if (dragging == null) return;
            MoveDrag(screen);
            bool moved = destination != null && destination.Model.TryPlace(dragging, dropX, dropY, rotated);
            Notify(moved ? "已放入" + destination.Model.Name : "放置无效，物品保留在原位置");
            CancelDrag(); Refresh();
        }
        public void CancelDrag()
        {
            dragging = null; destination = null;
            ghost.gameObject.SetActive(false); placement.gameObject.SetActive(false);
            placement.rectTransform.SetParent(dragLayer, false);
        }
        private void LateUpdate()
        {
            if (IsDragging) MoveDrag(pointer);
        }
        private void OnDisable()
        {
            if (session != null && session.IsOpen) session.Close();
        }
    }
}
