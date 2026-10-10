using UnityEngine;
using LootTheDeep.Gameplay;

namespace LootTheDeep.Inventory
{
    public sealed class SearchableContainer : MonoBehaviour
    {
        [SerializeField] private ContainerType type = ContainerType.MaintenanceBox;
        [SerializeField] private ContainerLootMode lootMode;
        [SerializeField] private LootTableSO defaultTable;
        [SerializeField] private LootTableSO overrideTable;
        public ContainerType Type => type;
        public ContainerLootMode LootMode => lootMode;
        public LootTableSO DefaultTable => defaultTable;
        public LootTableSO OverrideTable => overrideTable;
        public int Columns => columns;
        public int Rows => rows;
        public string InitializationError { get; private set; }
        private bool initializationAttempted;
        private RegionContainerManager registeredRegion;
        public string displayName = "沉船维修箱";
        [Min(0.1f)] public float searchSeconds = 1.5f;
        [SerializeField, Min(1)] private int columns = 6;
        [SerializeField, Min(1)] private int rows = 4;
        [SerializeField] private InventorySeed[] initialItems;
        [SerializeField] private SpriteRenderer visual;
        public GridInventory Contents { get; private set; }
        public int FoundCount { get; private set; }
        public float Progress { get; private set; }
        public bool Searching { get; private set; }
        public LootItem Current => Contents?.NextUnknown();
        private Color originalColor;

        public void ConfigureFixedPrototype(string name, LootDefinition definition)
        {
            displayName = name;
            lootMode = ContainerLootMode.FixedPrototype;
            initialItems = definition == null ? System.Array.Empty<InventorySeed>() : new[]
            {
                new InventorySeed { definition = definition, position = Vector2Int.zero }
            };
        }

        public RegionContainerManager FindNearestRegion()
        {
            return GetComponentInParent<RegionContainerManager>(true);
        }
        public void Initialize()
        {
            Initialize(new LootRuleResolver(), new LootGenerator(), new LootPlacementPlanner(), new System.Random());
        }
        public bool Initialize(LootRuleResolver resolver, LootGenerator generator,
            LootPlacementPlanner planner, System.Random random)
        {
            if (initializationAttempted) return Contents != null;
            initializationAttempted = true;
            if (visual != null) originalColor = visual.color;
            try
            {
                var inventory = new GridInventory(displayName, columns, rows);
                if (lootMode == ContainerLootMode.FixedPrototype)
                {
                    if (initialItems != null)
                        foreach (var seed in initialItems)
                            inventory.Seed(new LootItem(seed.definition, false), seed.position.x, seed.position.y);
                }
                else
                {
                    var errors = resolver.ValidateContainer(this);
                    if (errors.Count > 0) throw new System.InvalidOperationException(string.Join("\n", errors));
                    var generated = generator.Generate(resolver.Resolve(this).Table, random);
                    if (!planner.TryPlan(generated, columns, rows, out var placements, out var error))
                        throw new System.InvalidOperationException(error);
                    foreach (var placement in placements)
                        inventory.SeedGenerated(new LootItem(placement.Definition, false),
                            placement.X, placement.Y, placement.Rotated);
                }
                Contents = inventory;
                return true;
            }
            catch (System.Exception exception)
            {
                InitializationError = exception.Message;
                Debug.LogError(displayName + "：" + InitializationError, this);
                return false;
            }
        }
        private void OnEnable() { UpdateRegionRegistration(); }
        private void OnTransformParentChanged() { UpdateRegionRegistration(); }
        private void UpdateRegionRegistration()
        {
            var region = FindNearestRegion();
            if (registeredRegion != region)
            {
                if (registeredRegion != null) registeredRegion.UnregisterContainer(this);
                registeredRegion = region;
            }
            if (registeredRegion != null) registeredRegion.RegisterContainer(this);
        }
        private void OnDestroy()
        {
            if (registeredRegion != null) registeredRegion.UnregisterContainer(this);
        }
        public void Highlight(bool value)
        {
            if (visual != null) visual.color = value ? new Color(1f, .87f, .49f) : originalColor;
        }
        public void BeginSearch()
        {
            Searching = Current != null;
            Progress = 0f;
            if (Searching) CombatNoise2D.Emit(transform.position, 5f);
        }
        public void PauseSearch() { Searching = false; Progress = 0f; }
        public bool Tick(float deltaTime)
        {
            if (!Searching || Current == null) return false;
            Progress += deltaTime / searchSeconds;
            if (Progress < 1f) return false;
            Current.Revealed = true;
            FoundCount++;
            Progress = 0f;
            Searching = Current != null;
            return true;
        }
        private void OnDisable() { PauseSearch(); Highlight(false); }
    }
}
