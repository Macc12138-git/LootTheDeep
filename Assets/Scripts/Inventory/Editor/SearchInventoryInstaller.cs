using System;
using System.Collections.Generic;
using LootTheDeep.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace LootTheDeep.Inventory.Editor
{
    public sealed class SearchInventoryInstaller
    {
        [MenuItem("LootTheDeep/搜索/接入地图容器 %#F5")]
        public static void Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                scene.path != "Assets/Scenes/ExploreScene.unity")
                throw new InvalidOperationException("请在 ExploreScene 编辑模式中接入搜索，等待编译完成。");
            if (Object.FindFirstObjectByType<InventorySession>() != null)
            {
                Debug.Log("搜索已接入，保留现有容器与界面配置。");
                return;
            }
            var player = Object.FindFirstObjectByType<UnderwaterMover2D>();
            var map = Object.FindFirstObjectByType<Tilemap>();
            var navigation = Object.FindFirstObjectByType<TilemapPathfinder2D>();
            const string folder = "Assets/Prefabs/Inventory/";
            var uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "SearchInventoryUI.prefab");
            var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "SearchableContainer.prefab");
            if (player == null || map == null || navigation == null || uiPrefab == null || boxPrefab == null)
                throw new InvalidOperationException("缺少当前角色、Tilemap、导航或搜索预制体。");

            Physics2D.SyncTransforms();
            navigation.Rebuild();
            Vector2 origin = player.transform.position;
            var offsets = new[] { new Vector2(1.5f, 0), new Vector2(0, 6), new Vector2(0, -6),
                new Vector2(8, 12), new Vector2(8, -12) };
            var names = new[] { "路口物资箱", "北侧补给箱", "南侧补给箱", "北部储物箱", "南部储物箱" };
            var positions = new List<Vector2>();
            for (int i = 0; i < offsets.Length; i++)
                positions.Add(FindPlacement(map, navigation, origin, origin + offsets[i], positions, i == 0));

            var ui = (GameObject)PrefabUtility.InstantiatePrefab(uiPrefab, scene);
            Undo.RegisterCreatedObjectUndo(ui, "接入地图搜索");
            var session = ui.GetComponent<InventorySession>();
            session.player = player;
            PrefabUtility.RecordPrefabInstancePropertyModifications(session);
            var root = new GameObject("MapContainers");
            Undo.RegisterCreatedObjectUndo(root, "接入地图容器");
            var region = root.AddComponent<RegionContainerManager>();
            region.regionName = "探索物资区";
            for (int i = 0; i < positions.Count; i++)
            {
                var box = (GameObject)PrefabUtility.InstantiatePrefab(boxPrefab, scene);
                Undo.RegisterCreatedObjectUndo(box, "放置物资箱");
                box.transform.SetParent(root.transform, false);
                box.transform.position = positions[i];
                box.name = "SearchContainer_" + (i + 1);
                var visual = box.GetComponent<SpriteRenderer>();
                float scale = 1.1f / Mathf.Max(visual.sprite.bounds.size.x, visual.sprite.bounds.size.y);
                box.transform.localScale = new Vector3(scale, scale, 1f);
                visual.sortingLayerName = "World";
                visual.sortingOrder = 10;
                var container = box.GetComponent<SearchableContainer>();
                container.displayName = names[i];
                PrefabUtility.RecordPrefabInstancePropertyModifications(box.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
                PrefabUtility.RecordPrefabInstancePropertyModifications(container);
            }
            region.RefreshContainers();
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var events = PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(folder + "InventoryEventSystem.prefab"), scene);
                Undo.RegisterCreatedObjectUndo(events, "接入搜索输入");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
            Debug.Log($"WORLD_SEARCH_INSTALLED: dataPath={Application.dataPath}; product={Application.productName}; " +
                $"scene={scene.path}; containers={positions.Count}; positions={string.Join(", ", positions)}. F 搜索，Tab 背包，Esc 关闭。");
        }

        private static Vector2 FindPlacement(Tilemap map, TilemapPathfinder2D navigation, Vector2 origin,
            Vector2 preferred, List<Vector2> placed, bool nearSpawn)
        {
            var path = new List<Vector2>();
            for (int radius = 0; radius <= 8; radius++)
                for (int direction = 0; direction < (radius == 0 ? 1 : 8); direction++)
                {
                    float angle = direction * Mathf.PI / 4f;
                    Vector2 candidate = preferred + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius * .5f);
                    if (nearSpawn && Vector2.Distance(origin, candidate) > 1.8f ||
                        Vector2.Distance(origin, candidate) < 1.25f || !map.HasTile(map.WorldToCell(candidate))) continue;
                    bool blocked = false;
                    foreach (var position in placed)
                        if (Vector2.Distance(candidate, position) < 3f) { blocked = true; break; }
                    foreach (var hit in Physics2D.OverlapCircleAll(candidate, .65f))
                        if (!hit.isTrigger) { blocked = true; break; }
                    if (!blocked && navigation.TryFindPath(origin, candidate, path)) return candidate;
                }
            throw new InvalidOperationException("找不到可达的容器位置，请检查当前地图通路：" + preferred);
        }
    }
}
