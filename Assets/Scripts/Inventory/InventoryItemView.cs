using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LootTheDeep.Inventory
{
    public sealed class InventoryItemView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        [SerializeField] private RectTransform bounds;
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private Image qualityMark;
        [SerializeField] private Text label;
        [SerializeField] private GameObject searchOverlay;
        [SerializeField] private Image progress;
        private InventoryView page;
        private LootItem item;
        public void Bind(LootItem value, InventoryView owner, float cellSize)
        {
            item = value; page = owner;
            bounds.anchoredPosition = new Vector2(item.X * cellSize, -item.Y * cellSize);
            bounds.sizeDelta = new Vector2(item.Width, item.Height) * cellSize;
            background.color = item.Revealed ? page.QualityColor(item.Definition.quality) : new Color(.20f, .23f, .24f, .96f);
            icon.gameObject.SetActive(item.Revealed);
            icon.sprite = item.Definition.icon;
            icon.rectTransform.localEulerAngles = new Vector3(0, 0, item.Rotated ? -90 : 0);
            qualityMark.gameObject.SetActive(item.Revealed);
            qualityMark.color = item.Revealed ? Color.Lerp(background.color, Color.white, .45f) : Color.clear;
            label.text = item.Revealed ? item.Definition.displayName : "";
            searchOverlay.SetActive(false);
        }
        public void SetSearch(bool active, float value)
        {
            searchOverlay.SetActive(active);
            progress.fillAmount = value;
        }
        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && item.Revealed)
                page.BeginDrag(item, e.position, bounds);
        }
        public void OnDrag(PointerEventData e) { page.MoveDrag(e.position); }
        public void OnEndDrag(PointerEventData e) { page.EndDrag(e.position); }
        public void OnPointerClick(PointerEventData e)
        {
            if (!item.Revealed || e.dragging) return;
            if (e.button == PointerEventData.InputButton.Right) { page.Select(item); page.Rotate(); return; }
            if (e.button != PointerEventData.InputButton.Left) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed))
                page.Transfer(item);
            else page.Select(item);
        }
    }
}
