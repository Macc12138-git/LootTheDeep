using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LootTheDeep.Inventory.EditorTools
{
    public sealed class RegionLootInstaller
    {
        // Unity MenuItem/executeMethod requires a static entry point; implementation is instance-based.
        [MenuItem("LootTheDeep/Install Region Container Loot")]
        public static void Install() { new RegionLootInstaller().InstallScene(); }

        public void InstallScene()
        {
            const string scenePath = "Assets/Scenes/ExploreScene.unity";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isBatchMode) scene = EditorSceneManager.OpenScene(scenePath);
            if (scene.path != scenePath) { Debug.LogError("请先打开 ExploreScene。"); return; }
            var installed = Object.FindObjectsByType<RegionContainerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(r => r.gameObject.scene == scene);
            if (installed != null)
            { Debug.Log("区域容器已安装，保留现有配置。"); OpenWindow(installed); return; }
            var boxes = Object.FindObjectsByType<SearchableContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene == scene).OrderBy(c => c.name).ToList();
            if (boxes.Count < 3) { Debug.LogError("ExploreScene 需要已有的三个原型容器。"); return; }
            string folder = "Assets/Data/Inventory/Loot";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Data/Inventory", "Loot");
            var scrap = Item("scrap"); var battery = Item("battery"); var bandage = Item("bandage");
            var tank = Item("tank"); var pump = Item("pump"); var chip = Item("chip");
            var standard = Table(folder + "/DefaultLoot.asset", "通用物资", new[] { 75f, 22, 2.8f, .2f },
                new[] { scrap, bandage, battery, tank, pump, chip });
            var mechanical = Table(folder + "/MechanicalLoot.asset", "机械维修物资", new[] { 50f, 40, 9, 1 },
                new[] { scrap, battery, pump, chip });
            var electrical = Table(folder + "/ElectricalLoot.asset", "电力维修物资", new[] { 35f, 45, 15, 5 },
                new[] { scrap, battery, pump, chip });
            var medical = Table(folder + "/MedicalLoot.asset", "医疗补给", new[] { 100f, 0, 0, 0 }, new[] { bandage });
            var special = Table(folder + "/SpecialLoot.asset", "设备核心奖励", new[] { 0f, 0, 90, 10 }, new[] { pump, chip }, true);
            var mechanicalConfig = RegionConfig(folder + "/MechanicalRegion.asset", new[] {
                new ContainerLootBinding { type = ContainerType.MaintenanceBox, table = mechanical },
                new ContainerLootBinding { type = ContainerType.MedicalCabinet, table = medical } });
            var electricalConfig = RegionConfig(folder + "/ElectricalRegion.asset", new[] {
                new ContainerLootBinding { type = ContainerType.MaintenanceBox, table = electrical } });
            var parent = boxes[0].transform.parent;
            var regionA = CreateRegion("机械维修区", mechanicalConfig, parent);
            var regionB = CreateRegion("电力控制区", electricalConfig, parent);
            SetupBox(boxes[0], regionA, ContainerType.MaintenanceBox, standard, null);
            SetupBox(boxes[1], regionA, ContainerType.MedicalCabinet, standard, null);
            SetupBox(boxes[2], regionB, ContainerType.MaintenanceBox, standard, null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Inventory/SearchableContainer.prefab");
            var extra = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(extra, "添加特殊示例容器");
            extra.name = "SpecialStorageContainer";
            extra.transform.position = boxes[2].transform.position + Vector3.right * 2.5f;
            SetupBox(extra.GetComponent<SearchableContainer>(), regionB, ContainerType.StorageCabinet, standard, special);
            // Set the prefab's default so newly placed containers work without per-instance table assignment.
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var serialized = new SerializedObject(root.GetComponent<SearchableContainer>());
                serialized.FindProperty("lootMode").enumValueIndex = (int)ContainerLootMode.RandomTable;
                serialized.FindProperty("defaultTable").objectReferenceValue = standard;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            regionA.RefreshContainers(); regionB.RefreshContainers();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = regionA.gameObject;
            OpenWindow(regionA);
            Debug.Log("REGION_LOOT_INSTALLED: 两个区域、四个容器、三种类型、一个单独配置。");
        }
        private void OpenWindow(RegionContainerManager region)
        {
            if (!Application.isBatchMode) EditorWindow.GetWindow<RegionContainerWindow>().SetRegion(region);
        }
        private LootDefinition Item(string id) => AssetDatabase.LoadAssetAtPath<LootDefinition>("Assets/Data/Inventory/" + id + ".asset");
        private LootTableSO Table(string path, string name, float[] weights, LootDefinition[] items, bool core = false)
        {
            var existing = AssetDatabase.LoadAssetAtPath<LootTableSO>(path);
            if (existing != null) return existing;
            var table = ScriptableObject.CreateInstance<LootTableSO>();
            table.groups.Add(new LootRollGroup { name = name, coreReward = core, minRolls = core ? 1 : 2,
                maxRolls = core ? 1 : 4, qualityWeights = weights,
                entries = items.Select(item => new LootEntry { item = item, weight = 1 }).ToList() });
            if (core)
                table.groups.Add(new LootRollGroup { name = "附加奖励", minRolls = 1, maxRolls = 2,
                    qualityWeights = new[] { 75f, 25, 0, 0 }, entries = new List<LootEntry> {
                        new LootEntry { item = Item("scrap") }, new LootEntry { item = Item("battery") } } });
            AssetDatabase.CreateAsset(table, path);
            return table;
        }
        private RegionLootConfigSO RegionConfig(string path, ContainerLootBinding[] bindings)
        {
            var existing = AssetDatabase.LoadAssetAtPath<RegionLootConfigSO>(path);
            if (existing != null) return existing;
            var config = ScriptableObject.CreateInstance<RegionLootConfigSO>();
            config.bindings.AddRange(bindings);
            AssetDatabase.CreateAsset(config, path);
            return config;
        }
        private RegionContainerManager CreateRegion(string name, RegionLootConfigSO config, Transform parent)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "创建容器区域");
            go.transform.SetParent(parent, false);
            var region = Undo.AddComponent<RegionContainerManager>(go);
            region.regionName = name; region.config = config;
            return region;
        }
        private void SetupBox(SearchableContainer box, RegionContainerManager region, ContainerType type,
            LootTableSO defaultTable, LootTableSO overrideTable)
        {
            Undo.SetTransformParent(box.transform, region.transform, "设置容器区域");
            var serialized = new SerializedObject(box);
            serialized.FindProperty("type").enumValueIndex = (int)type;
            serialized.FindProperty("lootMode").enumValueIndex = (int)ContainerLootMode.RandomTable;
            serialized.FindProperty("defaultTable").objectReferenceValue = defaultTable;
            serialized.FindProperty("overrideTable").objectReferenceValue = overrideTable;
            serialized.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(box.transform);
        }
    }
}
