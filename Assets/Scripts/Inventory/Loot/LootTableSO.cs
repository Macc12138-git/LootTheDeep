using System;
using System.Collections.Generic;
using UnityEngine;

namespace LootTheDeep.Inventory
{
    public enum ContainerType { StorageCabinet, MaintenanceBox, MedicalCabinet, EquipmentCrate }
    public enum ContainerLootMode { FixedPrototype, RandomTable }
    public enum LootRuleSource { Missing, ContainerOverride, RegionDefault, PrefabDefault, FixedPrototype }

    [Serializable]
    public sealed class LootEntry
    {
        public LootDefinition item;
        [Min(0)] public float weight = 1;
    }

    [Serializable]
    public sealed class LootRollGroup
    {
        public string name = "普通奖励";
        public bool coreReward;
        [Min(0)] public int minRolls = 1;
        [Min(0)] public int maxRolls = 3;
        public float[] qualityWeights = { 75, 22, 2.8f, .2f };
        public List<LootEntry> entries = new();
    }

    [CreateAssetMenu(menuName = "LootTheDeep/Loot Table")]
    public sealed class LootTableSO : ScriptableObject
    {
        public List<LootRollGroup> groups = new();

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (groups == null || groups.Count == 0) { errors.Add("掉落表没有抽取组。"); return errors; }
            foreach (var group in groups)
            {
                if (group == null) { errors.Add("抽取组为空。"); continue; }
                string prefix = group.name + ": ";
                if (group.minRolls < 0 || group.maxRolls < group.minRolls || group.maxRolls > 1000)
                    errors.Add(prefix + "份数范围必须为 0—1000，且最小值不大于最大值。");
                if (group.qualityWeights == null || group.qualityWeights.Length != 4)
                { errors.Add(prefix + "品质权重必须恰好有四项（普通／少见／稀有／珍稀）。"); continue; }
                double total = 0;
                foreach (float weight in group.qualityWeights)
                {
                    if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0)
                        errors.Add(prefix + "品质权重必须为非负有限数。");
                    total += weight;
                }
                if (total <= 0) errors.Add(prefix + "品质总权重必须大于零。");
                var sums = new double[4];
                var seen = new HashSet<LootDefinition>();
                if (group.entries == null) { errors.Add(prefix + "缺少候选物品。"); continue; }
                foreach (var entry in group.entries)
                {
                    if (entry == null || entry.item == null) { errors.Add(prefix + "存在空物品引用。"); continue; }
                    if (!seen.Add(entry.item)) errors.Add(prefix + "重复物品：" + entry.item.displayName);
                    if (entry.item.quality < 0 || entry.item.quality > 3 || entry.item.width < 1 || entry.item.height < 1)
                    { errors.Add(prefix + "物品品质或尺寸无效：" + entry.item.name); continue; }
                    if (float.IsNaN(entry.weight) || float.IsInfinity(entry.weight) || entry.weight < 0)
                        errors.Add(prefix + "物品权重必须为非负有限数。");
                    else sums[entry.item.quality] += entry.weight;
                }
                for (int quality = 0; quality < 4; quality++)
                    if (group.qualityWeights[quality] > 0 && sums[quality] <= 0)
                        errors.Add(prefix + "品质 " + quality + " 没有正权重候选物品。");
            }
            return errors;
        }
    }
}
