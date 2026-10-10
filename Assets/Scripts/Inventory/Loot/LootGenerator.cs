using System;
using System.Collections.Generic;

namespace LootTheDeep.Inventory
{
    public sealed class GeneratedLoot
    {
        public LootDefinition Definition;
        public bool CoreReward;
        public string Group;
    }

    public sealed class LootGenerator
    {
        public List<GeneratedLoot> Generate(LootTableSO table, Random random)
        {
            if (table == null) throw new ArgumentException("未配置掉落表。");
            var errors = table.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            var result = new List<GeneratedLoot>();
            foreach (var group in table.groups)
            {
                int count = random.Next(group.minRolls, group.maxRolls + 1);
                for (int i = 0; i < count; i++)
                {
                    int quality = PickQuality(group.qualityWeights, random);
                    var entry = PickItem(group.entries, quality, random);
                    result.Add(new GeneratedLoot { Definition = entry.item, CoreReward = group.coreReward, Group = group.name });
                }
            }
            return result;
        }
        private int PickQuality(float[] weights, Random random)
        {
            double total = 0;
            foreach (float weight in weights) total += weight;
            double roll = random.NextDouble() * total;
            int last = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0) continue;
                last = i;
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return last;
        }
        private LootEntry PickItem(List<LootEntry> entries, int quality, Random random)
        {
            double total = 0;
            foreach (var entry in entries) if (entry.item.quality == quality) total += entry.weight;
            double roll = random.NextDouble() * total;
            LootEntry last = null;
            foreach (var entry in entries)
            {
                if (entry.item.quality != quality || entry.weight <= 0) continue;
                last = entry;
                roll -= entry.weight;
                if (roll < 0) return entry;
            }
            return last;
        }
    }
}
