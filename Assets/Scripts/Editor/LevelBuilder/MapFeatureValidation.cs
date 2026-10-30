using System;
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
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LootTheDeep.LevelBuilder
{
    [InitializeOnLoad]
    public static class MapFeatureValidation
    {
        private const string StateKey = "LootTheDeep.MapFeatureValidation";
        private const string Report = "Temp/MapFeatureValidation.txt";
        private static int checks, step, nextFrame, ammo, weapon;
        private static double next, deadline;
        private static float frozenTime;
        private static Vector3[] enemyPositions;
        private static Vector3 projectilePosition;
        private static EnemyCombat2D[] enemies;
        private static ShootingProjectile2D projectile;
        private static MapPlaytestController loader;
        private static MapView2D view;
        private static CombatPrototypeSession2D session;
        private static InventorySession inventory;
        private static PlayerShooter2D shooter;
        private static SearchableContainer box;
        private static ExtractionPoint2D exit;
        private static LootItem draggedItem;
        private static Texture previousTexture;

        [Serializable] private sealed class BuildState { public string[] paths; public bool[] enabled; }

        static MapFeatureValidation() => EditorApplication.playModeStateChanged += StateChanged;

        [MenuItem("LootTheDeep/地图/验证地图底图与界面")]
        public static void EditTests()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式验证。");
            checks = 0;
            File.WriteAllText(Report, $"EDIT: {Application.dataPath}; {Application.productName}\n");
            var original = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Tile floor = ScriptableObject.CreateInstance<Tile>(), wall = ScriptableObject.CreateInstance<Tile>();
            floor.colliderType = Tile.ColliderType.None; wall.colliderType = Tile.ColliderType.Grid;
            Texture2D texture = null;
            try
            {
                var map = MapEditorWorkspace.CreateMap(scene, "MapFeatureFixture");
                map.transform.position = new Vector3(300, -200);
                map.transform.localScale = new Vector3(1.5f, 1.25f, 1);
                foreach (var cell in new BoundsInt(-5, -3, 0, 10, 6, 1).allPositionsWithin) map.ground.SetTile(cell, floor);
                map.ground.SetTile(new Vector3Int(-2, -1), null);
                map.ground.SetTile(new Vector3Int(0, 0), wall);
                map.ground.SetEditorPreviewTile(new Vector3Int(100, 100), floor);
                var triangle = new GameObject("Triangle").AddComponent<PolygonCollider2D>();
                triangle.transform.SetParent(map.obstacles, false);
                triangle.transform.localPosition = new Vector3(2, 1);
                triangle.points = new[] { Vector2.zero, Vector2.right, Vector2.up };
                var trigger = new GameObject("Trigger").AddComponent<BoxCollider2D>();
                trigger.transform.SetParent(map.obstacles, false); trigger.transform.localPosition = new Vector3(-3.5f, .5f);
                trigger.isTrigger = true;
                var disabled = new GameObject("DisabledObstacle").AddComponent<BoxCollider2D>();
                disabled.transform.SetParent(map.obstacles, false); disabled.transform.localPosition = new Vector3(-4.5f, .5f); disabled.enabled = false;
                var moving = new GameObject("DynamicObject").AddComponent<BoxCollider2D>();
                moving.transform.SetParent(map.obstacles, false); moving.transform.localPosition = new Vector3(-4.5f, 1.5f);
                moving.gameObject.AddComponent<Rigidbody2D>().gravityScale = 0;
                var containerCollider = new GameObject("NotAnObstacle").AddComponent<BoxCollider2D>();
                containerCollider.transform.SetParent(map.containers, false); containerCollider.transform.localPosition = new Vector3(-3.5f, 1.5f);
                var active = MapEditorWorkspace.Marker(map, typeof(ExtractionPoint2D), map.ground.GetCellCenterWorld(new Vector3Int(3, -2)));
                var inactive = MapEditorWorkspace.Marker(map, typeof(ExtractionPoint2D), map.ground.GetCellCenterWorld(new Vector3Int(3, 1)));
                inactive.SetActive(false);
                map.RebuildNavigation();
                texture = MapTextureBuilder.Build(map, out Rect bounds);
                Check(Mathf.Approximately(bounds.width, 15) && Mathf.Approximately(bounds.height, 7.5f), "实际范围支持负格坐标、地图平移和正缩放，排除预览格");
                Check(Sample(texture, bounds, map.ground.GetCellCenterWorld(new Vector3Int(-4, -2))).Equals(MapTextureBuilder.GroundColor), "地面绘制");
                Check(Sample(texture, bounds, map.ground.GetCellCenterWorld(new Vector3Int(-2, -1))).Equals(MapTextureBuilder.EmptyColor), "保留内部空洞");
                foreach (var collider in map.terrain.GetComponentsInChildren<Collider2D>())
                    File.AppendAllText(Report, $"TERRAIN_COLLIDER {collider.GetType().Name}: shapes={collider.shapeCount}; bounds={collider.bounds}; overlap={collider.OverlapPoint(map.ground.GetCellCenterWorld(Vector3Int.zero))}" +
                        (collider is CompositeCollider2D composite ? $"; geometry={composite.geometryType}; paths={composite.pathCount}; points={composite.pointCount}" : "") + "\n");
                Check(Sample(texture, bounds, map.ground.GetCellCenterWorld(Vector3Int.zero)).Equals(MapTextureBuilder.ObstacleColor), "地形内墙体绘制");
                Check(Sample(texture, bounds, triangle.transform.TransformPoint(new Vector3(.2f, .2f))).Equals(MapTextureBuilder.ObstacleColor), "独立障碍按实际轮廓绘制");
                Check(Sample(texture, bounds, triangle.transform.TransformPoint(new Vector3(.85f, .85f))).Equals(MapTextureBuilder.GroundColor), "不把三角形包围盒误画成障碍");
                Check(Sample(texture, bounds, trigger.transform.position).Equals(MapTextureBuilder.GroundColor), "触发器不显示");
                Check(Sample(texture, bounds, disabled.transform.position).Equals(MapTextureBuilder.GroundColor), "禁用障碍不显示");
                Check(Sample(texture, bounds, moving.transform.position).Equals(MapTextureBuilder.GroundColor), "动态物体不显示");
                Check(Sample(texture, bounds, containerCollider.transform.position).Equals(MapTextureBuilder.GroundColor), "容器层对象不进入底图");
                var uiRoot = new GameObject("MapViewFixture"); SceneManager.MoveGameObjectToScene(uiRoot, scene);
                var mapView = MapFeatureInstaller.CreateView(uiRoot.transform);
                mapView.Initialize(map);
                Check(mapView.IsReady && !mapView.IsOpen, "地图初始化后默认关闭");
                var marker = mapView.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "ActiveExit");
                Vector2 expected = ((Vector2)active.transform.position - bounds.min) / bounds.size;
                Check(Vector2.Distance(marker.anchorMin, expected) < .0001f && marker.anchorMin == marker.anchorMax, "只显示有效出口且世界坐标映射正确");
                Check(marker.sizeDelta == new Vector2(26, 26), "出口图标保持固定尺寸");
                mapView.Show(true); Canvas.ForceUpdateCanvases();
                Check(marker.GetComponentInChildren<Text>().cachedTextGenerator.vertexCount >= 4, "撤离图标的中文字形实际生成，不被行高裁掉");
                var image = mapView.GetComponentInChildren<RawImage>(true);
                var content = image.rectTransform.parent as RectTransform;
                Check(Mathf.Abs(content.rect.width / content.rect.height - bounds.width / bounds.height) < .01f, "地图等比例适配面板");
                Check(content.rect.width <= ((RectTransform)content.parent).rect.width + 1 && content.rect.height <= ((RectTransform)content.parent).rect.height + 1, "全图完整落在面板内");
                Check(mapView.GetComponentsInChildren<RawImage>(true).Length == 1 && content.childCount == 2, "地图内容仅有底图和出口层");
                Texture old = image.texture;
                mapView.Initialize(map);
                Check(old == null && mapView.GetComponentsInChildren<RectTransform>(true).Count(t => t.name == "ActiveExit") == 1, "重新初始化释放旧底图且不累积出口");
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(MapEditorWorkspace.RigPath).GetComponentInChildren<MapView2D>(true) != null, "实际试玩 Rig 已接入地图 UI");
                File.AppendAllText(Report, $"EDIT_PASSED: {checks}\n");
                Debug.Log($"MAP_FEATURE_EDIT_PASSED: {checks}");
            }
            catch (Exception error) { File.AppendAllText(Report, "EDIT_FAILED: " + error + "\n"); throw; }
            finally
            {
                if (texture != null) Object.DestroyImmediate(texture);
                SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
                Object.DestroyImmediate(floor); Object.DestroyImmediate(wall);
            }
        }

        private static Color32 Sample(Texture2D texture, Rect bounds, Vector2 point)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((point.x - bounds.xMin) / bounds.width * texture.width), 0, texture.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((point.y - bounds.yMin) / bounds.height * texture.height), 0, texture.height - 1);
            return texture.GetPixels32()[y * texture.width + x];
        }

        [MenuItem("LootTheDeep/地图/验证地图开关与行动隔离")]
        public static void PlayTests()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式启动验证。");
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                throw new InvalidOperationException("地图验证需要 Scene Reload。");
            string fixture = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/LevelBuilder/__MapFeatureValidation.unity");
            var original = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = new GameObject("MapFeatureTestLoader"); SceneManager.MoveGameObjectToScene(root, scene);
                var controller = root.AddComponent<MapPlaytestController>();
                controller.mapPrefab = AssetDatabase.LoadAssetAtPath<MapRoot>(MapEditorWorkspace.DemoPath);
                controller.gameplayRig = AssetDatabase.LoadAssetAtPath<GameObject>(MapEditorWorkspace.RigPath);
                controller.spawnIndex = 0;
                EditorSceneManager.SaveScene(scene, fixture);
            }
            finally { SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
            var build = EditorBuildSettings.scenes;
            SessionState.SetString(StateKey + ".Build", JsonUtility.ToJson(new BuildState { paths = build.Select(s => s.path).ToArray(), enabled = build.Select(s => s.enabled).ToArray() }));
            SessionState.SetString(StateKey + ".Start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(StateKey + ".Fixture", fixture);
            SessionState.SetString(StateKey + ".Original", original.path);
            SessionState.SetString(StateKey + ".MapHash", Hash(MapEditorWorkspace.DemoPath));
            SessionState.SetString(StateKey + ".SceneHash", Hash(original.path));
            SessionState.SetBool(StateKey, true); SessionState.SetBool(StateKey + ".Passed", false);
            EditorBuildSettings.scenes = build.Concat(new[] { new EditorBuildSettingsScene(fixture, true) }).ToArray();
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(fixture);
            File.AppendAllText(Report, "PLAY_STARTED\n");
            EditorApplication.isPlaying = true;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(StateKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                checks = step = 0; deadline = EditorApplication.timeSinceStartup + 50;
                next = EditorApplication.timeSinceStartup + .5; nextFrame = Time.frameCount + 4;
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
            }
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.update -= Tick;
            var build = JsonUtility.FromJson<BuildState>(SessionState.GetString(StateKey + ".Build", ""));
            EditorBuildSettings.scenes = build.paths.Select((path, i) => new EditorBuildSettingsScene(path, build.enabled[i])).ToArray();
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(StateKey + ".Start", ""));
            string original = SessionState.GetString(StateKey + ".Original", "");
            bool unchanged = Hash(original) == SessionState.GetString(StateKey + ".SceneHash", "") && Hash(MapEditorWorkspace.DemoPath) == SessionState.GetString(StateKey + ".MapHash", "");
            string fixture = SessionState.GetString(StateKey + ".Fixture", "");
            if (!string.IsNullOrEmpty(fixture)) AssetDatabase.DeleteAsset(fixture);
            bool passed = SessionState.GetBool(StateKey + ".Passed", false) && unchanged && SceneManager.GetActiveScene().path == original;
            string result = $"PLAY_{(passed ? "PASSED" : "FAILED")}: {SessionState.GetInt(StateKey + ".Checks", checks)}; original map/scene unchanged={unchanged}; restored={SceneManager.GetActiveScene().path}";
            File.AppendAllText(Report, result + "\n");
            SessionState.SetBool(StateKey, false);
            if (passed) Debug.Log(result); else Debug.LogError(result);
        }

        private static void Bind()
        {
            loader = Object.FindFirstObjectByType<MapPlaytestController>();
            view = Object.FindFirstObjectByType<MapView2D>();
            session = Object.FindFirstObjectByType<CombatPrototypeSession2D>();
            inventory = Object.FindFirstObjectByType<InventorySession>();
            shooter = Object.FindFirstObjectByType<PlayerShooter2D>();
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("地图运行验证超时，step=" + step);
                if (EditorApplication.timeSinceStartup < next || Time.frameCount < nextFrame) return;
                switch (step)
                {
                    case 0:
                        Bind(); Check(view != null && view.IsReady && !view.IsOpen, "实际 Map 加载后地图就绪且默认关闭");
                        Check(view.GetComponentsInChildren<RectTransform>(true).Count(t => t.name == "ActiveExit") == loader.ActiveExitCount, "图标数量与本局有效出口一致");
                        Time.timeScale = .65f;
                        shooter.ActiveGun.TryFire(Vector2.right, Time.time, out _); ammo = shooter.ActiveGun.Ammo; weapon = shooter.ActiveWeaponIndex;
                        projectile = Object.Instantiate(shooter.ActiveGun.Config.projectilePrefab, inventory.player.transform.position + Vector3.up * 3, Quaternion.identity);
                        projectile.Fire(Vector2.up, 1, 1, inventory.player.GetComponent<Collider2D>());
                        Keys(KeyCodeM); Next(); break;
                    case 1:
                        Check(session.IsMapOpen && Time.timeScale == 0 && session.GameplayInputBlocked, "M 打开地图并暂停行动");
                        ScreenCapture.CaptureScreenshot("Temp/MapFeatureMap.png");
                        frozenTime = Time.time; projectilePosition = projectile.transform.position;
                        enemies = Object.FindObjectsByType<EnemyCombat2D>(FindObjectsSortMode.None);
                        enemyPositions = enemies.Select(e => e.transform.position).ToArray();
                        AimAndPress(true); Keys(Key.W, Key.Tab, Key.F, Key.R, Key.Digit2, Key.E); Next(.35); break;
                    case 2:
                        Check(!inventory.IsOpen && session.IsMapOpen && !session.IsFinished, "地图内屏蔽背包、搜索和撤离");
                        Check(shooter.ActiveGun.Ammo == ammo && !shooter.ActiveGun.IsReloading && shooter.ActiveWeaponIndex == weapon, "地图内屏蔽射击、换弹和切枪");
                        Check(Time.time == frozenTime && projectile.transform.position == projectilePosition, "地图内冻结行动时间和弹丸");
                        Check(enemies.Select((e, i) => e.transform.position == enemyPositions[i]).All(v => v) && inventory.player.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero, "地图内冻结敌人和玩家移动");
                        session.SendMessage("OnApplicationFocus", false); Check(Time.timeScale == 0, "地图叠加失焦仍暂停");
                        session.SetMapOpen(false); Check(Time.timeScale == 0, "关闭地图不能解除失焦暂停");
                        session.SendMessage("OnApplicationFocus", true); Check(Mathf.Approximately(Time.timeScale, .65f), "全部暂停解除后恢复原时间倍率");
                        session.SetMapOpen(true); Keys();
                        foreach (var enemy in enemies) { enemy.enabled = false; enemy.GetComponent<Rigidbody2D>().simulated = false; }
                        Next(); break;
                    case 3:
                        Keys(Key.Escape, Key.Tab, Key.F, Key.R, Key.Digit2); Next(); break;
                    case 4:
                        Check(!session.IsMapOpen && Mathf.Approximately(Time.timeScale, .65f), "Esc 关闭地图并恢复行动");
                        Check(!inventory.IsOpen && shooter.ActiveWeaponIndex == weapon && !shooter.ActiveGun.IsReloading, "关闭当帧不透传 Tab/F/R/切枪");
                        Check(shooter.ActiveGun.Ammo == ammo, "关闭地图后长按鼠标不会误射，需先松开");
                        AimAndPress(false); Keys(); Time.timeScale = 1;
                        box = loader.LoadedMap.All<SearchableContainer>().First(b => b.Current != null);
                        MovePlayer((Vector2)box.transform.position + Vector2.left);
                        inventory.Open(box);
                        draggedItem = new LootItem(box.Current.Definition);
                        Check(inventory.Backpack.TryAddFirst(draggedItem), "创建本局拖拽物品夹具");
                        inventory.View.Refresh();
                        inventory.View.BeginDrag(draggedItem, Vector2.zero, inventory.View.PlayerGrids[0].GetComponent<RectTransform>());
                        Check(inventory.IsOpen && box.Searching && inventory.View.IsDragging, "搜索和拖拽同时进行时准备切换地图");
                        Keys(KeyCodeM); Next(); break;
                    case 5:
                        Check(session.IsMapOpen && !inventory.IsOpen && !inventory.View.IsDragging && !box.Searching && box.Progress == 0, "从背包打开地图会关闭背包、取消拖拽并停止搜索");
                        Check(inventory.Backpack.Items.Contains(draggedItem), "取消拖拽不丢失物品");
                        session.SetMapOpen(false); Keys(); Next(); break;
                    case 6:
                        Check(!inventory.IsOpen && !box.Searching, "关闭地图不自动恢复背包或搜索");
                        exit = loader.LoadedMap.Exits.First(); MovePlayer(exit.transform.position);
                        Keys(Key.E); Next(.35); break;
                    case 7:
                        Check(ExitProgress() > 0, "正常按 E 可推进撤离");
                        Keys(KeyCodeM, Key.E); Next(); break;
                    case 8:
                        Check(session.IsMapOpen && ExitProgress() == 0, "打开地图中断撤离读条");
                        Keys(Key.E); Next(.35); break;
                    case 9:
                        Check(!session.IsFinished && ExitProgress() == 0, "地图内持续按 E 不能撤离");
                        Keys(KeyCodeM); Next(); break;
                    case 10:
                        Check(!session.IsMapOpen && Time.timeScale == 1, "再次按 M 关闭地图");
                        Keys(); Next(); break;
                    case 11: Keys(Key.E); Next(2.5); break;
                    case 12:
                        Check(session.IsFinished && !session.IsMapOpen && Time.timeScale == 0, "地图关闭后正常撤离结算");
                        Keys(KeyCodeM); Next(); break;
                    case 13:
                        Check(!session.IsMapOpen && Time.timeScale == 0, "结算后 M 不能打开地图或解除暂停");
                        previousTexture = view.GetComponentInChildren<RawImage>(true).texture;
                        Keys(); session.Restart(); Next(.6); break;
                    case 14:
                        Bind(); Check(!session.IsFinished && !session.IsMapOpen && Time.timeScale == 1 && view.IsReady, "结算后重试恢复正常关闭状态");
                        Check(previousTexture == null && Object.FindObjectsByType<MapView2D>(FindObjectsSortMode.None).Length == 1, "重试释放旧底图且地图窗口不累积");
                        Time.timeScale = .5f; session.SetMapOpen(true); session.Restart(); Next(.6); break;
                    case 15:
                        Bind(); Check(!session.IsMapOpen && Time.timeScale == 1, "地图打开时重试不会遗留暂停");
                        session.SetMapOpen(true); session.Finish(false); session.SetMapOpen(true);
                        Check(session.IsFinished && !session.IsMapOpen && Time.timeScale == 0, "失败结算强制关闭地图且不能重新打开");
                        SessionState.SetBool(StateKey + ".Passed", true); SessionState.SetInt(StateKey + ".Checks", checks);
                        Keys(); AimAndPress(false); EditorApplication.update -= Tick; EditorApplication.isPlaying = false; break;
                }
            }
            catch (Exception error)
            {
                File.AppendAllText(Report, $"PLAY_ERROR step={step}: {error}\n"); Debug.LogException(error);
                SessionState.SetInt(StateKey + ".Checks", checks); Keys(); AimAndPress(false);
                EditorApplication.update -= Tick; EditorApplication.isPlaying = false;
            }
        }

        private const Key KeyCodeM = Key.M;
        private static void Next(double seconds = .15)
        { step++; next = EditorApplication.timeSinceStartup + seconds; nextFrame = Time.frameCount + 3; }
        private static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        private static void AimAndPress(bool pressed)
        {
            if (Mouse.current == null || inventory == null) return;
            var state = new MouseState { position = Camera.main.WorldToScreenPoint(inventory.player.transform.position + Vector3.right * 5) };
            state = state.WithButton(MouseButton.Left, pressed);
            InputSystem.QueueStateEvent(Mouse.current, state);
        }
        private static void MovePlayer(Vector2 position)
        { inventory.player.transform.position = position; inventory.player.GetComponent<Rigidbody2D>().position = position; Physics2D.SyncTransforms(); }
        private static float ExitProgress() => (float)typeof(ExtractionPoint2D).GetField("progress", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(exit);
        private static string Hash(string path) => File.Exists(path) ? Hash128.Compute(File.ReadAllText(path)).ToString() : "";
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("MAP_FEATURE_CHECK_FAILED: " + message);
            checks++; File.AppendAllText(Report, "PASS " + message + "\n");
        }
    }
}
