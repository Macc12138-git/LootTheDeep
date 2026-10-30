using System;
using System.Linq;
using LootTheDeep.Gameplay;
using LootTheDeep.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace LootTheDeep.LevelBuilder
{
    public static class LevelBuilderValidation
    {
        private static int checks;

        [MenuItem("LootTheDeep/开发者模式/验证关卡搭建（编辑测试） %#F2")]
        public static void Run()
        {
            if (!LevelBuilderScene.CanEdit) throw new InvalidOperationException("请在 MapEditorScene 编辑模式运行。");
            Scene original = SceneManager.GetActiveScene();
            Debug.Log($"LEVEL_BUILDER_VALIDATION_START: dataPath={Application.dataPath}; product={Application.productName}; scene={original.path}");
            var originalSelection = Selection.objects;
            var library = LevelBuilderLibrary.LoadOrCreate();
            Check(library.tiles.Count > 0 && library.modules.Count > 0 && library.prefabs.Count >= 3, "素材库可直接使用");
            Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            string scratchPath = AssetDatabase.GenerateUniqueAssetPath(LevelBuilderLibrary.Folder + "/__布局验证.unity");
            string modulePath = AssetDatabase.GenerateUniqueAssetPath(LevelBuilderLibrary.Folder + "/__模块验证.asset");
            LevelTileModule captured = null;
            try
            {
                checks = 1;
                var grid = new GameObject("ValidationGrid", typeof(Grid));
                SceneManager.MoveGameObjectToScene(grid, scratch);
                grid.transform.position = new Vector3(12000, 12000, 0);
                var mapObject = new GameObject("ValidationMap", typeof(Tilemap), typeof(TilemapRenderer));
                mapObject.transform.SetParent(grid.transform, false);
                var map = mapObject.GetComponent<Tilemap>();
                var body = mapObject.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Static;
                var tileCollider = mapObject.AddComponent<TilemapCollider2D>();
                tileCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
                mapObject.AddComponent<CompositeCollider2D>();
                var floor = library.tiles.OfType<Tile>().First(t => t.colliderType == Tile.ColliderType.None);
                var wallTile = library.tiles.OfType<Tile>().First(t => t.colliderType != Tile.ColliderType.None);
                int group = LevelBuilderScene.Begin("验证矩形与撤销");
                LevelBuilderScene.Paint(map, new BoundsInt(0, 0, 0, 12, 12, 1), floor);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Check(map.GetTile(new Vector3Int(11, 11)) == floor && map.GetUsedTilesCount() == 1, "矩形铺设覆盖整个范围");
                Undo.PerformUndo(); Check(!map.HasTile(new Vector3Int(0, 0)), "一次撤销整次矩形铺设");
                Undo.PerformRedo(); Check(map.HasTile(new Vector3Int(11, 11)), "重做恢复地形");
                group = LevelBuilderScene.Begin("验证连续笔刷");
                Undo.RegisterCompleteObjectUndo(map, "验证连续笔刷");
                for (int x = 14; x < 17; x++) LevelBuilderScene.Paint(map, new BoundsInt(x, 0, 0, 1, 1, 1), floor, false);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Undo.PerformUndo();
                Check(Enumerable.Range(14, 3).All(x => !map.HasTile(new Vector3Int(x, 0))), "连续笔刷整笔撤销");
                group = LevelBuilderScene.Begin("验证擦除");
                LevelBuilderScene.Paint(map, new BoundsInt(1, 1, 0, 2, 2, 1), null);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Check(!map.HasTile(new Vector3Int(1, 1)) && map.HasTile(Vector3Int.zero), "矩形擦除不越界");
                Undo.PerformUndo(); Check(map.HasTile(new Vector3Int(1, 1)), "擦除可撤销");

                map.SetTile(new Vector3Int(0, 0), wallTile);
                map.SetTile(new Vector3Int(2, 1), null);
                map.SetTileFlags(Vector3Int.zero, TileFlags.None);
                map.SetColor(Vector3Int.zero, Color.yellow);
                map.SetTransformMatrix(Vector3Int.zero, Matrix4x4.Scale(new Vector3(-1, 1, 1)));
                captured = LevelTileModule.Capture(map, new BoundsInt(0, 0, 0, 3, 2, 1));
                AssetDatabase.CreateAsset(captured, modulePath); AssetDatabase.SaveAssets();
                Check(captured.cells.Count == 6 && captured.cells.Count(c => c.tile == null) == 1, "模块捕获包含空格与颜色、矩阵");
                Check(captured.RotatedSize(1) == new Vector2Int(2, 3) && captured.RotateCell(new Vector3Int(2, 1), 1) == new Vector3Int(0, 2), "非正方形模块旋转范围正确");
                group = LevelBuilderScene.Begin("验证旋转模块");
                LevelBuilderScene.PlaceModule(map, captured, new Vector3Int(5, 5), 1, true);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                var rotatedCell = new Vector3Int(6, 5);
                Check(map.GetTile(rotatedCell) == wallTile && map.GetColor(rotatedCell) == Color.yellow &&
                    map.GetTransformMatrix(rotatedCell) == Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90)) * captured.cells[0].transform,
                    "旋转模块写入地块与变换");
                Check(!map.HasTile(new Vector3Int(5, 7)), "覆盖模式写入模块空格");
                Undo.PerformUndo(); Check(map.GetTile(rotatedCell) == floor && map.HasTile(new Vector3Int(5, 7)), "一次撤销整个模块");
                group = LevelBuilderScene.Begin("验证填空模块");
                LevelBuilderScene.PlaceModule(map, captured, new Vector3Int(5, 5), 1, false);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Check(map.GetTile(rotatedCell) == floor && map.HasTile(new Vector3Int(5, 7)), "只填空格模式保护原地形");
                LevelBuilderScene.Paint(map, new BoundsInt(0, 0, 0, 12, 12, 1), floor);

                Vector3 spawn = map.GetCellCenterWorld(new Vector3Int(2, 2));
                var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gameplay/Player.prefab");
                var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scratch);
                playerObject.transform.position = spawn;
                var player = playerObject.GetComponent<PlayerShooter2D>();
                var region = LevelBuilderScene.CreateRegion(scratch);
                region.regionName = "验证区域";
                var entry = library.prefabs.First(p => p.kind == LevelAssetKind.Container);
                group = LevelBuilderScene.Begin("验证容器放置");
                var boxObject = LevelBuilderScene.Place(scratch, entry, region, map.GetCellCenterWorld(new Vector3Int(6, 6)), 0);
                var box = boxObject.GetComponent<SearchableContainer>();
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Check(PrefabUtility.IsPartOfPrefabInstance(boxObject) && box.FindNearestRegion() == region &&
                    box.GetComponent<SpriteRenderer>().bounds.size.x >= 1f && box.Contents == null,
                    "容器保持预制体关联、区域归属、显示尺寸且不抽取物资");
                Undo.PerformUndo(); Check(boxObject == null, "放置对象可撤销");
                Undo.PerformRedo(); box = LevelBuilderScene.All<SearchableContainer>(scratch).Single();
                Check(box != null && box.FindNearestRegion() == region &&
                    box.transform.position == map.GetCellCenterWorld(new Vector3Int(6, 6)) &&
                    box.GetComponent<SpriteRenderer>().bounds.size.x >= 1f, "重做恢复对象、位置、缩放与区域归属");
                var serializedBox = new SerializedObject(box);
                serializedBox.FindProperty("searchSeconds").floatValue = 3f;
                serializedBox.ApplyModifiedProperties();
                group = LevelBuilderScene.Begin("验证复制");
                var duplicate = LevelBuilderScene.Duplicate(new[] { box.transform }, map).Single();
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Check(PrefabUtility.IsPartOfPrefabInstance(duplicate) && duplicate.GetComponent<SearchableContainer>().searchSeconds == 3f &&
                    duplicate.transform.parent == region.transform, "复制保留预制体连接、实例覆盖与区域");
                Undo.PerformUndo(); Check(duplicate == null && box != null, "复制可整次撤销");

                var enemyObject = LevelBuilderScene.Place(scratch, library.prefabs.First(p => p.kind == LevelAssetKind.Enemy), null,
                    map.GetCellCenterWorld(new Vector3Int(3, 3)), 0);
                Check(enemyObject.transform.parent.name == "CombatEncounter" && PrefabUtility.IsPartOfPrefabInstance(enemyObject), "敌人自动归入战斗层级");
                Vector3 snapped = LevelBuilderScene.Snap(map, grid.transform.position + new Vector3(2.1f, 3.9f, 7f));
                Check(snapped == grid.transform.position + new Vector3(2.5f, 3.5f, 7f), "吸附使用地图坐标变换并保留 Z");

                var navigation = LevelBuilderScene.SyncNavigation(map, null, true);
                Check(navigation.CanTraverse(spawn, spawn), "地形碰撞与导航同步");
                var blocker = new GameObject("AddedStaticObstacle", typeof(BoxCollider2D));
                SceneManager.MoveGameObjectToScene(blocker, scratch);
                blocker.transform.position = box.transform.position + Vector3.right * .75f;
                blocker.GetComponent<BoxCollider2D>().size = Vector2.one;
                navigation = LevelBuilderScene.SyncNavigation(map, navigation, true);
                Check(!navigation.CanTraverse(box.transform.position, box.transform.position), "新增静态障碍进入导航引用");
                bool accessible = LevelBuilderScene.TryApproach(navigation, spawn, box, 1.8f, out Vector2 approach);
                if (!accessible)
                {
                    Vector2 candidate = (Vector2)box.transform.position + Vector2.left * .8f;
                    Debug.Log("LEVEL_APPROACH_DIAGNOSTIC: spawn=" + spawn + "; box=" + box.transform.position + "; candidate=" + candidate +
                        "; clear=" + navigation.CanTraverse(candidate, candidate) + "; route=" + navigation.TryFindPath(spawn, candidate, new System.Collections.Generic.List<Vector2>()) +
                        "; hits=" + string.Join(",", Physics2D.LinecastAll(candidate, box.transform.position).Select(h => h.collider.name)) +
                        "; blockers=" + string.Join(",", LevelBuilderScene.Blockers(scratch).Select(c => c.name + ":" + c.bounds)));
                }
                Check(accessible && Vector2.Distance(approach, box.transform.position) <= 1.8f, "箱子中心不可走时仍能找到无遮挡搜索位置");
                blocker.transform.position = box.transform.position;
                blocker.GetComponent<BoxCollider2D>().size = Vector2.one * 5f;
                LevelBuilderScene.SyncNavigation(map, navigation, true);
                Check(!LevelBuilderScene.TryApproach(navigation, spawn, box, 1.8f, out _), "完全封死的箱子不能通过搜索可达检查");
                Object.DestroyImmediate(blocker);
                LevelBuilderScene.SyncNavigation(map, navigation, true);
                Check(navigation.CanTraverse(box.transform.position, box.transform.position), "移除障碍后导航刷新");

                // Save/reopen a disposable additive scene. The real exploration layout is never overwritten.
                LevelBuilderScene.Changed(box);
                Check(EditorSceneManager.SaveScene(scratch, scratchPath), "布局保存成功");
                EditorSceneManager.CloseScene(scratch, true);
                scratch = EditorSceneManager.OpenScene(scratchPath, OpenSceneMode.Additive);
                var loadedBox = LevelBuilderScene.All<SearchableContainer>(scratch).Single();
                var loadedNav = LevelBuilderScene.All<TilemapPathfinder2D>(scratch).Single();
                var loadedMap = LevelBuilderScene.All<Tilemap>(scratch).Single();
                Check(loadedBox.searchSeconds == 3f && loadedBox.FindNearestRegion().regionName == "验证区域" &&
                    PrefabUtility.IsPartOfPrefabInstance(loadedBox), "重开后保留容器覆盖、区域和预制体关联");
                Check(new SerializedObject(loadedNav).FindProperty("ground").objectReferenceValue == loadedMap && loadedMap.HasTile(new Vector3Int(11, 11)), "重开后保留地图和导航绑定");
                Check(AssetDatabase.LoadAssetAtPath<LevelTileModule>(modulePath).cells.Count == 6, "模块资源持久化");
                Check(new SerializedObject(entry.prefab.GetComponent<SearchableContainer>()).FindProperty("searchSeconds").floatValue != 3f, "实例修改没有污染源预制体");

                const string mapPrefabPath = "Assets/Map/Prefabs/Map/MapBase.prefab";
                string sourceHash = Hash128.Compute(System.IO.File.ReadAllText(mapPrefabPath)).ToString();
                var prefabMapRoot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(mapPrefabPath), scratch);
                prefabMapRoot.name = "PrefabTileOverrideTest";
                var prefabMap = prefabMapRoot.GetComponentInChildren<Tilemap>();
                Vector3Int extra = new(prefabMap.cellBounds.xMax + 2, prefabMap.cellBounds.yMax + 2, 0);
                LevelBuilderScene.Paint(prefabMap, new BoundsInt(extra, Vector3Int.one), floor);
                EditorSceneManager.SaveScene(scratch, scratchPath);
                EditorSceneManager.CloseScene(scratch, true);
                scratch = EditorSceneManager.OpenScene(scratchPath, OpenSceneMode.Additive);
                var reloadedMap = scratch.GetRootGameObjects().Single(r => r.name == "PrefabTileOverrideTest").GetComponentInChildren<Tilemap>();
                Check(reloadedMap.GetTile(extra) == floor && PrefabUtility.IsPartOfPrefabInstance(reloadedMap), "地图预制体地形覆盖保存重开后保留");
                Check(Hash128.Compute(System.IO.File.ReadAllText(mapPrefabPath)).ToString() == sourceHash, "地形编辑未修改源地图预制体");
                Debug.Log($"LEVEL_BUILDER_EDIT_CHECKS_PASSED: {checks} checks; tiles, undo/redo, rotated sparse modules, prefabs, overrides, regions, navigation, approach reachability, save/reopen.");
            }
            finally
            {
                if (scratch.IsValid() && scratch.isLoaded) EditorSceneManager.CloseScene(scratch, true);
                SceneManager.SetActiveScene(original);
                Selection.objects = originalSelection;
                if (AssetDatabase.LoadAssetAtPath<Object>(scratchPath) != null) AssetDatabase.DeleteAsset(scratchPath);
                if (AssetDatabase.LoadAssetAtPath<Object>(modulePath) != null) AssetDatabase.DeleteAsset(modulePath);
            }
        }

        [MenuItem("LootTheDeep/开发者模式/检查当前布局 %#F10")]
        public static void CheckCurrent()
        {
            if (!LevelBuilderScene.CanEdit) throw new InvalidOperationException("请在 MapEditorScene 编辑模式运行。");
            var scene = SceneManager.GetActiveScene();
            var map = LevelBuilderScene.All<Tilemap>(scene).First();
            var navigation = LevelBuilderScene.SyncNavigation(map, LevelBuilderScene.All<TilemapPathfinder2D>(scene).FirstOrDefault(), true);
            var issues = MapEditorValidation.Check(MapEditorWorkspace.Current, 12345);
            foreach (var issue in issues)
                if (issue.error) Debug.LogError("LEVEL_LAYOUT: " + issue.message, issue.target);
                else Debug.LogWarning("LEVEL_LAYOUT: " + issue.message, issue.target);
            Debug.Log($"LEVEL_LAYOUT_CHECK_COMPLETE: {issues.Count(i => i.error)} errors; {issues.Count(i => !i.error)} warnings; " +
                $"enemies={LevelBuilderScene.All<EnemyCombat2D>(scene).Length}; containers={LevelBuilderScene.All<SearchableContainer>(scene).Length}");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("LEVEL_BUILDER_CHECK_FAILED: " + message);
            checks++;
        }
    }
}
