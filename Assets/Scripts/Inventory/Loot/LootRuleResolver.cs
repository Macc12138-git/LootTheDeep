using System.Collections.Generic;
using UnityEngine;

namespace LootTheDeep.Inventory
{
    public sealed class ResolvedLootRule
    {
        public LootTableSO Table;
        public LootRuleSource Source;
        public Object SourceObject;
        public string Error;
    }

    public sealed class LootRuleResolver
    {
        public ResolvedLootRule Resolve(SearchableContainer container)
        {
            if (container.LootMode == ContainerLootMode.FixedPrototype)
                return new ResolvedLootRule { Source = LootRuleSource.FixedPrototype, SourceObject = container };
            if (container.OverrideTable != null)
                return Result(container.OverrideTable, LootRuleSource.ContainerOverride, container);
            var region = container.FindNearestRegion();
            if (region != null && region.config != null)
            {
                var errors = region.config.Validate();
                if (errors.Count > 0)
                    return new ResolvedLootRule { Source = LootRuleSource.RegionDefault, SourceObject = region.config,
                        Error = string.Join("\n", errors) };
                if (region.config.TryGetTable(container.Type, out var table))
                    return Result(table, LootRuleSource.RegionDefault, region.config);
            }
            return Result(container.DefaultTable, LootRuleSource.PrefabDefault, container);
        }
        public List<string> ValidateContainer(SearchableContainer container)
        {
            var rule = Resolve(container);
            var errors = new List<string>();
            if (rule.Source == LootRuleSource.FixedPrototype) return errors;
            if (!string.IsNullOrEmpty(rule.Error)) { errors.Add(rule.Error); return errors; }
            errors.AddRange(rule.Table.Validate());
            if (errors.Count > 0) return errors;
            foreach (var group in rule.Table.groups)
                foreach (var entry in group.entries)
                    if (group.maxRolls > 0 && entry.weight > 0 && group.qualityWeights[entry.item.quality] > 0 &&
                        !((entry.item.width <= container.Columns && entry.item.height <= container.Rows) ||
                          (entry.item.height <= container.Columns && entry.item.width <= container.Rows)))
                        errors.Add("物品无法放入容器：" + entry.item.displayName);
            return errors;
        }
        private ResolvedLootRule Result(LootTableSO table, LootRuleSource source, Object owner)
        {
            return new ResolvedLootRule { Table = table, Source = table == null ? LootRuleSource.Missing : source,
                SourceObject = owner, Error = table == null ? "未配置掉落表。" : null };
        }
    }
}
