using System;
using System.Collections.Generic;
using LootTheDeep.Gameplay;
using UnityEngine;

namespace LootTheDeep.Weapons
{
    [Serializable]
    public sealed class GunConfig
    {
        [InspectorName("武器 ID")] public string id;
        [InspectorName("名称")] public string displayName;
        [InspectorName("弹容量"), Min(1)] public int magazineCapacity = 30;
        [InspectorName("射速（发/分）"), Min(1f)] public float roundsPerMinute = 600f;
        [InspectorName("换弹耗时（秒）"), Min(0.01f)] public float reloadSeconds = 1.8f;
        [InspectorName("分散半角（度）"), Range(0f, 180f)] public float spreadDegrees;
        [InspectorName("基础伤害"), Min(1)] public int baseDamage = 10;
        [InspectorName("子弹速度"), Min(0.1f)] public float projectileSpeed = 20f;
        [InspectorName("子弹预制体")] public ShootingProjectile2D projectilePrefab;

        public float ShotInterval => 60f / roundsPerMinute;
    }

    [CreateAssetMenu(menuName = "LootTheDeep/Weapons/Gun Config Table", fileName = "GunConfigs")]
    public sealed class GunConfigTableSO : ScriptableObject
    {
        [InspectorName("枪械配置")] public List<GunConfig> guns = new();

        public GunConfig Find(string id)
        {
            foreach (GunConfig gun in guns)
                if (gun != null && gun.id == id) return gun;
            return null;
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (guns == null || guns.Count == 0) { errors.Add("枪械配置表为空。"); return errors; }
            foreach (GunConfig gun in guns)
            {
                if (gun == null) { errors.Add("存在空枪械配置。"); continue; }
                if (string.IsNullOrWhiteSpace(gun.id) || !ids.Add(gun.id))
                    errors.Add("武器 ID 为空或重复：" + gun.id);
                string prefix = gun.id + "：";
                if (gun.magazineCapacity < 1) errors.Add(prefix + "弹容量必须大于零。");
                if (!PositiveFinite(gun.roundsPerMinute)) errors.Add(prefix + "射速必须为大于零的有限数。");
                if (!PositiveFinite(gun.reloadSeconds)) errors.Add(prefix + "换弹耗时必须为大于零的有限数。");
                if (float.IsNaN(gun.spreadDegrees) || float.IsInfinity(gun.spreadDegrees) ||
                    gun.spreadDegrees < 0f || gun.spreadDegrees > 180f)
                    errors.Add(prefix + "分散半角必须在 0—180 度之间。");
                if (gun.baseDamage < 1) errors.Add(prefix + "基础伤害必须大于零。");
                if (!PositiveFinite(gun.projectileSpeed)) errors.Add(prefix + "子弹速度必须为大于零的有限数。");
                if (gun.projectilePrefab == null) errors.Add(prefix + "缺少子弹预制体。");
            }
            return errors;
        }

        private static bool PositiveFinite(float value) => value > 0f && !float.IsInfinity(value);
    }
}
