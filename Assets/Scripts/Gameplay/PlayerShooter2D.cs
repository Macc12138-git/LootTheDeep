using System;
using LootTheDeep.Inventory;
using LootTheDeep.Weapons;
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
            public string weaponId;
            public GameObject visual;
            public Transform muzzle;
            public SpriteRenderer muzzleFlash;
            [NonSerialized] public GunRuntime Gun;
            [NonSerialized] public float FlashUntil;
        }

        [SerializeField] private GunConfigTableSO gunConfigs;
        [SerializeField] private Transform weaponPivot;
        [SerializeField] private WeaponSlot[] weapons;
        [SerializeField, Min(0f)] private float minimumMuzzleDistance = 0.08f;

        private PlayerInput playerInput;
        private UnderwaterMover2D mover;
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
        public GunRuntime ActiveGun => ActiveWeapon?.Gun;
        public GunConfig ActiveGunConfig => ActiveWeapon == null ? null : gunConfigs?.Find(ActiveWeapon.weaponId);

        public void Configure(GunConfigTableSO configs, Transform pivot, params WeaponSlot[] slots)
        {
            gunConfigs = configs;
            weaponPivot = pivot;
            weapons = slots;
        }

        private void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
            mover = GetComponent<UnderwaterMover2D>();
            ownerCollider = GetComponent<Collider2D>();
            health = GetComponent<CombatHealth2D>();
            if (weapons == null) return;
            foreach (WeaponSlot weapon in weapons)
            {
                GunConfig config = gunConfigs?.Find(weapon.weaponId);
                if (config == null)
                {
                    Debug.LogError("枪械配置表缺少武器：" + weapon.weaponId, this);
                    continue;
                }
                weapon.Gun = new GunRuntime(config);
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
                    if ((health == null || !health.IsDead) &&
                        (session == null || !session.IsFinished))
                        weapon.Gun?.Tick(Time.deltaTime);
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
            if (!playable || weaponPivot == null || ActiveWeapon?.muzzle == null || ActiveGun == null) return;

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
                    mover?.SetAimDirection(pivotDirection);
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
            if (health != null && health.IsDead || session != null && session.IsFinished) return;
            ActiveGun?.StartReload();
            RefreshWeaponHud();
        }

        private void TryFire()
        {
            WeaponSlot weapon = ActiveWeapon;
            GunRuntime gun = ActiveGun;
            if (gun == null || gun.Config.projectilePrefab == null ||
                health != null && health.IsDead || session != null && session.IsFinished) return;

            Vector2 origin = weapon.muzzle.position;
            if (!gun.TryFire(aimWorldPoint - origin, Time.time, out Vector2 direction)) return;
            ShootingProjectile2D projectile = Instantiate(gun.Config.projectilePrefab, origin,
                Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg));
            projectile.Fire(direction, gun.Config.projectileSpeed, gun.Config.baseDamage, ownerCollider);
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
            GunRuntime gun = ActiveGun;
            if (gun != null && hud != null)
                hud.SetWeapon(gun.Config.displayName, gun.Ammo, gun.IsReloading, gun.ReloadProgress);
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
