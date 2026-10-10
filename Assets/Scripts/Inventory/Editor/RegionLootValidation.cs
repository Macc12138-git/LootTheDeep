using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LootTheDeep.Inventory.EditorTools
{
    // Batch validation entry; run in an isolated project copy because it enters Play Mode.
    public sealed class RegionLootValidation
    {
        private int assertions;
        private double playStarted;
        private bool completed;
        private readonly List<string> unexpectedErrors = new();
        public static void Run() { new RegionLootValidation().ScheduleChecks(); }
        private double checksStartAt;
        private void ScheduleChecks()
        {
            // Let editor startup callbacks (including Search indexing) finish before entering Play Mode.
            checksStartAt = EditorApplication.timeSinceStartup + 5;
            EditorApplication.update += WaitForEditorStartup;
        }
        private void WaitForEditorStartup()
        {
            if (EditorApplication.timeSinceStartup < checksStartAt || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= WaitForEditorStartup;
            RunChecks();
        }
        private void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("VALIDATION FAILED: " + message);
            assertions++;
        }
        private void RunChecks()
        {
            Application.logMessageReceived += CaptureError;
            try
            {
                new RegionLootInstaller().InstallScene();
                ValidateDataAndEditor();
                EditorSceneManager.OpenScene("Assets/Scenes/ExploreScene.unity");
                // Only the disposable batch project changes these settings.
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                EditorApplication.playModeStateChanged += OnPlayState;
                EditorApplication.update += WaitForPlay;
                playStarted = EditorApplication.timeSinceStartup;
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception) { Fail(exception); }
        }
        private void ValidateDataAndEditor()
        {
            var resolver = new LootRuleResolver();
            var generator = new LootGenerator();
            var planner = new LootPlacementPlanner();
            var regions = Object.FindObjectsByType<RegionContainerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Check(regions.Length == 2, "two installed regions");
            var boxes = regions.SelectMany(r => { r.RefreshContainers(); return r.Containers; }).ToArray();
            Check(boxes.Length == 4 && boxes.Distinct().Count() == 4, "four uniquely owned containers");
            Check(boxes.Select(c => c.Type).Distinct().Count() == 3, "three types");
            foreach (var box in boxes)
            {
                Check(resolver.ValidateContainer(box).Count == 0, "valid sample table " + box.name);
                var random = new System.Random(42);
                for (int i = 0; i < 100; i++)
                    Check(planner.TryPlan(generator.Generate(resolver.Resolve(box).Table, random), box.Columns, box.Rows, out _, out _), "sample capacity");
            }
            var special = boxes.Single(c => c.OverrideTable != null);
            Check(resolver.Resolve(special).Source == LootRuleSource.ContainerOverride, "override priority");
            var actions = new LootConfigEditorActions();
            var table = special.OverrideTable;
            actions.BatchClearOverrides(new[] { special });
            Check(resolver.Resolve(special).Source == LootRuleSource.PrefabDefault, "fallback to prefab");
            Undo.PerformUndo();
            Check(special.OverrideTable == table, "clear override undo");
            Undo.PerformRedo();
            Check(special.OverrideTable == null, "clear override redo");
            actions.BatchSetOverride(new[] { special }, table);
            var inherited = boxes.First(c => c.OverrideTable == null);
            Check(resolver.Resolve(inherited).Source == LootRuleSource.RegionDefault, "region priority");
            actions.BatchSetOverride(new[] { inherited }, table);
            Check(inherited.OverrideTable == table && boxes.Count(c => c.OverrideTable != null) == 2, "batch only explicit targets");
            Undo.PerformUndo();
            Check(inherited.OverrideTable == null, "batch undo");

            var parent = regions[0];
            var nestedObject = new GameObject("ValidationNestedRegion");
            nestedObject.transform.SetParent(parent.transform);
            var nested = nestedObject.AddComponent<RegionContainerManager>();
            var child = new GameObject("ValidationChild");
            child.transform.SetParent(nested.transform);
            var nestedBox = child.AddComponent<SearchableContainer>();
            child.SetActive(false);
            parent.RefreshContainers(); nested.RefreshContainers();
            Check(!parent.Containers.Contains(nestedBox) && nested.Containers.Contains(nestedBox), "nearest region incl inactive");
            Object.DestroyImmediate(nestedObject);

            var itemA = ScriptableObject.CreateInstance<LootDefinition>();
            itemA.id = "a"; itemA.width = 2; itemA.height = 1; itemA.quality = 0;
            var itemB = ScriptableObject.CreateInstance<LootDefinition>();
            itemB.id = "b"; itemB.width = 1; itemB.height = 1; itemB.quality = 0;
            var test = ScriptableObject.CreateInstance<LootTableSO>();
            test.groups.Add(new LootRollGroup { minRolls = 1, maxRolls = 1, qualityWeights = new[] { 1f, 0, 0, 0 },
                entries = new List<LootEntry> { new LootEntry { item = itemA, weight = 30 }, new LootEntry { item = itemB, weight = 70 } } });
            int countA = 0;
            var rng = new System.Random(123);
            for (int i = 0; i < 20000; i++) if (generator.Generate(test, rng)[0].Definition == itemA) countA++;
            Check(Math.Abs(countA / 20000.0 - .3) < .02, "weighted distribution");
            test.groups[0].qualityWeights[1] = 1;
            Check(test.Validate().Count > 0, "missing quality pool rejected");
            test.groups[0].qualityWeights[1] = 0;
            var items = new List<GeneratedLoot> { new GeneratedLoot { Definition = itemA, CoreReward = true } };
            Check(planner.TryPlan(items, 1, 2, out var placements, out _) && placements[0].Rotated, "rotation placement");
            Check(!planner.TryPlan(items, 1, 1, out placements, out _) && placements.Count == 0, "failure has no partial drops");
            var grid = new GridInventory("test", 1, 2);
            var hidden = new LootItem(itemA, false);
            grid.SeedGenerated(hidden, 0, 0, true);
            Check(!new GridInventory("target", 2, 2).TryAddFirst(hidden), "hidden item not transferable");
            Object.DestroyImmediate(test); Object.DestroyImmediate(itemA); Object.DestroyImmediate(itemB);

            string clonePath = "Assets/Data/Inventory/Loot/ValidationClone.asset";
            var clone = actions.CloneAndAssign(special, clonePath);
            Check(clone != table && special.OverrideTable == clone && clone.groups[0] != table.groups[0], "clone independent groups");
            Undo.PerformUndo();
            Check(special.OverrideTable == table, "clone assignment undo");
            // Reload original saved scene to verify the persisted configuration, not test mutations.
            AssetDatabase.DeleteAsset(clonePath);
            EditorSceneManager.OpenScene("Assets/Scenes/ExploreScene.unity");
            Check(Object.FindObjectsByType<SearchableContainer>(FindObjectsSortMode.None).Count(c => c.OverrideTable != null) == 1, "saved override survives reload");
            Debug.Log("REGION_LOOT_EDIT_CHECKS_PASSED " + assertions);
        }
        private void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) playStarted = EditorApplication.timeSinceStartup;
            if (state == PlayModeStateChange.EnteredEditMode && completed)
            {
                if (unexpectedErrors.Count > 0) { Fail(new Exception(string.Join("\n", unexpectedErrors))); return; }
                Debug.Log("REGION_LOOT_ALL_CHECKS_PASSED " + assertions);
                Application.logMessageReceived -= CaptureError;
                EditorApplication.Exit(0);
            }
        }
        private void WaitForPlay()
        {
            if (completed) return;
            if (EditorApplication.timeSinceStartup - playStarted > 90) { Fail(new Exception("Play Mode timeout")); return; }
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - playStarted < 3) return;
            try
            {
                var boxes = Object.FindObjectsByType<SearchableContainer>(FindObjectsSortMode.None);
                Check(boxes.Length == 4 && boxes.All(c => c.Contents != null), "session initialized all region boxes");
                var session = Object.FindFirstObjectByType<InventorySession>();
                Check(session != null && session.Storage.Count > 0, "player inventory intact");
                var box = boxes[0];
                var contents = box.Contents;
                var before = contents.Items.ToArray();
                box.BeginSearch(); box.Tick(100);
                Check(contents.Items.Count(c => c.Revealed) == 1, "one search tick reveals one item");
                box.PauseSearch(); box.Initialize(); box.BeginSearch();
                Check(ReferenceEquals(contents, box.Contents) && before.SequenceEqual(box.Contents.Items), "reopen no reroll");
                var first = contents.Items.First(c => c.Revealed);
                session.Open(box); session.QuickTransfer(first);
                Check(first.Owner == session.Backpack, "revealed loot transfer");
                session.Close();
                box.gameObject.SetActive(false); box.gameObject.SetActive(true);
                Check(ReferenceEquals(contents, box.Contents), "disable enable preserves contents");
                var region = box.FindNearestRegion();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Inventory/SearchableContainer.prefab");
                var spawned = Object.Instantiate(prefab, region.transform).GetComponent<SearchableContainer>();
                region.RegisterContainer(spawned);
                Check(spawned.Contents != null && region.Containers.Contains(spawned), "dynamic container registered and initialized");
                new LootPreviewService().Simulate(new LootRuleResolver().Resolve(box).Table, box.Columns, box.Rows, 20, 99);
                Check(ReferenceEquals(contents, box.Contents), "preview doesn't mutate runtime contents");
                completed = true;
                EditorApplication.update -= WaitForPlay;
                EditorApplication.isPlaying = false;
            }
            catch (Exception exception) { Fail(exception); }
        }
        private void Fail(Exception exception)
        {
            completed = true;
            Application.logMessageReceived -= CaptureError;
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        private void CaptureError(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                unexpectedErrors.Add(message + "\n" + stack);
        }
    }
}
