using System;
using System.Collections.Generic;
using UnityEngine;

namespace LootTheDeep.Inventory
{
    [Serializable]
    public sealed class ContainerLootBinding
    {
        public ContainerType type;
        public LootTableSO table;
    }

    [CreateAssetMenu(menuName = "LootTheDeep/Region Loot Config")]
    public sealed class RegionLootConfigSO : ScriptableObject
    {
        public List<ContainerLootBinding> bindings = new();
        public bool TryGetTable(ContainerType type, out LootTableSO table)
        {
            table = null;
            if (bindings == null) return false;
            foreach (var binding in bindings)
                if (binding != null && binding.type == type) { table = binding.table; return true; }
            return false;
        }
        public List<string> Validate()
        {
            var errors = new List<string>();
            var types = new HashSet<ContainerType>();
            if (bindings == null) { errors.Add("区域映射列表为空。"); return errors; }
            foreach (var binding in bindings)
            {
                if (binding == null) { errors.Add("存在空区域映射。"); continue; }
                if (!types.Add(binding.type)) errors.Add("容器类型映射重复：" + binding.type);
                if (binding.table == null) errors.Add("映射没有掉落表：" + binding.type);
            }
            return errors;
        }
    }
}
