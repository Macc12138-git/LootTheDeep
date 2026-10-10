using UnityEngine;
using UnityEngine.UI;

namespace LootTheDeep.Gameplay
{
    [RequireComponent(typeof(Canvas))]
    public sealed class ShootingHud2D : MonoBehaviour
    {
        [SerializeField] private RectTransform crosshair;
        [SerializeField] private Graphic[] crosshairParts;
        [SerializeField] private Text weaponLabel;
        [SerializeField] private Text ammoLabel;
        [SerializeField] private RectTransform reloadGroup;
        [SerializeField] private Image reloadFill;
        [SerializeField] private Text reloadLabel;
        private bool reloading;

        private RectTransform canvasRect;
        private Canvas canvas;

        public void Configure(RectTransform marker, Graphic[] parts, Text weapon, Text ammo)
        {
            crosshair = marker;
            crosshairParts = parts;
            weaponLabel = weapon;
            ammoLabel = ammo;
        }

        public void ConfigureReload(RectTransform group, Image fill, Text label)
        {
            reloadGroup = group; reloadFill = fill; reloadLabel = label;
        }

        private void Awake()
        {
            canvas = GetComponent<Canvas>();
            canvasRect = GetComponent<RectTransform>();
        }

        public void SetCombatActive(bool active, bool inventoryOpen)
        {
            if (crosshair != null) crosshair.gameObject.SetActive(active);
            if (weaponLabel != null) weaponLabel.gameObject.SetActive(active);
            if (ammoLabel != null) ammoLabel.gameObject.SetActive(active);
            if (reloadGroup != null)
            {
                reloadGroup.gameObject.SetActive((active || inventoryOpen) && reloading);
                reloadGroup.anchorMin = reloadGroup.anchorMax = reloadGroup.pivot =
                    inventoryOpen ? new Vector2(0.5f, 1f) : Vector2.one;
                reloadGroup.anchoredPosition = inventoryOpen ? new Vector2(0f, -12f) : new Vector2(-26f, -90f);
            }
        }

        public void SetCrosshair(Vector2 screenPoint, bool valid)
        {
            if (crosshair == null || canvasRect == null) return;
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, uiCamera, out Vector2 local))
                crosshair.anchoredPosition = local;
            Color color = valid ? new Color(0.92f, 1f, 0.88f) : new Color(1f, 0.44f, 0.35f, 0.75f);
            foreach (Graphic part in crosshairParts)
                if (part != null) part.color = color;
        }

        public void SetWeapon(string weaponName, int ammo, bool loading, float progress)
        {
            if (weaponLabel != null) weaponLabel.text = weaponName;
            if (ammoLabel != null) ammoLabel.text = ammo == 0 && !loading ? "空仓  按 R 换弹" : $"{ammo:00} / ∞";
            reloading = loading;
            if (reloadFill != null) reloadFill.fillAmount = Mathf.Clamp01(progress);
            if (reloadLabel != null) reloadLabel.text = loading ? "换弹中" : "";
        }
    }
}
