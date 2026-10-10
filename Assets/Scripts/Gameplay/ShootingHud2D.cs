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

        private RectTransform canvasRect;
        private Canvas canvas;

        public void Configure(RectTransform marker, Graphic[] parts, Text weapon, Text ammo)
        {
            crosshair = marker;
            crosshairParts = parts;
            weaponLabel = weapon;
            ammoLabel = ammo;
        }

        private void Awake()
        {
            canvas = GetComponent<Canvas>();
            canvasRect = GetComponent<RectTransform>();
        }

        public void SetCombatActive(bool active)
        {
            if (crosshair != null) crosshair.gameObject.SetActive(active);
            if (weaponLabel != null) weaponLabel.gameObject.SetActive(active);
            if (ammoLabel != null) ammoLabel.gameObject.SetActive(active);
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

        public void SetWeapon(string weaponName, int ammo, int capacity, bool unlimited)
        {
            if (weaponLabel != null) weaponLabel.text = weaponName;
            if (ammoLabel != null) ammoLabel.text = unlimited ? "TEST  INF" : $"TEST  {ammo:00} / {capacity:00}";
        }
    }
}
