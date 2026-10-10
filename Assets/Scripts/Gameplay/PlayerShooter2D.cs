using System;
using LootTheDeep.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LootTheDeep.Gameplay
{
    [RequireComponent(typeof(PlayerInput))]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerShooter2D : MonoBehaviour
    {
        [Serializable]
        public sealed class WeaponSlot
        {
            public string displayName;
            public GameObject visual;
            public Transform muzzle;
            public SpriteRenderer muzzleFlash;
            public ShootingProjectile2D projectilePrefab;
            [Min(1)] public int damage;
            [Min(0.01f)] public float fireInterval;
            [Min(0.1f)] public float projectileSpeed;
            [Min(0)] public int initialAmmo;

            [NonSerialized] public int Ammo;
            [NonSerialized] public float NextShotAt;
            [NonSerialized] public float FlashUntil;
        }

        [SerializeField] private Transform weaponPivot;
        [SerializeField] private WeaponSlot[] weapons;
        [SerializeField] private bool infiniteAmmo;
        [SerializeField, Min(0f)] private float minimumMuzzleDistance = 0.08f;

        private PlayerInput playerInput;
        private InputAction aimPointAction;
        private InputAction attackAction;
        private InputAction previousAction;
        private InputAction nextAction;
        private InventorySession inventory;
        private ShootingHud2D hud;
        private Camera targetCamera;
        private Collider2D ownerCollider;
        private Vector2 aimWorldPoint;
        private int activeIndex;
        private bool aimValid;
        private bool focused = true;
        private bool cursorBaselineCaptured;
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLock;

        public int ActiveWeaponIndex => activeIndex;
        public WeaponSlot ActiveWeapon => weapons != null && weapons.Length > activeIndex ? weapons[activeIndex] : null;

        public void Configure(Transform pivot, WeaponSlot mainWeapon, WeaponSlot secondaryWeapon)
        {
            weaponPivot = pivot;
            weapons = new[] { mainWeapon, secondaryWeapon };
        }

        private void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
            ownerCollider = GetComponent<Collider2D>();
            if (weapons == null) return;
            foreach (WeaponSlot weapon in weapons)
            {
                weapon.Ammo = weapon.initialAmmo;
                weapon.NextShotAt = 0f;
            }
            SelectWeapon(0);
        }

        private void OnEnable()
        {
            aimPointAction = playerInput.actions?.FindAction("Player/AimPoint", false);
            attackAction = playerInput.actions?.FindAction("Player/Attack", false);
            previousAction = playerInput.actions?.FindAction("Player/Previous", false);
            nextAction = playerInput.actions?.FindAction("Player/Next", false);
            if (aimPointAction == null || attackAction == null)
                Debug.LogError("Player/AimPoint and Player/Attack are required for the shooting range.", this);
        }

        private void Start()
        {
            targetCamera = Camera.main;
            inventory = FindFirstObjectByType<InventorySession>();
            hud = FindFirstObjectByType<ShootingHud2D>();
            RefreshWeaponHud();
        }

        private void LateUpdate()
        {
            if (weapons != null)
                foreach (WeaponSlot weapon in weapons)
                    if (weapon.muzzleFlash != null && weapon.muzzleFlash.gameObject.activeSelf &&
                        Time.time >= weapon.FlashUntil)
                        weapon.muzzleFlash.gameObject.SetActive(false);

            bool playable = focused && targetCamera != null && hud != null && aimPointAction != null &&
                (inventory == null || !inventory.IsOpen);
            SetCombatCursor(playable);
            hud?.SetCombatActive(playable);
            if (!playable || weaponPivot == null || ActiveWeapon?.muzzle == null) return;

            if (previousAction?.WasPressedThisFrame() == true) SelectWeapon(0);
            if (nextAction?.WasPressedThisFrame() == true) SelectWeapon(1);

            Vector2 screenPoint = aimPointAction.ReadValue<Vector2>();
            var ray = targetCamera.ScreenPointToRay(screenPoint);
            var plane = new Plane(Vector3.forward, weaponPivot.position);
            aimValid = false;
            if (screenPoint.x >= 0f && screenPoint.x <= Screen.width &&
                screenPoint.y >= 0f && screenPoint.y <= Screen.height &&
                plane.Raycast(ray, out float distance))
            {
                aimWorldPoint = ray.GetPoint(distance);
                Vector2 pivotDirection = aimWorldPoint - (Vector2)weaponPivot.position;
                if (pivotDirection.sqrMagnitude > 0.0001f)
                {
                    float angle = Mathf.Atan2(pivotDirection.y, pivotDirection.x) * Mathf.Rad2Deg;
                    weaponPivot.rotation = Quaternion.Euler(0f, 0f, angle);
                    Vector2 muzzleToAim = aimWorldPoint - (Vector2)ActiveWeapon.muzzle.position;
                    aimValid = Vector2.Dot(muzzleToAim, ActiveWeapon.muzzle.right) > minimumMuzzleDistance;
                }
            }

            hud.SetCrosshair(screenPoint, aimValid);
            if (aimValid && attackAction?.IsPressed() == true) TryFire();
        }

        public void SelectWeapon(int index)
        {
            if (weapons == null || index < 0 || index >= weapons.Length) return;
            activeIndex = index;
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i].visual != null) weapons[i].visual.SetActive(i == index);
            RefreshWeaponHud();
        }

        private void TryFire()
        {
            WeaponSlot weapon = ActiveWeapon;
            if (weapon.projectilePrefab == null || Time.time < weapon.NextShotAt ||
                (!infiniteAmmo && weapon.Ammo <= 0)) return;

            Vector2 origin = weapon.muzzle.position;
            Vector2 direction = (aimWorldPoint - origin).normalized;
            ShootingProjectile2D projectile = Instantiate(weapon.projectilePrefab, origin,
                Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg));
            projectile.Fire(direction, weapon.projectileSpeed, weapon.damage, ownerCollider);
            if (!infiniteAmmo) weapon.Ammo--;
            weapon.NextShotAt = Time.time + weapon.fireInterval;
            if (weapon.muzzleFlash != null)
            {
                weapon.FlashUntil = Time.time + 0.08f;
                weapon.muzzleFlash.gameObject.SetActive(true);
            }
            RefreshWeaponHud();
        }

        private void RefreshWeaponHud()
        {
            WeaponSlot weapon = ActiveWeapon;
            if (weapon != null && hud != null)
                hud.SetWeapon(weapon.displayName, weapon.Ammo, weapon.initialAmmo, infiniteAmmo);
        }

        private void SetCombatCursor(bool active)
        {
            if (active && !cursorBaselineCaptured)
            {
                previousCursorVisible = Cursor.visible;
                previousCursorLock = Cursor.lockState;
                cursorBaselineCaptured = true;
            }
            if (active)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = false;
            }
            else if (cursorBaselineCaptured)
            {
                Cursor.lockState = previousCursorLock;
                Cursor.visible = true;
            }
        }

        private void OnApplicationFocus(bool value) => focused = value;

        private void OnDisable()
        {
            if (cursorBaselineCaptured)
            {
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
                cursorBaselineCaptured = false;
            }
            hud?.SetCombatActive(false);
        }
    }
}
