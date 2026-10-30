using System.Collections.Generic;
using System.Linq;
using LootTheDeep.Gameplay;
using LootTheDeep.Gameplay.Maps;
using LootTheDeep.Inventory;
using UnityEditor;
using UnityEngine;

namespace LootTheDeep.LevelBuilder
{
    internal static class MapEditorValidation
    {
        internal static List<LayoutIssue> StructuralErrors(MapRoot root)
        {
            var issues = new List<LayoutIssue>();
            if (LevelBuilderScene.All<MapRoot>(root.gameObject.scene).Length != 1)
                issues.Add(new LayoutIssue(root, "制作场景需要且仅保留一个 MapRoot。"));
            if (root.All<TilemapPathfinder2D>().Length != 1)
                issues.Add(new LayoutIssue(root, "Map 需要且仅保留一个导航组件。"));
            if (string.IsNullOrWhiteSpace(root.mapId)) issues.Add(new LayoutIssue(root, "地图编号不能为空。"));
            var required = new Component[] { root.ground, root.navigation, root.terrain, root.obstacles, root.containers,
                root.playerSpawnPoints, root.extractionPoints, root.enemyRegions };
            if (required.Any(c => c == null || !c.transform.IsChildOf(root.transform)))
                issues.Add(new LayoutIssue(root, "地图层级/引用不完整；必须全部位于 MapRoot 内。"));
            if (root.All<MapRoot>().Length != 1) issues.Add(new LayoutIssue(root, "MapRoot 内不能嵌套另一个 MapRoot。"));
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) { issues.Add(new LayoutIssue(root, "Map 内存在丢失脚本。")); continue; }
                if (component is PlayerShooter2D or EnemyCombat2D or InventorySession or Camera or Canvas or CombatPrototypeSession2D)
                    issues.Add(new LayoutIssue(component, "Map 不能包含测试玩家、运行敌人、摄像机或界面：" + component.name));
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (property.name is "m_PrefabInstance" or "m_PrefabAsset" or "m_CorrespondingSourceObject") continue;
                    var value = property.objectReferenceValue;
                    if (value == null || EditorUtility.IsPersistent(value)) continue;
                    Transform target = value is GameObject go ? go.transform : (value as Component)?.transform;
                    if (target == null || !target.IsChildOf(root.transform))
                        issues.Add(new LayoutIssue(component, component.name + "." + property.propertyPath + " 引用了 Map 之外的临时对象。"));
                }
            }
            return issues;
        }

        internal static List<LayoutIssue> Check(MapRoot root, int seed)
        {
            var issues = StructuralErrors(root);
            if (issues.Count > 0) return issues;
            var grid = root.ground.layoutGrid;
            if (grid == null || grid.cellLayout != GridLayout.CellLayout.Rectangle || grid.cellSwizzle != GridLayout.CellSwizzle.XYZ ||
                Quaternion.Angle(root.ground.transform.rotation, Quaternion.identity) > .01f ||
                root.ground.transform.lossyScale.x <= 0 || root.ground.transform.lossyScale.y <= 0)
            { issues.Add(new LayoutIssue(root, "地图需要无旋转、正缩放的 XY 矩形 Grid。")); return issues; }
            var bounds = root.Bounds;
            if (bounds.size == Vector3Int.zero)
            { issues.Add(new LayoutIssue(root.ground, "地图尚未绘制地形，请先铺设地面。")); return issues; }
            if (bounds.size.z != 1 || bounds.zMin != 0)
            { issues.Add(new LayoutIssue(root.ground, "地图地形需要绘制在 Z=0 的单层网格。")); return issues; }
            root.RebuildNavigation();
            var spawns = root.Spawns;
            if (spawns.Length == 0) issues.Add(new LayoutIssue(root, "至少放置一个启用的玩家出生点。"));
            if (root.Exits.Length < root.activeExtractionCount || root.activeExtractionCount < 1)
                issues.Add(new LayoutIssue(root, "撤离候选不足，或本局启用数量不是正数。"));
            foreach (var exit in root.Exits)
                if (!root.navigation.CanTraverse(exit.transform.position, exit.transform.position))
                    issues.Add(new LayoutIssue(exit, "撤离点卡墙或没有地面。"));
            var resolver = new LootRuleResolver();
            foreach (var box in root.All<SearchableContainer>().Where(b => b.isActiveAndEnabled))
            {
                if (box.FindNearestRegion() == null) issues.Add(new LayoutIssue(box, "容器必须属于 Map 内的搜索区域。"));
                foreach (var error in resolver.ValidateContainer(box)) issues.Add(new LayoutIssue(box, box.displayName + "：" + error));
            }
            foreach (var region in root.All<EnemyRegion2D>().Where(r => r.isActiveAndEnabled))
            {
                if (region.enemies.Count == 0) issues.Add(new LayoutIssue(region, "敌人区域没有配置敌人。", false));
                if (region.enemies.Any(e => e.prefab != null && !PrefabUtility.IsPartOfPrefabAsset(e.prefab)))
                    issues.Add(new LayoutIssue(region, "敌人区域必须引用项目中的敌人预制体。"));
                foreach (var entry in region.enemies.Where(e => e.prefab != null))
                {
                    var serialized = new SerializedObject(entry.prefab);
                    if (entry.prefab.GetComponent<CombatHealth2D>() == null ||
                        serialized.FindProperty("attackKind").enumValueIndex == 0 && serialized.FindProperty("projectilePrefab").objectReferenceValue == null)
                        issues.Add(new LayoutIssue(region, "敌人缺少生命或远程弹丸配置。"));
                }
            }
            foreach (var spawn in spawns)
            {
                if (!root.navigation.CanTraverse(spawn.transform.position, spawn.transform.position))
                { issues.Add(new LayoutIssue(spawn, "出生点卡墙或没有地面。")); continue; }
                if (MapSpawnPlanner.ReachableExits(root, spawn.transform.position).Count < root.activeExtractionCount)
                    issues.Add(new LayoutIssue(spawn, "此出生点没有足够的可达撤离点。"));
                foreach (var box in root.All<SearchableContainer>().Where(b => b.isActiveAndEnabled))
                    if (!LevelBuilderScene.TryApproach(root.navigation, spawn.transform.position, box, 1.8f, out _))
                        issues.Add(new LayoutIssue(box, "从 " + spawn.name + " 无法抵达容器搜索位置。"));
                if (!MapSpawnPlanner.PlanEnemies(root, spawn.transform.position, seed, out _, out string error))
                    issues.Add(new LayoutIssue(spawn, error));
            }
            return issues;
        }
    }
}
