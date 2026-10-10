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
            [Min(0.1f)] public float reloadSeconds = 1.8f;

            [NonSerialized] public int Ammo;
            [NonSerialized] public float NextShotAt;
            [NonSerialized] public float FlashUntil;
            [NonSerialized] public float ReloadRemaining;
            public bool IsReloading => ReloadRemaining > 0f;
            public float ReloadProgress => IsReloading ? 1f - ReloadRemaining / reloadSeconds : 0f;
        }

        [SerializeField] private Transform weaponPivot;
        [SerializeField] private WeaponSlot[] weapons;
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
        private CombatHealth2D health;
        private CombatPrototypeSession2D session;
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
            health = GetComponent<CombatHealth2D>();
            if (weapons == null) return;
            foreach (WeaponSlot weapon in weapons)
            {
                weapon.Ammo = weapon.initialAmmo;
                weapon.NextShotAt = 0f;
                weapon.ReloadRemaining = 0f;
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
            session = FindFirstObjectByType<CombatPrototypeSession2D>();
            RefreshWeaponHud();
        }

        private void LateUpdate()
        {
            if (weapons != null)
                foreach (WeaponSlot weapon in weapons)
                {
                    if (weapon.IsReloading && health != null && !health.IsDead &&
                        (session == null || !session.IsFinished))
                    {
                        weapon.ReloadRemaining = Mathf.Max(0f, weapon.ReloadRemaining - Time.deltaTime);
                        if (!weapon.IsReloading) weapon.Ammo = weapon.initialAmmo;
                    }
                    if (weapon.muzzleFlash != null && weapon.muzzleFlash.gameObject.activeSelf &&
                        Time.time >= weapon.FlashUntil)
                        weapon.muzzleFlash.gameObject.SetActive(false);
                }

            bool playable = focused && targetCamera != null && hud != null && aimPointAction != null &&
                (inventory == null || !inventory.IsOpen) && (health == null || !health.IsDead) &&
                (session == null || !session.IsFinished);
            SetCombatCursor(playable);
            RefreshWeaponHud();
            hud?.SetCombatActive(playable, inventory != null && inventory.IsOpen &&
                (health == null || !health.IsDead) && (session == null || !session.IsFinished));
            if (!playable || weaponPivot == null || ActiveWeapon?.muzzle == null) return;

            if (previousAction?.WasPressedThisFrame() == true) SelectWeapon(0);
            if (nextAction?.WasPressedThisFrame() == true) SelectWeapon(1);
            if (Keyboard.current?.rKey.wasPressedThisFrame == true) Reload();

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

        public void Reload()
        {
            WeaponSlot weapon = ActiveWeapon;
            if (weapon == null || weapon.IsReloading || weapon.Ammo >= weapon.initialAmmo ||
                health != null && health.IsDead || session != null && session.IsFinished) return;
            weapon.ReloadRemaining = weapon.reloadSeconds;
            RefreshWeaponHud();
        }

        private void TryFire()
        {
            WeaponSlot weapon = ActiveWeapon;
            if (weapon == null || weapon.projectilePrefab == null || weapon.IsReloading ||
                Time.time < weapon.NextShotAt || weapon.Ammo <= 0 ||
                health != null && health.IsDead || session != null && session.IsFinished) return;

            Vector2 origin = weapon.muzzle.position;
            Vector2 direction = (aimWorldPoint - origin).normalized;
            ShootingProjectile2D projectile = Instantiate(weapon.projectilePrefab, origin,
                Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg));
            projectile.Fire(direction, weapon.projectileSpeed, weapon.damage, ownerCollider);
            weapon.Ammo--;
            weapon.NextShotAt = Time.time + weapon.fireInterval;
            CombatNoise2D.Emit(transform.position, 8f);
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
                hud.SetWeapon(weapon.displayName, weapon.Ammo, weapon.IsReloading, weapon.ReloadProgress);
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
            hud?.SetCombatActive(false, false);
        }
    }
}
