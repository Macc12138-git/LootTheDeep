using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LootTheDeep.Gameplay;
using LootTheDeep.Gameplay.Maps;
using LootTheDeep.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace LootTheDeep.LevelBuilder
{
    [InitializeOnLoad]
    public static class MapWorkflowValidation
    {
        private const string Key = "LootTheDeep.MapWorkflowValidation";
        private static int checks, step;
        private static double next, deadline;
        private static float nextGameTime;
        private static int nextFrame;
        private static InventorySession inventory;
        private static SearchableContainer box;
        private static CombatPrototypeSession2D session;
        private static MapPlaytestController loader;

        static MapWorkflowValidation() => EditorApplication.playModeStateChanged += StateChanged;

        [MenuItem("LootTheDeep/关卡编辑器/验证 Map 存取与检查")]
        public static void EditTests()
        {
            if (!LevelBuilderScene.CanEdit) throw new InvalidOperationException("请在 MapEditorScene 编辑模式验证。");
            checks = 0;
            var original = SceneManager.GetActiveScene();
            string originalFile = Hash(LevelBuilderScene.ExplorePath);
            string sharedFile = Hash("Assets/Map/Prefabs/Map/MapBase.prefab");
            string mapPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Maps/__MapValidation.prefab");
            string copyPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Maps/__MapValidationCopy.prefab");
            string scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/LevelBuilder/__MapValidation.unity");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root = MapEditorWorkspace.CreateMap(scene, "Validation");
                root.transform.position = new Vector3(2000, 2000);
                var floor = LevelBuilderLibrary.LoadOrCreate().tiles.OfType<Tile>().First(t => t.colliderType == Tile.ColliderType.None);
                LevelBuilderScene.Paint(root.ground, new BoundsInt(-16, -12, 0, 32, 24, 1), floor);
                var spawn = MapEditorWorkspace.Marker(root, typeof(PlayerSpawnPoint2D), root.transform.position + new Vector3(-10.5f, .5f));
                MapEditorWorkspace.Marker(root, typeof(ExtractionPoint2D), root.transform.position + new Vector3(10.5f, .5f));
                var area = MapEditorWorkspace.Marker(root, typeof(EnemyRegion2D), root.transform.position + new Vector3(6, 6)).GetComponent<EnemyRegion2D>();
                area.enemies.Add(new EnemyRegionEntry { prefab = AssetDatabase.LoadAssetAtPath<EnemyCombat2D>("Assets/Prefabs/Gameplay/RangedGuard.prefab"), count = 3 });
                var entry = LevelBuilderLibrary.LoadOrCreate().prefabs.First(e => e.kind == LevelAssetKind.Container);
                LevelBuilderScene.Place(scene, entry, root.GetComponentInChildren<RegionContainerManager>(), root.transform.position + Vector3.up * 3.5f, 0);
                root.RebuildNavigation();
                var initialIssues = MapEditorValidation.Check(root, 12345);
                Check(!initialIssues.Any(i => i.error), "完整 Map 检查通过：" + string.Join("; ", initialIssues.Select(i => i.message)));
                Check(root.All<PlayerShooter2D>().Length == 0 && root.All<EnemyCombat2D>().Length == 0, "地图没有测试玩家或活体敌人");
                Check(root.All<SearchableContainer>().Single().Contents == null, "编辑期不初始化物资");
                MapSpawnPlanner.PlanEnemies(root, spawn.transform.position, 12345, out var a, out _);
                MapSpawnPlanner.PlanEnemies(root, spawn.transform.position, 12345, out var b, out _);
                Check(a.Count == 3 && a.Select(p => p.position).SequenceEqual(b.Select(p => p.position)), "相同种子重复生成一致");
                Check(a.All(p => root.navigation.CanTraverse(p.position, p.position)) && a.All(p => a.All(q => p.position == q.position || Vector2.Distance(p.position, q.position) >= 1.3f)), "敌人落点可通行且不重叠");
                int group = LevelBuilderScene.Begin("标记撤销测试");
                var extra = MapEditorWorkspace.Marker(root, typeof(PlayerSpawnPoint2D), root.transform.position);
                Undo.RegisterCreatedObjectUndo(extra, "放置出生标记"); Undo.CollapseUndoOperations(group);
                Undo.PerformUndo(); Check(root.Spawns.Length == 1, "出生标记创建可撤销");
                Undo.PerformRedo(); Check(root.Spawns.Length == 2, "出生标记创建可重做");
                Object.DestroyImmediate(root.Spawns.Last().gameObject);
                area.enemies[0].count = 1000;
                Check(MapEditorValidation.Check(root, 12345).Any(i => i.error && i.message.Contains("空间不足")), "阻断放不下的敌人数量");
                area.enemies[0].count = 3;
                Vector3 spawnPosition = spawn.transform.position;
                spawn.transform.position = root.transform.position + new Vector3(100, 100);
                Check(MapEditorValidation.Check(root, 12345).Any(i => i.error && i.message.Contains("出生点")), "阻断无地面的出生点");
                spawn.transform.position = spawnPosition;
                root.activeExtractionCount = 2;
                Check(MapEditorValidation.Check(root, 12345).Any(i => i.error && i.message.Contains("撤离")), "阻断有效出口数量不足");
                root.activeExtractionCount = 1;
                for (int y = -12; y < 12; y++) root.ground.SetTile(new Vector3Int(0, y), LevelBuilderLibrary.LoadOrCreate().tiles.OfType<Tile>().First(t => t.colliderType != Tile.ColliderType.None));
                Check(MapEditorValidation.Check(root, 12345).Any(i => i.error && i.message.Contains("没有足够的可达撤离点")), "阻断被墙完全隔开的出口");
                for (int y = -12; y < 12; y++) root.ground.SetTile(new Vector3Int(0, y), floor);
                var external = new GameObject("外部引用测试").AddComponent<BoxCollider2D>();
                SceneManager.MoveGameObjectToScene(external.gameObject, scene);
                root.navigation.Configure(root.ground, new Collider2D[] { external });
                Check(MapEditorValidation.StructuralErrors(root).Any(i => i.message.Contains("之外")), "阻断场景外部引用导出");
                Object.DestroyImmediate(external.gameObject); root.RebuildNavigation();
                EditorSceneManager.SaveScene(scene, scenePath);
                Check(MapEditorWorkspace.SaveMap(false, mapPath) == mapPath, "首次保存创建 Map 预制体");
                string guid = AssetDatabase.AssetPathToGUID(mapPath);
                MapEditorWorkspace.Marker(root, typeof(PlayerSpawnPoint2D), root.transform.position + new Vector3(-9.5f, -5.5f));
                LevelBuilderScene.Paint(root.ground, new BoundsInt(-16, -12, 0, 1, 1, 1), null);
                var updatedBox = root.All<SearchableContainer>().Single(); updatedBox.displayName = "修改后的容器"; LevelBuilderScene.Changed(updatedBox);
                area.enemies[0].count = 4; LevelBuilderScene.Changed(area);
                root.displayName = "更新地图"; LevelBuilderScene.Changed(root);
                Check(MapEditorWorkspace.SaveMap(false) == mapPath && AssetDatabase.AssetPathToGUID(mapPath) == guid, "重复保存更新原资源并保持 GUID");
                var saved = AssetDatabase.LoadAssetAtPath<MapRoot>(mapPath);
                Check(saved.displayName == "更新地图" && saved.All<PlayerSpawnPoint2D>().Length == 2 && saved.All<ExtractionPoint2D>().Length == 1 && saved.All<EnemyRegion2D>().Single().enemies[0].count == 4, "打开已保存地图后继续增改标记和区域");
                Check(!saved.ground.HasTile(new Vector3Int(-16, -12)) && saved.All<SearchableContainer>().Single().displayName == "修改后的容器", "已保存地图的地形和嵌套容器覆盖持久化");
                Check(MapEditorWorkspace.SaveMap(true, copyPath) == copyPath && AssetDatabase.AssetPathToGUID(copyPath) != guid, "另存为独立预制体而非变体");
                root.displayName = "副本更新"; LevelBuilderScene.Changed(root); MapEditorWorkspace.SaveMap(false);
                Check(AssetDatabase.LoadAssetAtPath<MapRoot>(mapPath).displayName == "更新地图", "修改副本不改原地图");
                EditorSceneManager.SaveScene(scene);
                MapEditorWorkspace.OpenMap(mapPath);
                Check(MapEditorWorkspace.Current.displayName == "更新地图" && MapEditorWorkspace.AssetPath == mapPath, "关闭地图后重新打开正确资产");
                EditorSceneManager.SaveScene(scene);
                MapEditorWorkspace.NewMap();
                Check(MapEditorWorkspace.Current != null && MapEditorWorkspace.Current.Spawns.Length == 0 && string.IsNullOrEmpty(MapEditorWorkspace.AssetPath), "新建空白地图解除旧资产绑定");
                Check(Hash(LevelBuilderScene.ExplorePath) == originalFile && Hash("Assets/Map/Prefabs/Map/MapBase.prefab") == sharedFile, "保留原演示场景与共享地形");
                Debug.Log($"MAP_EDIT_CHECKS_PASSED: {checks} checks; dataPath={Application.dataPath}; product={Application.productName}");
            }
            finally
            {
                SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
                // Only assets created by this test; never delete user map directories.
                foreach (string path in new[] { mapPath, copyPath, scenePath }) if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            }
        }

        [MenuItem("LootTheDeep/关卡编辑器/验证加载搜索撤离与试玩隔离 %#F1")]
        public static void PlayTests()
        {
            if (!LevelBuilderScene.CanEdit) throw new InvalidOperationException("请在 MapEditorScene 编辑模式验证。");
            if (MapEditorValidation.Check(MapEditorWorkspace.Current, 12345).Any(i => i.error)) throw new InvalidOperationException("请先修正地图错误。");
            string path = MapEditorWorkspace.SaveMap(false);
            if (path == null) return;
            SessionState.SetString(Key + ".Path", path); SessionState.SetString(Key + ".Hash", Hash(path));
            SessionState.SetString(Key + ".Scene", Hash(MapEditorWorkspace.EditorPath));
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".Passed", false);
            MapEditorWorkspace.Play(path, 12345, 0, true);
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                checks = step = 0; next = EditorApplication.timeSinceStartup + .6; deadline = next + 40;
                nextFrame = Time.frameCount + 3; nextGameTime = Time.time + .2f;
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                checks = SessionState.GetInt(Key + ".Checks", checks);
                EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
                try
                {
                    Check(SessionState.GetBool(Key + ".Passed", false), "运行测试完成");
                    Check(SceneManager.GetActiveScene().path == MapEditorWorkspace.EditorPath, "退出试玩返回制作场景");
                    Check(Hash(SessionState.GetString(Key + ".Path", "")) == SessionState.GetString(Key + ".Hash", "") && Hash(MapEditorWorkspace.EditorPath) == SessionState.GetString(Key + ".Scene", ""), "试玩未写回地图或制作场景");
                    Check(MapEditorWorkspace.Current.All<SearchableContainer>().All(b => b.Contents == null && b.FoundCount == 0), "编辑地图恢复未搜索状态");
                    Debug.Log($"MAP_PLAY_CHECKS_PASSED: {checks} checks; load, A*, F search, E extraction, restart, edit restoration");
                }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < next) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("测试超时，step=" + step);
                if (Time.frameCount < nextFrame || Time.timeScale > 0 && Time.time < nextGameTime) return;
                switch (step)
                {
                    case 0:
                        loader = Object.FindFirstObjectByType<MapPlaytestController>();
                        inventory = Object.FindFirstObjectByType<InventorySession>(); session = Object.FindFirstObjectByType<CombatPrototypeSession2D>();
                        Check(loader != null && loader.LoadedMap != null && inventory != null && session != null, "独立场景加载地图和游戏环境");
                        Check(Vector2.Distance(inventory.player.transform.position, loader.SelectedSpawn.transform.position) < .1f, "玩家在指定候选点出生");
                        Check(loader.ActiveExitCount == loader.LoadedMap.activeExtractionCount, "仅启用本局指定数量的可达出口");
                        var enemies = Object.FindObjectsByType<EnemyCombat2D>(FindObjectsSortMode.None);
                        MapSpawnPlanner.PlanEnemies(loader.LoadedMap, loader.SelectedSpawn.transform.position, loader.seed, out var planned, out _);
                        Check(enemies.Length == planned.Count && enemies.All(e => planned.Any(p => p.position == (Vector2)typeof(EnemyCombat2D).GetField("home", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(e))), "区域生成数量正确，AI 原点就是最终落点");
                        foreach (var enemy in enemies) { enemy.enabled = false; enemy.GetComponent<Rigidbody2D>().simulated = false; }
                        var path = new List<Vector2>();
                        Check(loader.LoadedMap.navigation.TryFindPath(new Vector2(-4.5f, 5.5f), new Vector2(4.5f, 5.5f), path) && path.Count > 1, "地图中 A* 能绕墙");
                        box = loader.LoadedMap.All<SearchableContainer>().First(b => b.Contents != null && b.Current != null);
                        Check(loader.LoadedMap.All<SearchableContainer>().All(b => b.Contents != null && b.FoundCount == 0), "加载顺序使所有容器正确初始化");
                        MovePlayer((Vector2)box.transform.position + Vector2.left);
                        Keys(); Next(.2); break;
                    case 1: Keys(UnityEngine.InputSystem.Key.F); Next(.15); break;
                    case 2:
                        Keys(); Check(inventory.IsOpen && inventory.Target == box && box.Searching, $"F 键进入地图容器搜索 (open={inventory.IsOpen}, target={inventory.Target}, distance={Vector2.Distance(inventory.player.transform.position, box.transform.position)}, frame={Time.frameCount}, scale={Time.timeScale})");
                        box.Tick(box.searchSeconds + .1f); Check(box.FoundCount > 0, "搜索可揭示物资");
                        inventory.Close();
                        // Exercise simultaneous exits, including a far point which used to erase the nearby prompt.
                        foreach (var candidate in loader.LoadedMap.All<ExtractionPoint2D>()) candidate.gameObject.SetActive(true);
                        var exit = loader.LoadedMap.Exits.First(); MovePlayer(exit.transform.position);
                        Next(.2); break;
                    case 3:
                        Check(session.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("按住 E")), "多出口不会互相清除撤离提示");
                        Keys(UnityEngine.InputSystem.Key.E); Next(2.4); break;
                    case 4:
                        Keys(); Check(session.IsFinished && Time.timeScale == 0, "按住 E 完成测试撤离");
                        Check(session.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("尚未入库")), "结算明确未接入永久入库");
                        session.Restart(); Next(1); break;
                    case 5:
                        loader = Object.FindFirstObjectByType<MapPlaytestController>(); inventory = Object.FindFirstObjectByType<InventorySession>();
                        Check(loader.LoadedMap != null && !Object.FindFirstObjectByType<CombatPrototypeSession2D>().IsFinished && inventory != null, "重试保留地图选择并重新初始化");
                        Check(loader.LoadedMap.All<SearchableContainer>().All(b => b.FoundCount == 0), "重试不继承已搜索状态");
                        var enemyToKill = Object.FindFirstObjectByType<EnemyCombat2D>();
                        if (enemyToKill != null) { enemyToKill.transform.position += Vector3.up * 4; enemyToKill.GetComponent<CombatHealth2D>().TakeDamage(int.MaxValue); }
                        inventory.player.transform.position += Vector3.right * 5;
                        SessionState.SetBool(Key + ".Passed", true); SessionState.SetInt(Key + ".Checks", checks); EditorApplication.update -= Tick; EditorApplication.isPlaying = false;
                        break;
                }
            }
            catch (Exception e)
            { Debug.LogException(e); Keys(); EditorApplication.update -= Tick; EditorApplication.isPlaying = false; }
        }

        private static void Next(double seconds)
        { step++; next = EditorApplication.timeSinceStartup + seconds; nextGameTime = Time.time + (float)seconds; nextFrame = Time.frameCount + 3; }
        private static void Keys(params Key[] keys) { if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); }
        private static void MovePlayer(Vector2 position)
        { inventory.player.GetComponent<Rigidbody2D>().position = position; inventory.player.transform.position = position; Physics2D.SyncTransforms(); }
        private static string Hash(string path) => Hash128.Compute(File.ReadAllText(path)).ToString();
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("MAP_CHECK_FAILED: " + message); checks++; }
    }
}
