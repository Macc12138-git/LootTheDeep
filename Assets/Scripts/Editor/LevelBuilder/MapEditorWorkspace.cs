using System;
using System.Collections.Generic;
using System.Linq;
using LootTheDeep.Gameplay;
using LootTheDeep.Gameplay.Maps;
using LootTheDeep.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace LootTheDeep.LevelBuilder
{
    [InitializeOnLoad]
    internal static class MapEditorWorkspace
    {
        public const string EditorPath = "Assets/Scenes/MapEditorScene.unity";
        public const string PlayPath = "Assets/Scenes/MapPlaytestScene.unity";
        public const string DemoPath = "Assets/Maps/Map_001/Map_001.prefab";
        public const string RigPath = "Assets/Prefabs/Development/MapPlaytestRig.prefab";
        private const string PreviousStart = "LootTheDeep.MapEditor.PreviousStart";
        public static MapRoot Current => LevelBuilderScene.All<MapRoot>(SceneManager.GetActiveScene()).FirstOrDefault();
        public static string AssetPath => Current == null ? "" : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(Current);

        static MapEditorWorkspace()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PreviousStart + ".Set", false))
                    EditorApplication.ExecuteMenuItem("Window/General/Game");
                if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(PreviousStart + ".Set", false)) return;
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousStart, ""));
                SessionState.SetBool(PreviousStart + ".Set", false);
            };
        }

        [MenuItem("LootTheDeep/关卡编辑器/打开制作场景")]
        public static void OpenEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (SceneManager.GetActiveScene().path == EditorPath) { LevelBuilderWindow.Open(); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureAssets();
            EditorSceneManager.OpenScene(EditorPath);
            LevelBuilderWindow.Open();
            Selection.activeGameObject = Current.gameObject;
            SceneView.lastActiveSceneView?.Frame(new Bounds(Vector3.zero, new Vector3(34, 26, 1)), false);
        }

        public static void EnsureAssets()
        {
            if (Application.dataPath != System.IO.Path.GetFullPath("Assets")) throw new InvalidOperationException("工程路径不一致。");
            var active = SceneManager.GetActiveScene();
            EnsureFolder("Assets/Maps/Map_001"); EnsureFolder("Assets/Prefabs/Development");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RigPath) == null) CreateRig();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorPath) == null)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                try
                {
                    var existing = AssetDatabase.LoadAssetAtPath<MapRoot>(DemoPath);
                    if (existing != null) PrefabUtility.InstantiatePrefab(existing.gameObject, scene);
                    else
                    {
                        var root = CreateMap(scene, "Map_001");
                        SeedDemo(root);
                        root.RebuildNavigation();
                        PrefabUtility.SaveAsPrefabAssetAndConnect(root.gameObject, DemoPath, InteractionMode.AutomatedAction);
                    }
                    EditorSceneManager.SaveScene(scene, EditorPath);
                }
                finally { EditorSceneManager.CloseScene(scene, true); }
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlayPath) == null)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                try
                {
                    var loader = new GameObject("MapLoader").AddComponent<MapPlaytestController>();
                    SceneManager.MoveGameObjectToScene(loader.gameObject, scene);
                    loader.mapPrefab = AssetDatabase.LoadAssetAtPath<MapRoot>(DemoPath);
                    loader.gameplayRig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
                    EditorSceneManager.SaveScene(scene, PlayPath);
                }
                finally { EditorSceneManager.CloseScene(scene, true); }
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == PlayPath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(PlayPath, true) }).ToArray();
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            AssetDatabase.SaveAssets();
            Debug.Log($"MAP_EDITOR_READY: dataPath={Application.dataPath}; product={Application.productName}; scene={active.path}");
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }

        internal static MapRoot CreateMap(Scene scene, string id)
        {
            var root = new GameObject(id).AddComponent<MapRoot>();
            SceneManager.MoveGameObjectToScene(root.gameObject, scene);
            root.mapId = id; root.displayName = id;
            root.terrain = Child(root.transform, "Terrain"); root.obstacles = Child(root.transform, "Obstacles");
            root.containers = Child(root.transform, "Containers"); root.playerSpawnPoints = Child(root.transform, "PlayerSpawnPoints");
            root.extractionPoints = Child(root.transform, "ExtractionPoints"); root.enemyRegions = Child(root.transform, "EnemyRegions");
            var grid = root.terrain.gameObject.AddComponent<Grid>(); grid.cellSize = new Vector3(1, 1, 0);
            var terrain = Child(root.terrain, "Ground").gameObject;
            root.ground = terrain.AddComponent<Tilemap>(); terrain.AddComponent<TilemapRenderer>().sortingLayerName = "World";
            terrain.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            terrain.AddComponent<TilemapCollider2D>().compositeOperation = Collider2D.CompositeOperation.Merge;
            terrain.AddComponent<CompositeCollider2D>();
            root.navigation = root.gameObject.AddComponent<TilemapPathfinder2D>();
            var region = Child(root.containers, "默认搜索区域").gameObject.AddComponent<RegionContainerManager>();
            region.regionName = "默认搜索区域";
            return root;
        }

        internal static Transform Child(Transform parent, string name)
        { var value = new GameObject(name).transform; value.SetParent(parent, false); return value; }

        internal static GameObject Marker(MapRoot map, Type type, Vector3 position)
        {
            Transform parent = type == typeof(PlayerSpawnPoint2D) ? map.playerSpawnPoints : type == typeof(ExtractionPoint2D) ? map.extractionPoints : map.enemyRegions;
            string label = type == typeof(PlayerSpawnPoint2D) ? "出生点" : type == typeof(ExtractionPoint2D) ? "撤离点" : "敌人区域";
            var child = Child(parent, label + "_" + (parent.childCount + 1));
            child.position = position;
            child.gameObject.AddComponent(type);
            if (type == typeof(ExtractionPoint2D))
            {
                var visual = Child(child, "标记"); visual.localScale = new Vector3(1.6f, 1.6f, 1);
                var renderer = visual.gameObject.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/PrototypeSquare.png");
                renderer.color = new Color(.2f, 1, .5f, .6f); renderer.sortingLayerName = "World"; renderer.sortingOrder = 2;
            }
            return child.gameObject;
        }

        private static void SeedDemo(MapRoot root)
        {
            var library = LevelBuilderLibrary.LoadOrCreate();
            var floor = library.tiles.OfType<Tile>().First(t => t.colliderType == Tile.ColliderType.None);
            var wall = library.tiles.OfType<Tile>().First(t => t.colliderType != Tile.ColliderType.None);
            foreach (var p in new BoundsInt(-16, -12, 0, 32, 24, 1).allPositionsWithin) root.ground.SetTile(p, floor);
            for (int y = -7; y < 8; y++) if (Mathf.Abs(y) > 1) root.ground.SetTile(new Vector3Int(0, y), wall);
            Marker(root, typeof(PlayerSpawnPoint2D), new Vector3(-10.5f, .5f));
            Marker(root, typeof(PlayerSpawnPoint2D), new Vector3(-10.5f, -5.5f));
            Marker(root, typeof(ExtractionPoint2D), new Vector3(11.5f, .5f));
            Marker(root, typeof(ExtractionPoint2D), new Vector3(11.5f, -7.5f));
            var enemyRegion = Marker(root, typeof(EnemyRegion2D), new Vector3(7, 6)).GetComponent<EnemyRegion2D>();
            enemyRegion.enemies.Add(new EnemyRegionEntry { prefab = AssetDatabase.LoadAssetAtPath<EnemyCombat2D>("Assets/Prefabs/Gameplay/RangedGuard.prefab") });
            enemyRegion.enemies.Add(new EnemyRegionEntry { prefab = AssetDatabase.LoadAssetAtPath<EnemyCombat2D>("Assets/Prefabs/Gameplay/MeleeRaider.prefab") });
            var entry = library.prefabs.First(e => e.kind == LevelAssetKind.Container);
            var region = root.GetComponentInChildren<RegionContainerManager>();
            for (int i = 0; i < 3; i++)
            {
                var box = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, root.gameObject.scene);
                box.transform.SetParent(region.transform, false); box.transform.position = new Vector3(-8.5f + i * 7, -3.5f);
                box.transform.localScale = entry.scale;
                var renderer = box.GetComponent<SpriteRenderer>(); renderer.sortingLayerName = "World"; renderer.sortingOrder = 10;
            }
        }

        private static void CreateRig()
        {
            // Work on a transient copy; never reparent or save objects in the user's ExploreScene.
            // URP registers preview lights globally too. Suspend/restore live lights during this import only.
            var lights = Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None).Where(l => l.enabled).ToArray();
            foreach (var light in lights) light.enabled = false;
            Scene preview = default;
            try
            {
                preview = EditorSceneManager.OpenPreviewScene(LevelBuilderScene.ExplorePath);
                var rig = new GameObject("MapPlaytestRig"); SceneManager.MoveGameObjectToScene(rig, preview);
                var roots = preview.GetRootGameObjects().Where(r => r != rig &&
                    (r.GetComponentInChildren<UnderwaterMover2D>(true) != null || r.GetComponentInChildren<Camera>(true) != null ||
                     r.GetComponentInChildren<Canvas>(true) != null || r.GetComponentInChildren<EventSystem>(true) != null || r.name == "Global Light 2D")).ToArray();
                foreach (var value in roots)
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.UnpackPrefabInstance(value, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    value.transform.SetParent(rig.transform, true);
                }
                foreach (var p in rig.GetComponentsInChildren<ExtractionPoint2D>(true)) Object.DestroyImmediate(p.gameObject);
                var inventory = rig.GetComponentInChildren<InventorySession>(true);
                inventory.containers = Array.Empty<SearchableContainer>();
                MapFeatureInstaller.CreateView(rig.transform);
                PrefabUtility.SaveAsPrefabAsset(rig, RigPath);
            }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                foreach (var light in lights) if (light != null) light.enabled = true;
            }
        }

        private static bool ConfirmSwitch()
        {
            if (!SceneManager.GetActiveScene().isDirty) return true;
            int answer = EditorUtility.DisplayDialogComplex("地图尚未保存", "保存当前 Map 预制体后继续？放弃会丢弃本次地图修改。", "保存 Map", "取消", "放弃修改");
            return answer == 2 || answer == 0 && SaveMap(false) != null;
        }

        public static void NewMap()
        {
            if (!ConfirmSwitch()) return;
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            Undo.ClearAll();
            Selection.activeGameObject = CreateMap(scene, "Map_New").gameObject;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void OpenMap(string path = null)
        {
            if (path == null) path = EditorUtility.OpenFilePanel("打开 Map 预制体", "Assets/Maps", "prefab");
            if (string.IsNullOrEmpty(path)) return;
            if (System.IO.Path.IsPathRooted(path)) path = FileUtil.GetProjectRelativePath(path);
            var asset = AssetDatabase.LoadAssetAtPath<MapRoot>(path);
            if (asset == null) { EditorUtility.DisplayDialog("不是 Map", "请选择带 MapRoot 的地图预制体。", "确定"); return; }
            if (!ConfirmSwitch()) return;
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            Undo.ClearAll();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset.gameObject, scene);
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static string SaveMap(bool saveAs, string path = null)
        {
            var root = Current;
            if (root == null) return null;
            var errors = MapEditorValidation.StructuralErrors(root);
            if (errors.Count > 0)
            { EditorUtility.DisplayDialog("无法导出 Map", string.Join("\n", errors.Select(e => e.message)), "确定"); return null; }
            if (path == null && !saveAs) path = AssetPath;
            if (string.IsNullOrEmpty(path))
            {
                EnsureFolder("Assets/Maps");
                path = EditorUtility.SaveFilePanelInProject("保存 Map 预制体", root.mapId, "prefab", "仅导出 MapRoot；不包含玩家、UI 和运行状态。", "Assets/Maps");
            }
            if (string.IsNullOrEmpty(path)) return null;
            if (!path.StartsWith("Assets/Maps/", StringComparison.Ordinal))
            { EditorUtility.DisplayDialog("保存路径", "请保存到 Assets/Maps 下。", "确定"); return null; }
            if (AssetDatabase.LoadMainAssetAtPath(path) != null && (saveAs || path != AssetPath))
            { EditorUtility.DisplayDialog("资源已存在", "另存为请使用新文件名，避免覆盖其他地图。", "确定"); return null; }
            root.RebuildNavigation();
            if (saveAs && PrefabUtility.IsPartOfPrefabInstance(root.gameObject))
                PrefabUtility.UnpackPrefabInstance(root.gameObject, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root.gameObject, path, InteractionMode.AutomatedAction, out bool success);
            if (!success) return null;
            EditorSceneManager.SaveScene(root.gameObject.scene);
            Debug.Log("MAP_SAVED: " + path);
            return path;
        }

        public static void Play(string path, int seed, int spawnIndex, bool showAll)
        {
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
            { EditorUtility.DisplayDialog("需要 Scene Reload", "请在 Project Settings > Editor 启用 Scene Reload，以隔离试玩状态。", "确定"); return; }
            EnsureAssets();
            var scene = EditorSceneManager.OpenScene(PlayPath, OpenSceneMode.Additive);
            try
            {
                var loader = LevelBuilderScene.All<MapPlaytestController>(scene).Single();
                loader.mapPrefab = AssetDatabase.LoadAssetAtPath<MapRoot>(path); loader.seed = seed; loader.spawnIndex = spawnIndex; loader.showAllCandidates = showAll;
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            SessionState.SetString(PreviousStart, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(PreviousStart + ".Set", true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(PlayPath);
            EditorApplication.isPlaying = true;
        }
    }
}
