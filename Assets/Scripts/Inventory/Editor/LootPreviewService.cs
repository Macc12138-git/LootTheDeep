using System.Collections.Generic;
using System.Text;

namespace LootTheDeep.Inventory.EditorTools
{
    public sealed class LootPreviewService
    {
        private readonly LootGenerator generator = new();
        private readonly LootPlacementPlanner planner = new();

        public string CalculatePerRollProbabilities(LootTableSO table)
        {
            var errors = table.Validate();
            if (errors.Count > 0) return string.Join("\n", errors);
            var text = new StringBuilder();
            string[] names = { "普通", "少见", "稀有", "珍稀" };
            foreach (var group in table.groups)
            {
                text.AppendLine($"{group.name}：{group.minRolls}—{group.maxRolls} 份（每份概率）");
                double qualityTotal = 0;
                foreach (float weight in group.qualityWeights) qualityTotal += weight;
                for (int quality = 0; quality < 4; quality++)
                {
                    double probability = group.qualityWeights[quality] / qualityTotal;
                    text.AppendLine($"  {names[quality]}：{probability:P2}");
                    double itemTotal = 0;
                    foreach (var entry in group.entries) if (entry.item.quality == quality) itemTotal += entry.weight;
                    foreach (var entry in group.entries)
                        if (entry.item.quality == quality && entry.weight > 0)
                            text.AppendLine($"    {entry.item.displayName}：档内 {entry.weight / itemTotal:P2}；每份 {probability * entry.weight / itemTotal:P3}");
                }
            }
            return text.ToString();
        }
        public string Simulate(LootTableSO table, int columns, int rows, int sampleCount, int seed)
        {
            var errors = table.Validate();
            if (errors.Count > 0) return string.Join("\n", errors);
            var random = new System.Random(seed);
            var boxOccurrences = new Dictionary<LootDefinition, int>();
            int failed = 0, totalItems = 0;
            string example = "";
            for (int sample = 0; sample < sampleCount; sample++)
            {
                var items = generator.Generate(table, random);
                totalItems += items.Count;
                var unique = new HashSet<LootDefinition>();
                foreach (var item in items)
                    if (unique.Add(item.Definition))
                        boxOccurrences[item.Definition] = boxOccurrences.GetValueOrDefault(item.Definition) + 1;
                bool fits = planner.TryPlan(items, columns, rows, out var placements, out var error);
                if (!fits) failed++;
                if (sample == 0)
                {
                    var first = new StringBuilder("首个样本：\n");
                    if (!fits) first.AppendLine(error);
                    else foreach (var placement in placements)
                        first.AppendLine($"{placement.Definition.displayName} @ ({placement.X},{placement.Y}) {(placement.Rotated ? "旋转" : "")}");
                    example = first.ToString();
                }
            }
            var text = new StringBuilder($"样本 {sampleCount}，种子 {seed}，平均 {totalItems / (double)sampleCount:F2} 份\n摆放失败 {failed}/{sampleCount}（未剔除失败样本）\n以下为抽取结果整箱至少出现一次的模拟比例，非实际入库率：\n");
            foreach (var pair in boxOccurrences)
                text.AppendLine($"{pair.Key.displayName}：{pair.Value / (double)sampleCount:P2}");
            text.AppendLine(example);
            return text.ToString();
        }
    }
}
