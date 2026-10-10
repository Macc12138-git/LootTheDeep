using System;
using System.Collections.Generic;
using UnityEngine;

namespace LootTheDeep.Inventory
{
    [DisallowMultipleComponent]
    public sealed class RegionContainerManager : MonoBehaviour
    {
        public string regionName = "新区域";
        public RegionLootConfigSO config;
        private readonly List<SearchableContainer> containers = new();
        public IReadOnlyList<SearchableContainer> Containers => containers;
        public event Action ContainersChanged;

        public void RefreshContainers()
        {
            containers.Clear();
            foreach (var container in GetComponentsInChildren<SearchableContainer>(true))
                if (container.FindNearestRegion() == this) containers.Add(container);
            ContainersChanged?.Invoke();
        }
        public void RegisterContainer(SearchableContainer container)
        {
            if (container == null || container.FindNearestRegion() != this || containers.Contains(container)) return;
            containers.Add(container);
            ContainersChanged?.Invoke();
        }
        public void UnregisterContainer(SearchableContainer container)
        {
            if (containers.Remove(container)) ContainersChanged?.Invoke();
        }
    }
}
