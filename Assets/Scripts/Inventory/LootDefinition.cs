using System;
using UnityEngine;

namespace LootTheDeep.Inventory
{
    [CreateAssetMenu(menuName = "LootTheDeep/Loot Definition")]
    public sealed class LootDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        [Min(1)] public int width = 1;
        [Min(1)] public int height = 1;
        [Range(0, 3)] public int quality;
        public Sprite icon;
    }

    [Serializable]
    public struct InventorySeed
    {
        public LootDefinition definition;
        public int area;
        public Vector2Int position;
    }
}
