using System.Collections.Generic;

namespace LootTheDeep.Inventory
{
    public sealed class LootPlacement
    {
        public LootDefinition Definition;
        public int X, Y;
        public bool Rotated;
    }

    public sealed class LootPlacementPlanner
    {
        // A deterministic bounded packing plan. Failure never rerolls or discards loot.
        public bool TryPlan(List<GeneratedLoot> items, int columns, int rows,
            out List<LootPlacement> placements, out string error)
        {
            placements = new List<LootPlacement>();
            error = null;
            var ordered = new List<GeneratedLoot>(items);
            ordered.Sort(CompareItems);
            var occupied = new bool[columns, rows];
            foreach (var item in ordered)
            {
                if (!TryFindSpace(occupied, item.Definition, out var placement))
                {
                    error = "空间不足，未生成任何物资：" + item.Definition.displayName + "。请减少份数或扩大容器。";
                    placements.Clear();
                    return false;
                }
                placements.Add(placement);
                int width = placement.Rotated ? item.Definition.height : item.Definition.width;
                int height = placement.Rotated ? item.Definition.width : item.Definition.height;
                for (int x = placement.X; x < placement.X + width; x++)
                    for (int y = placement.Y; y < placement.Y + height; y++) occupied[x, y] = true;
            }
            return true;
        }
        private int CompareItems(GeneratedLoot a, GeneratedLoot b)
        {
            int core = b.CoreReward.CompareTo(a.CoreReward);
            if (core != 0) return core;
            int size = (b.Definition.width * b.Definition.height).CompareTo(a.Definition.width * a.Definition.height);
            return size != 0 ? size : string.CompareOrdinal(a.Definition.id, b.Definition.id);
        }
        private bool TryFindSpace(bool[,] occupied, LootDefinition item, out LootPlacement placement)
        {
            for (int y = 0; y < occupied.GetLength(1); y++)
                for (int x = 0; x < occupied.GetLength(0); x++)
                    for (int rotation = 0; rotation < 2; rotation++)
                    {
                        int width = rotation == 0 ? item.width : item.height;
                        int height = rotation == 0 ? item.height : item.width;
                        if (x + width > occupied.GetLength(0) || y + height > occupied.GetLength(1)) continue;
                        bool fits = true;
                        for (int dx = x; dx < x + width; dx++)
                            for (int dy = y; dy < y + height; dy++) if (occupied[dx, dy]) fits = false;
                        if (!fits) continue;
                        placement = new LootPlacement { Definition = item, X = x, Y = y, Rotated = rotation == 1 };
                        return true;
                    }
            placement = null;
            return false;
        }
    }
}
