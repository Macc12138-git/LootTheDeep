using UnityEngine;

namespace LootTheDeep.Weapons
{
    // Each equipped gun owns its state; the shared configuration is never used to store ammunition or timers.
    public sealed class GunRuntime
    {
        public GunConfig Config { get; }
        public int Ammo { get; private set; }
        public float ReloadRemaining { get; private set; }
        public bool IsReloading => ReloadRemaining > 0f;
        public float ReloadProgress => IsReloading ? 1f - ReloadRemaining / reloadDuration : 0f;

        private float nextShotAt;
        private float reloadDuration;

        public GunRuntime(GunConfig config)
        {
            Config = config;
            Ammo = config.magazineCapacity;
        }

        public bool StartReload()
        {
            if (IsReloading || Ammo >= Config.magazineCapacity) return false;
            reloadDuration = Config.reloadSeconds;
            ReloadRemaining = reloadDuration;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!IsReloading) return;
            ReloadRemaining = Mathf.Max(0f, ReloadRemaining - deltaTime);
            if (!IsReloading) Ammo = Config.magazineCapacity;
        }

        public bool TryFire(Vector2 aimDirection, float time, out Vector2 shotDirection)
        {
            shotDirection = Vector2.zero;
            if (IsReloading || Ammo <= 0 || time < nextShotAt || aimDirection.sqrMagnitude < 0.0001f) return false;
            float spread = Config.spreadDegrees == 0f ? 0f : Random.Range(-Config.spreadDegrees, Config.spreadDegrees);
            shotDirection = Quaternion.Euler(0f, 0f, spread) * aimDirection.normalized;
            Ammo--;
            nextShotAt = time + Config.ShotInterval;
            return true;
        }
    }
}
