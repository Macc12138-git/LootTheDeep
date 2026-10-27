using System.Collections.Generic;
using LootTheDeep.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LootTheDeep.Inventory
{
    public sealed class InventorySession : MonoBehaviour
    {
        public UnderwaterMover2D player;
        public SearchableContainer[] containers;
        [SerializeField] private InventoryView view;
        [SerializeField] private InventorySeed[] initialItems;
        [Min(.1f)] public float interactDistance = 1.8f;
        public readonly List<GridInventory> Storage = new();
        public GridInventory Backpack => Storage[0];
        public InventoryView View => view;
        public SearchableContainer Target { get; private set; }
        public bool IsOpen { get; private set; }
        private readonly List<SearchableContainer> activeContainers = new();
        private readonly List<RegionContainerManager> regions = new();
        private readonly LootRuleResolver lootResolver = new();
        private readonly LootGenerator lootGenerator = new();
        private readonly LootPlacementPlanner lootPlanner = new();
        private readonly System.Random lootRandom = new();
        private bool started;
        private SearchableContainer nearby;
        private CursorLockMode previousLock;
        private bool previousCursor;
        private bool hasCursorState;

        private void Start()
        {
            foreach (var grid in View.PlayerGrids)
                Storage.Add(new GridInventory(grid.displayName, grid.columns, grid.rows));
            foreach (var seed in initialItems)
                Storage[seed.area].Seed(new LootItem(seed.definition), seed.position.x, seed.position.y);
            started = true;
            ConnectRegions();
            View.Bind(this);
            View.Show(false);
        }
        private void Update()
        {
            if (View == null || player == null) return;
            if (IsOpen && Target != null && !CanInteract(Target)) Close();
            // Also handle a container destroyed while its page was open.
            if (IsOpen && View.HasContainer && Target == null) Close();
            if (!IsOpen) FindNearby();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (View.IsDragging) View.CancelDrag(); else Close();
                }
                else if (keyboard.tabKey.wasPressedThisFrame) { if (IsOpen) Close(); else Open(null); }
                else if (keyboard.fKey.wasPressedThisFrame && !IsOpen && nearby != null) Open(nearby);
                if (IsOpen && keyboard.rKey.wasPressedThisFrame) View.Rotate();
            }
            if (IsOpen && Target != null)
            {
                if (Target.Tick(Time.deltaTime)) View.Refresh();
                View.UpdateSearch();
            }
        }
        private void FindNearby()
        {
            SearchableContainer candidate = null;
            float distance = interactDistance;
            foreach (var container in activeContainers)
            {
                if (container == null || !container.isActiveAndEnabled || container.Contents == null) continue;
                float next = Vector2.Distance(player.transform.position, container.transform.position);
                if (next <= distance && CanInteract(container)) { distance = next; candidate = container; }
            }
            if (nearby != candidate)
            {
                if (nearby != null) nearby.Highlight(false);
                nearby = candidate;
                if (nearby != null) nearby.Highlight(true);
            }
            View.SetPrompt(nearby == null ? "Tab  随身物品" : "F  搜索 · " + nearby.displayName + "     Tab  随身物品");
        }
        private bool CanInteract(SearchableContainer container)
        {
            if (player == null || !container.isActiveAndEnabled || container.Contents == null ||
                Vector2.Distance(player.transform.position, container.transform.position) > interactDistance) return false;
            foreach (var hit in Physics2D.LinecastAll(player.transform.position, container.transform.position))
            {
                var collider = hit.collider;
                if (collider.isTrigger || collider.transform.IsChildOf(player.transform) ||
                    collider.transform.IsChildOf(container.transform) ||
                    collider.attachedRigidbody != null && collider.attachedRigidbody.bodyType != RigidbodyType2D.Static)
                    continue;
                return false;
            }
            return true;
        }
        public void Open(SearchableContainer container)
        {
            if (View == null || (container != null && !CanInteract(container))) return;
            if (!IsOpen)
            {
                previousLock = Cursor.lockState; previousCursor = Cursor.visible; hasCursorState = true;
            }
            Target?.PauseSearch(); Target = container; IsOpen = true;
            player.SetInventoryOpen(true);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            View.Show(true); Target?.BeginSearch(); View.Refresh();
        }
        public void Close()
        {
            Target?.PauseSearch(); Target = null; IsOpen = false;
            if (player != null) player.SetInventoryOpen(false);
            if (View != null) { View.CancelDrag(); View.Show(false); }
            if (hasCursorState) { Cursor.lockState = previousLock; Cursor.visible = previousCursor; hasCursorState = false; }
        }
        public void ToggleSearch()
        {
            if (Target == null) return;
            if (Target.Searching) Target.PauseSearch(); else Target.BeginSearch();
            View.Refresh();
        }
        public void QuickTransfer(LootItem item)
        {
            if (item == null || Target == null) { View.Notify("打开附近容器后才能快捷转移"); return; }
            GridInventory destination = item.Owner == Target.Contents ? Backpack : Target.Contents;
            bool success = destination.TryAddFirst(item);
            View.Notify(success ? "已移入" + destination.Name : "没有足够的连续空间，请先整理或旋转");
            View.Refresh();
        }
        private void OnApplicationFocus(bool focus)
        {
            if (!focus && View != null) { View.CancelDrag(); Target?.PauseSearch(); if (IsOpen) View.Refresh(); }
        }
        private void ConnectRegions()
        {
            DisconnectRegions();
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var region in root.GetComponentsInChildren<RegionContainerManager>(true))
                {
                    region.RefreshContainers();
                    regions.Add(region);
                    region.ContainersChanged += RefreshRegionContainers;
                }
            RefreshRegionContainers();
        }
        private void RefreshRegionContainers()
        {
            activeContainers.Clear();
            foreach (var region in regions)
                if (region != null)
                    foreach (var container in region.Containers) AddContainer(container);
            // Compatibility for existing prototype scenes. Region-managed boxes need no manual registration.
            if (containers != null)
                foreach (var container in containers)
                    if (container != null && container.FindNearestRegion() == null) AddContainer(container);
        }
        private void AddContainer(SearchableContainer container)
        {
            if (container == null || activeContainers.Contains(container)) return;
            activeContainers.Add(container);
            container.Initialize(lootResolver, lootGenerator, lootPlanner, lootRandom);
        }

        public void RegisterPrototypeContainer(SearchableContainer container)
        {
            if (container != null && container.isActiveAndEnabled) AddContainer(container);
        }
        private void DisconnectRegions()
        {
            foreach (var region in regions)
                if (region != null) region.ContainersChanged -= RefreshRegionContainers;
            regions.Clear();
        }
        private void OnEnable() { if (started) ConnectRegions(); }
        private void OnDisable() { DisconnectRegions(); Close(); }
    }
}
