using System;
using System.IO;
using System.Linq;
using System.Reflection;
using LootTheDeep.Gameplay;
using LootTheDeep.Gameplay.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace LootTheDeep.LevelBuilder
{
    internal static class MapBoundsValidation
    {
        [MenuItem("LootTheDeep/关卡编辑器/验证自动地图范围")]
        private static void Run()
        {
            if (!LevelBuilderScene.CanEdit) throw new InvalidOperationException("请在 MapEditorScene 编辑模式验证。");
            const string report = "Temp/MapBoundsValidation.txt";
            var original = SceneManager.GetActiveScene();
            var selected = Selection.objects;
            string identity = $"dataPath={Application.dataPath}; product={Application.productName}; scene={original.path}";
            int checks = 0;
            void Check(bool condition, string label)
            {
                if (!condition) throw new InvalidOperationException("MAP_BOUNDS_CHECK_FAILED: " + label);
                checks++;
            }
            string Hash(string path) => Hash128.Compute(File.ReadAllText(path)).ToString();
            string originalHash = Hash(original.path), demoHash = Hash(MapEditorWorkspace.DemoPath);
            string paletteHash = Hash("Assets/Map/Tile/MapBase.prefab");
            string mapPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Maps/__MapBoundsValidation.prefab");
            var scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scratch);
            try
            {
                Check(Application.dataPath == Path.GetFullPath("Assets") && original.path == MapEditorWorkspace.EditorPath,
                    "工程与制作场景正确");
                var root = MapEditorWorkspace.CreateMap(scratch, "BoundsValidation");
                root.transform.position = new Vector3(2000, 2000);
                var map = root.ground;
                var floor = LevelBuilderLibrary.LoadOrCreate().tiles.OfType<Tile>().First(t => t.colliderType == Tile.ColliderType.None);
                Check(new SerializedObject(root).FindProperty("bounds") == null, "不再保存独立的手动地图范围");
                Check(root.Bounds.size == Vector3Int.zero, "新地图范围为空而非默认矩形");
                Check(MapEditorValidation.Check(root, 12345).Any(i => i.message.Contains("尚未绘制")), "空地图检查提示先铺地面");

                var area = new BoundsInt(38, -25, 0, 8, 8, 1);
                LevelBuilderScene.Paint(map, area, floor);
                Check(root.Bounds == area, "绘制区域自动决定范围，允许超出原固定范围及负坐标");
                Vector3Int extra = new(70, 50, 0);
                map.SetTile(extra, floor);
                Check(root.Bounds.max == extra + Vector3Int.one, "向外绘制自动扩大范围");
                map.SetTile(extra, null);
                var stored = map.cellBounds;
                Check(root.Bounds == area, "擦除外侧地块自动缩小，不保留历史空白");
                Check(map.cellBounds == stored, "读取范围不修改 Tilemap 的 origin 或 size");
                map.SetEditorPreviewTile(extra, floor);
                Check(root.Bounds == area, "原生笔刷预览不计入地图范围");
                map.ClearAllEditorPreviewTiles();

                int group = LevelBuilderScene.Begin("验证自动范围撤销");
                LevelBuilderScene.Paint(map, new BoundsInt(extra, Vector3Int.one), floor);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Check(root.Bounds.Contains(extra), "绘制后范围更新");
                Undo.PerformUndo(); Check(root.Bounds == area, "撤销绘制后范围恢复");
                Undo.PerformRedo(); Check(root.Bounds.Contains(extra), "重做绘制后范围恢复");
                map.SetTile(extra, null);

                group = LevelBuilderScene.Begin("验证擦除地图边缘");
                LevelBuilderScene.Paint(map, new BoundsInt(38, -25, 0, 1, 8, 1), null);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Check(root.Bounds == new BoundsInt(39, -25, 0, 7, 8, 1), "擦除最小边自动收缩起点和尺寸");
                Undo.PerformUndo(); Check(root.Bounds == area, "撤销擦除恢复原起点");

                group = LevelBuilderScene.Begin("验证清空地图");
                Undo.RegisterCompleteObjectUndo(map, "验证清空地图"); map.ClearAllTiles();
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Check(root.Bounds.size == Vector3Int.zero, "擦除全部地块后范围为空");
                Undo.PerformUndo(); Check(root.Bounds == area, "撤销清空恢复绘制范围");
                map.SetTile(new Vector3Int(40, -22, 2), floor);
                Check(MapEditorValidation.Check(root, 12345).Any(i => i.message.Contains("Z=0")), "多层地形给出明确错误");
                map.SetTile(new Vector3Int(40, -22, 2), null);

                var spawn = MapEditorWorkspace.Marker(root, typeof(PlayerSpawnPoint2D), map.GetCellCenterWorld(new Vector3Int(40, -22)));
                MapEditorWorkspace.Marker(root, typeof(ExtractionPoint2D), map.GetCellCenterWorld(new Vector3Int(43, -22)));
                Check(!MapEditorValidation.Check(root, 12345).Any(i => i.error), "原固定范围之外的出生点和撤离点正常检查");
                var hole = new Vector3Int(41, -22, 0);
                map.SetTile(hole, null); root.RebuildNavigation();
                Check(root.Bounds == area, "内部空洞不改变外接矩形");
                Vector2 holePosition = map.GetCellCenterWorld(hole);
                Check(!root.navigation.CanTraverse(holePosition, holePosition), "外接矩形内的空白不是可通行地面");
                Check(!root.navigation.CanTraverse(spawn.transform.position, map.GetCellCenterWorld(new Vector3Int(43, -22))), "导航不能直穿空洞");
                Check(!MapEditorValidation.Check(root, 12345).Any(i => i.error), "有绕行地面时仍可到达撤离点");
                spawn.transform.position = holePosition;
                Check(MapEditorValidation.Check(root, 12345).Any(i => i.message.Contains("出生点卡墙或没有地面")), "空洞上的出生点被阻止");
                spawn.transform.position = map.GetCellCenterWorld(new Vector3Int(40, -22));

                var world = root.WorldBounds;
                Check(world == new Rect(2038, 1975, 8, 8), "世界边界正确处理地图平移");
                root.transform.localScale = new Vector3(2, 3, 1);
                Check(root.WorldBounds == new Rect(2076, 1925, 16, 24), "世界边界正确处理地图缩放");
                root.transform.localScale = Vector3.one;

                var rig = new GameObject("范围验证玩法环境"); SceneManager.MoveGameObjectToScene(rig, scratch);
                var playerObject = new GameObject("Player"); playerObject.transform.SetParent(rig.transform, false);
                var player = playerObject.AddComponent<UnderwaterMover2D>();
                var body = player.GetComponent<Rigidbody2D>(); body.gravityScale = 0;
                Set(player, "body", body); Set(player, "instantMovement", true);
                player.ConfigureBounds(world); player.ConfigureGround(map);
                Vector2 Move(Vector2 from, Vector2 input, float speed = 4.5f)
                {
                    body.position = from; body.linearVelocity = Vector2.zero;
                    Set(player, "moveInput", input); Set(player, "moveSpeed", speed);
                    Invoke(player, "FixedUpdate");
                    return body.position + body.linearVelocity * Time.fixedDeltaTime;
                }
                Vector2 edge = new Vector2(2040.98f, 1978.5f);
                Check(Move(edge, Vector2.right) == edge, "玩家不能从地面走入内部空洞");
                var slide = Move(edge, Vector2.one.normalized);
                Check(Mathf.Approximately(slide.x, edge.x) && slide.y > edge.y, "斜向触碰空白边缘时保留沿边移动");
                var clear = new Vector2(2040.2f, 1977.5f);
                Check(Move(clear, Vector2.right).x > clear.x, "玩家可在实际地面正常移动");
                var from = new Vector2(2040.5f, 1978.5f);
                Check(Move(from, Vector2.right, 3f / Time.fixedDeltaTime) == from, "高速一步移动也不能跨过空白走到另一侧");
                var outerEdge = new Vector2(2045.98f, 1977.5f);
                Check(Move(outerEdge, Vector2.right) == outerEdge, "玩家不能走出实际地图边缘");
                player.ConfigureGround(null);
                Check(Move(edge, Vector2.right).x > edge.x, "未配置地图地面时保留旧场景移动行为");

                Check(PrefabUtility.SaveAsPrefabAsset(root.gameObject, mapPath) != null, "自动范围地图可保存预制体");
                var saved = AssetDatabase.LoadAssetAtPath<MapRoot>(mapPath);
                var reopened = ((GameObject)PrefabUtility.InstantiatePrefab(saved.gameObject, scratch)).GetComponent<MapRoot>();
                Check(reopened.Bounds == area && !reopened.ground.HasTile(hole), "重开地图从瓦片还原范围并保留空洞");
                Object.DestroyImmediate(reopened.gameObject);
                var camera = new GameObject("Camera"); camera.transform.SetParent(rig.transform, false);
                camera.AddComponent<Camera>().orthographic = true;
                var follow = camera.AddComponent<CameraFollow2D>();
                var loader = new GameObject("范围验证加载器").AddComponent<MapPlaytestController>();
                loader.mapPrefab = saved; loader.gameplayRig = rig;
                Invoke(loader, "Awake");
                Check(loader.LoadedMap != null && loader.SelectedSpawn != null, "加载器可加载旧固定范围外的地图");
                var loadedPlayer = LevelBuilderScene.All<UnderwaterMover2D>(scratch).Single(p => p != player);
                Check(Get<Tilemap>(loadedPlayer, "movementGround") == loader.LoadedMap.ground, "加载器将实际地面绑定给玩家");
                Check(Get<Vector2>(loadedPlayer, "horizontalRange") == new Vector2(world.xMin, world.xMax) &&
                    Get<float>(loadedPlayer, "minimumWorldY") == world.yMin && Get<float>(loadedPlayer, "maximumWorldY") == world.yMax,
                    "玩家运行边界来自实际瓦片范围");
                var loadedCamera = LevelBuilderScene.All<CameraFollow2D>(scratch).Single(c => c != follow);
                Check(Get<Vector2>(loadedCamera, "horizontalRange") == new Vector2(world.xMin, world.xMax) &&
                    Get<float>(loadedCamera, "minimumWorldY") == world.yMin && Get<float>(loadedCamera, "maximumWorldY") == world.yMax,
                    "镜头边界来自实际瓦片范围");
                Check(Hash(original.path) == originalHash && Hash(MapEditorWorkspace.DemoPath) == demoHash &&
                    Hash("Assets/Map/Tile/MapBase.prefab") == paletteHash, "未修改用户制作场景、示例地图或共享调色板文件");
                string result = $"MAP_BOUNDS_CHECKS_PASSED: {checks} checks; {identity}";
                File.WriteAllText(report, result); Debug.Log(result);
            }
            catch (Exception exception)
            {
                File.WriteAllText(report, identity + "\n" + exception); throw;
            }
            finally
            {
                SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scratch, true);
                if (AssetDatabase.LoadMainAssetAtPath(mapPath) != null) AssetDatabase.DeleteAsset(mapPath);
                Selection.objects = selected;
            }
        }

        private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
        private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
        private static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
