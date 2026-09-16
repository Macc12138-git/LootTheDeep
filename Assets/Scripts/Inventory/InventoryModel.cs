using System;
using System.Collections.Generic;
using UnityEngine;

namespace LootTheDeep.Inventory
{
    public sealed class LootItem
    {
        public readonly LootDefinition Definition;
        public GridInventory Owner { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public bool Rotated { get; internal set; }
        public bool Revealed { get; internal set; }
        public int Width => Rotated ? Definition.height : Definition.width;
        public int Height => Rotated ? Definition.width : Definition.height;
        public LootItem(LootDefinition definition, bool revealed = true)
        {
            Definition = definition;
            Revealed = revealed;
        }
    }

    // UI and search presentation never own the items. Transfers commit only after validation.
    public sealed class GridInventory
    {
        public readonly string Name;
        public readonly int Columns;
        public readonly int Rows;
        private readonly List<LootItem> items = new();
        public IReadOnlyList<LootItem> Items => items;
        public int Occupied
        {
            get { int result = 0; foreach (var item in items) result += item.Width * item.Height; return result; }
        }
        public GridInventory(string name, int columns, int rows)
        {
            Name = name; Columns = columns; Rows = rows;
        }
        public bool CanPlace(LootItem item, int x, int y, bool rotated)
        {
            int w = rotated ? item.Definition.height : item.Definition.width;
            int h = rotated ? item.Definition.width : item.Definition.height;
            if (x < 0 || y < 0 || x + w > Columns || y + h > Rows) return false;
            foreach (var other in items)
                if (other != item && x < other.X + other.Width && x + w > other.X &&
                    y < other.Y + other.Height && y + h > other.Y) return false;
            return true;
        }
        public bool TryPlace(LootItem item, int x, int y, bool rotated)
        {
            if (!item.Revealed || !CanPlace(item, x, y, rotated)) return false;
            Commit(item, x, y, rotated);
            return true;
        }
        public bool TryAddFirst(LootItem item)
        {
            if (!item.Revealed) return false;
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Columns; x++)
                    if (TryPlace(item, x, y, item.Rotated)) return true;
            return false;
        }
        public void Seed(LootItem item, int x, int y)
        {
            if (item.Owner != null || !CanPlace(item, x, y, false))
                throw new InvalidOperationException("Invalid initial inventory placement.");
            Commit(item, x, y, false);
        }
        private void Commit(LootItem item, int x, int y, bool rotated)
        {
            item.Owner?.items.Remove(item);
            item.Owner = this; item.X = x; item.Y = y; item.Rotated = rotated;
            items.Add(item);
        }
        public LootItem NextUnknown()
        {
            LootItem next = null;
            foreach (var item in items)
                if (!item.Revealed && (next == null || item.Y < next.Y || item.Y == next.Y && item.X < next.X)) next = item;
            return next;
        }
    }
}
