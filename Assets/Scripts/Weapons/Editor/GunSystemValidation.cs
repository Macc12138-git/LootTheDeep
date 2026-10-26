using System;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LootTheDeep.Weapons.EditorTools
{
    public static class GunSystemValidation
    {
        [MenuItem("LootTheDeep/武器/验证枪械系统")]
        public static void Run()
        {
            var table = AssetDatabase.LoadAssetAtPath<GunConfigTableSO>(GunConfigWindow.DefaultTablePath);
            Check(table != null, "configuration asset exists");
            Check(table.Validate().Count == 0, string.Join("\n", table.Validate()));
            ValidateBindings("Assets/Prefabs/Gameplay/DeepseekFishPlayer.prefab");
            ValidateBindings("Assets/Prefabs/Gameplay/Player.prefab");
            ValidateStateAndCadence();
            ValidateSpread();
            ValidateInvalidRows(table);
            Debug.Log("GUN_SYSTEM_CHECKS_PASSED: configuration bindings, RPM, ammunition, reload, instance isolation and random spread.");
        }

        private static void ValidateBindings(string path)
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var data = new SerializedObject(player.GetComponent<Gameplay.PlayerShooter2D>());
            var configs = (GunConfigTableSO)data.FindProperty("gunConfigs").objectReferenceValue;
            Check(configs != null, path + " has no configuration table");
            var slots = data.FindProperty("weapons");
            Check(slots.arraySize > 0, path + " has no weapons");
            for (int i = 0; i < slots.arraySize; i++)
            {
                var slot = slots.GetArrayElementAtIndex(i);
                string id = slot.FindPropertyRelative("weaponId").stringValue;
                Check(configs.Find(id) != null, path + " has an unknown weapon ID: " + id);
                Check(slot.FindPropertyRelative("muzzle").objectReferenceValue != null, path + " has no muzzle");
                Check(slot.FindPropertyRelative("damage") == null && slot.FindPropertyRelative("initialAmmo") == null &&
                    slot.FindPropertyRelative("fireInterval") == null, "weapon attributes must be stored outside Player");
            }
        }

        private static void ValidateStateAndCadence()
        {
            var config = new GunConfig { magazineCapacity = 2, roundsPerMinute = 600f, reloadSeconds = 1f };
            var gun = new GunRuntime(config);
            var other = new GunRuntime(config);
            Check(Mathf.Approximately(config.ShotInterval, .1f), "600 RPM must produce a .1 second interval");
            Check(!gun.TryFire(Vector2.zero, 0f, out _) && gun.Ammo == 2, "invalid aim must not consume ammunition");
            Check(gun.TryFire(Vector2.right * 5f, 0f, out Vector2 direction) && direction == Vector2.right, "zero spread must preserve normalized aim");
            Check(!gun.TryFire(Vector2.right, .099f, out _) && gun.Ammo == 1, "cooldown must block early shots");
            Check(gun.TryFire(Vector2.right, .1001f, out _) && gun.Ammo == 0, "fire must resume at the configured cadence");
            Check(!gun.TryFire(Vector2.right, 2f, out _), "empty magazine must block firing");
            Check(other.Ammo == 2 && config.magazineCapacity == 2, "shared configuration must not share ammunition state");
            Check(gun.StartReload() && !gun.StartReload(), "reload must start once");
            gun.Tick(.4f);
            Check(gun.IsReloading && Mathf.Abs(gun.ReloadProgress - .4f) < .0001f && gun.Ammo == 0, "reload must take the configured time");
            Check(!gun.TryFire(Vector2.right, 3f, out _), "reload must block firing");
            gun.Tick(.61f);
            Check(!gun.IsReloading && gun.Ammo == 2 && !gun.StartReload(), "completed reload must fill the magazine; full magazines must not reload");
            Check(other.Ammo == 2 && !other.IsReloading, "reload state must be independent per equipped gun");
        }

        private static void ValidateSpread()
        {
            var config = new GunConfig { magazineCapacity = 512, roundsPerMinute = 1200f, spreadDegrees = 3f };
            var gun = new GunRuntime(config);
            Check(Mathf.Approximately(config.ShotInterval, .05f), "1200 RPM must produce a .05 second interval");
            var previousRandom = Random.state;
            try
            {
                Random.InitState(2671);
                float minimum = 0f, maximum = 0f;
                for (int i = 0; i < config.magazineCapacity; i++)
                {
                    Check(gun.TryFire(Vector2.up, i * .06f, out Vector2 direction), "continuous fire must emit each scheduled round");
                    float angle = Vector2.SignedAngle(Vector2.up, direction);
                    Check(Mathf.Abs(angle) <= 3.001f && Mathf.Abs(direction.magnitude - 1f) < .0001f,
                        "random spread must remain inside the configured cone without changing speed");
                    minimum = Mathf.Min(minimum, angle);
                    maximum = Mathf.Max(maximum, angle);
                }
                Check(minimum < -1.5f && maximum > 1.5f, "continuous fire must spread to both sides of the aim direction");
            }
            finally { Random.state = previousRandom; }
        }

        private static void ValidateInvalidRows(GunConfigTableSO source)
        {
            var invalid = ScriptableObject.CreateInstance<GunConfigTableSO>();
            try
            {
                invalid.guns.Add(new GunConfig { id = "duplicate", roundsPerMinute = 0f, spreadDegrees = -1f,
                    projectilePrefab = source.guns[0].projectilePrefab });
                invalid.guns.Add(new GunConfig { id = "duplicate", projectilePrefab = source.guns[0].projectilePrefab });
                var errors = invalid.Validate();
                Check(errors.Exists(error => error.Contains("ID")) && errors.Exists(error => error.Contains("射速")) &&
                    errors.Exists(error => error.Contains("分散")), "invalid IDs, RPM and spread must be reported");
            }
            finally { UnityEngine.Object.DestroyImmediate(invalid); }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("GUN_SYSTEM_CHECK_FAILED: " + message);
        }
    }
}
