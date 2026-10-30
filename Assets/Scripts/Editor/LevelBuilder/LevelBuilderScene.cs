using System;
using System.Collections.Generic;
using System.Linq;
using LootTheDeep.Gameplay;
using LootTheDeep.Gameplay.Maps;
using LootTheDeep.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace LootTheDeep.LevelBuilder
{
    internal sealed class LayoutIssue
    {
        public Object target;
        public string message;
        public bool error;
        public LayoutIssue(Object target, string message, bool error = true)
        { this.target = target; this.message = message; this.error = error; }
    }

    internal static class LevelBuilderScene
    {
        public const string ExplorePath = "Assets/Scenes/ExploreScene.unity";
        public static bool CanEdit => !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling && PrefabStageUtility.GetCurrentPrefabStage() == null &&
            SceneManager.GetActiveScene().path == MapEditorWorkspace.EditorPath;

        public static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

        public static void Changed(Object value)
        {
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
            var component = value as Component;
            if (component != null) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }

        public static int Begin(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        public static BoundsInt Rectangle(Vector3Int a, Vector3Int b) => new(
            new Vector3Int(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), a.z),
            new Vector3Int(Mathf.Abs(a.x - b.x) + 1, Mathf.Abs(a.y - b.y) + 1, 1));

        public static void Paint(Tilemap map, BoundsInt area, TileBase tile, bool recordUndo = true)
        {
            if (recordUndo) Undo.RegisterCompleteObjectUndo(map, tile == null ? "擦除地形" : "绘制地形");
            foreach (Vector3Int p in area.allPositionsWithin)
            {
                map.SetTile(p, tile);
                if (tile == null) continue;
                map.SetTileFlags(p, TileFlags.None);
                map.SetTransformMatrix(p, Matrix4x4.identity);
                map.SetColor(p, Color.white);
                if (tile is Tile typed) { map.SetColliderType(p, typed.colliderType); map.SetTileFlags(p, typed.flags); }
                else map.SetTileFlags(p, TileFlags.LockAll);
            }
            Changed(map);
        }

        public static void PlaceModule(Tilemap map, LevelTileModule module, Vector3Int anchor, int turns, bool overwrite)
        {
            Undo.RegisterCompleteObjectUndo(map, "放置地图模块");
            Matrix4x4 rotation = Matrix4x4.Rotate(Quaternion.Euler(0, 0, turns * 90));
            foreach (var cell in module.cells)
            {
                Vector3Int p = anchor + module.RotateCell(cell.position, turns);
                if (!overwrite && (map.HasTile(p) || cell.tile == null)) continue;
                map.SetTile(p, cell.tile);
                if (cell.tile == null) continue;
                map.SetTileFlags(p, TileFlags.None);
                map.SetColor(p, cell.color);
                map.SetTransformMatrix(p, rotation * cell.transform);
                map.SetColliderType(p, cell.collider);
                map.SetTileFlags(p, cell.flags);
            }
            Changed(map);
        }

        public static Vector3 Snap(Tilemap map, Vector3 position)
        {
            Vector3 result = map.GetCellCenterWorld(map.WorldToCell(position));
            result.z = position.z;
            return result;
        }

        public static GameObject[] Duplicate(Transform[] selected, Tilemap map)
        {
            Selection.objects = selected.Select(t => (Object)t.gameObject).ToArray();
            Unsupported.DuplicateGameObjectsUsingPasteboard();
            var copies = Selection.gameObjects;
            Vector3 offset = map.CellToWorld(Vector3Int.right) - map.CellToWorld(Vector3Int.zero);
            foreach (var copy in copies)
            {
                Undo.RecordObject(copy.transform, "偏移复制对象");
                copy.transform.position += offset;
                Changed(copy.transform);
            }
            return copies;
        }

        private static Transform Root(Scene scene, string name)
        {
            var mapRoot = All<MapRoot>(scene).FirstOrDefault();
            if (mapRoot != null) return name == "CombatNavigation" ? mapRoot.transform : mapRoot.obstacles;
            var root = scene.GetRootGameObjects().FirstOrDefault(r => r.name == name);
            if (root == null)
            {
                root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "建立布局层级");
            }
            return root.transform;
        }

        public static RegionContainerManager CreateRegion(Scene scene)
        {
            var root = new GameObject("搜索区域");
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "新建搜索区域");
            var mapRoot = All<MapRoot>(scene).FirstOrDefault();
            if (mapRoot != null) Undo.SetTransformParent(root.transform, mapRoot.containers, "搜索区域归入地图");
            return Undo.AddComponent<RegionContainerManager>(root);
        }

        public static GameObject Place(Scene scene, LevelPrefabEntry entry, RegionContainerManager region,
            Vector3 position, int turns)
        {
            if (entry.prefab == null || !PrefabUtility.IsPartOfPrefabAsset(entry.prefab))
                throw new InvalidOperationException("请使用项目中的预制体资源。");
            if (entry.kind == LevelAssetKind.Container && (region == null || region.gameObject.scene != scene))
                throw new InvalidOperationException("请先指定当前场景中的搜索区域。");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "放置" + entry.label);
            Transform parent = entry.kind == LevelAssetKind.Container ? region.transform :
                Root(scene, entry.kind == LevelAssetKind.Enemy ? "CombatEncounter" : "LevelObstacles");
            Undo.SetTransformParent(instance.transform, parent, "归入布局层级");
            Undo.RecordObject(instance.transform, "设置放置位置与缩放");
            instance.transform.position = position;
            instance.transform.localScale = entry.scale;
            if (entry.allowRotation) instance.transform.rotation = Quaternion.Euler(0, 0, turns * 90);
            Changed(instance.transform);
            if (entry.kind == LevelAssetKind.Container)
            {
                var renderer = instance.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    Undo.RecordObject(renderer, "设置世界容器显示层");
                    renderer.sortingLayerName = "World";
                    renderer.sortingOrder = 10;
                    Changed(renderer);
                }
            }
            return instance;
        }

        public static Collider2D[] Blockers(Scene scene) => All<Collider2D>(scene).Where(c =>
            c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.shapeCount > 0 &&
            c.GetComponentInParent<CombatHealth2D>() == null && c.GetComponentInParent<ShootingProjectile2D>() == null &&
            (c.attachedRigidbody == null || c.attachedRigidbody.bodyType == RigidbodyType2D.Static) &&
            !(c is TilemapCollider2D && c.GetComponent<CompositeCollider2D>() != null)).ToArray();

        public static TilemapPathfinder2D SyncNavigation(Tilemap map, TilemapPathfinder2D navigation, bool recordUndo)
        {
            var scene = map.gameObject.scene;
            foreach (var collider in All<TilemapCollider2D>(scene))
                if (collider.hasTilemapChanges) collider.ProcessTilemapChanges();
            Physics2D.SyncTransforms();
            foreach (var collider in All<CompositeCollider2D>(scene))
                if (collider.generationType == CompositeCollider2D.GenerationType.Manual) collider.GenerateGeometry();
            Physics2D.SyncTransforms();
            if (navigation == null)
                navigation = Undo.AddComponent<TilemapPathfinder2D>(Root(scene, "CombatNavigation").gameObject);
            var blockers = Blockers(scene);
            var serialized = new SerializedObject(navigation);
            var previous = serialized.FindProperty("obstacles");
            bool different = serialized.FindProperty("ground").objectReferenceValue != map || previous.arraySize != blockers.Length;
            if (!different)
            {
                var old = new HashSet<Object>();
                for (int i = 0; i < previous.arraySize; i++) old.Add(previous.GetArrayElementAtIndex(i).objectReferenceValue);
                different = blockers.Any(c => !old.Contains(c));
            }
            if (different && recordUndo) Undo.RecordObject(navigation, "更新导航障碍引用");
            navigation.Configure(map, blockers);
            if (different) Changed(navigation);
            return navigation;
        }

        // Match the runtime search distance and static line-of-sight rules. The box center may be blocked.
        public static bool TryApproach(TilemapPathfinder2D navigation, Vector2 from, SearchableContainer box,
            float distance, out Vector2 approach)
        {
            Vector2 center = box.transform.position;
            var candidates = new List<Vector2> { from, center };
            const float spacing = .4f;
            int steps = Mathf.CeilToInt(distance / spacing);
            for (int y = -steps; y <= steps; y++)
                for (int x = -steps; x <= steps; x++) candidates.Add(center + new Vector2(x, y) * spacing);
            var route = new List<Vector2>();
            foreach (Vector2 p in candidates.OrderBy(p => (p - from).sqrMagnitude))
            {
                if (Vector2.Distance(p, center) > distance || !navigation.CanTraverse(p, p)) continue;
                bool blocked = Physics2D.LinecastAll(p, center).Any(hit => !hit.collider.isTrigger &&
                    !hit.collider.transform.IsChildOf(box.transform) &&
                    hit.collider.GetComponentInParent<CombatHealth2D>() == null &&
                    (hit.collider.attachedRigidbody == null || hit.collider.attachedRigidbody.bodyType == RigidbodyType2D.Static));
                if (!blocked && navigation.TryFindPath(from, p, route)) { approach = p; return true; }
            }
            approach = center;
            return false;
        }

        public static List<LayoutIssue> Check(Tilemap map, TilemapPathfinder2D navigation, PlayerShooter2D player)
        {
            var issues = new List<LayoutIssue>();
            if (map == null) { issues.Add(new LayoutIssue(null, "未指定目标 Tilemap。")); return issues; }
            Scene scene = map.gameObject.scene;
            if (map.layoutGrid == null || map.layoutGrid.cellLayout != GridLayout.CellLayout.Rectangle ||
                map.layoutGrid.cellSwizzle != GridLayout.CellSwizzle.XYZ ||
                Quaternion.Angle(map.transform.rotation, Quaternion.identity) > .01f ||
                map.transform.lossyScale.x <= 0 || map.transform.lossyScale.y <= 0)
                issues.Add(new LayoutIssue(map, "当前导航支持无旋转、正缩放的 XY 矩形网格（允许平移）。"));
            if (navigation == null || navigation.gameObject.scene != scene)
                issues.Add(new LayoutIssue(map, "缺少当前场景的导航组件，请先同步导航。"));
            if (All<TilemapPathfinder2D>(scene).Count(n => n.isActiveAndEnabled) != 1)
                issues.Add(new LayoutIssue(map, "需要且仅保留一个启用的导航组件。"));
            if (player == null || player.gameObject.scene != scene || !player.isActiveAndEnabled)
                issues.Add(new LayoutIssue(map, "需要启用的玩家出生对象。"));
            if (All<PlayerShooter2D>(scene).Count(p => p.isActiveAndEnabled) != 1)
                issues.Add(new LayoutIssue(map, "需要且仅保留一个启用的玩家。"));
            if (navigation == null || player == null || player.gameObject.scene != scene) return issues;
            Vector2 spawn = player.transform.position;
            if (!navigation.CanTraverse(spawn, spawn)) issues.Add(new LayoutIssue(player, "玩家出生点缺地面或与静态障碍重叠。"));
            foreach (var enemy in All<EnemyCombat2D>(scene).Where(e => e.isActiveAndEnabled))
            {
                if (!navigation.CanTraverse(enemy.transform.position, enemy.transform.position))
                    issues.Add(new LayoutIssue(enemy, "敌人出生点卡墙或缺少地面：" + enemy.name));
                var serialized = new SerializedObject(enemy);
                if (enemy.GetComponent<CombatHealth2D>() == null ||
                    serialized.FindProperty("attackKind").enumValueIndex == 0 && serialized.FindProperty("projectilePrefab").objectReferenceValue == null)
                    issues.Add(new LayoutIssue(enemy, "敌人缺少生命或远程弹丸配置：" + enemy.name));
            }
            var boxes = All<SearchableContainer>(scene).Where(c => c.isActiveAndEnabled).ToArray();
            var inventory = All<InventorySession>(scene).FirstOrDefault(i => i.isActiveAndEnabled);
            if (boxes.Length > 0 && (inventory == null || inventory.View == null || inventory.player != player.GetComponent<UnderwaterMover2D>()))
                issues.Add(new LayoutIssue(player, "容器搜索需要已绑定当前玩家的 InventorySession 与界面。"));
            float range = inventory != null ? inventory.interactDistance : 1.8f;
            var resolver = new LootRuleResolver();
            foreach (var box in boxes)
            {
                if (box.FindNearestRegion() == null) issues.Add(new LayoutIssue(box, "容器未归入搜索区域：" + box.name));
                foreach (string error in resolver.ValidateContainer(box)) issues.Add(new LayoutIssue(box, box.displayName + "：" + error));
                if (!TryApproach(navigation, spawn, box, range, out _))
                    issues.Add(new LayoutIssue(box, "从出生点无法抵达搜索距离内的无遮挡位置：" + box.displayName));
            }
            foreach (var region in All<RegionContainerManager>(scene))
                if (region.config != null)
                    foreach (string error in region.config.Validate()) issues.Add(new LayoutIssue(region, region.regionName + "：" + error));
            if (boxes.Length == 0) issues.Add(new LayoutIssue(map, "当前布局没有可搜索容器。", false));
            return issues;
        }
    }
}
