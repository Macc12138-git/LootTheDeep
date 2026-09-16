using LootTheDeep.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LootTheDeep.Inventory.Editor
{
    // Unity requires a static entry point for MenuItem / batchmode executeMethod.
    // All scene nodes and components come from authored prefabs, not construction code.
    public sealed class SearchInventoryInstaller
    {
        [MenuItem("LootTheDeep/Install Search Inventory Prefabs")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            const string scenePath = "Assets/Scenes/ExploreScene.unity";
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isBatchMode) scene = EditorSceneManager.OpenScene(scenePath);
            if (scene.path != scenePath)
            {
                Debug.LogError("Open ExploreScene before installing the inventory prefabs.");
                return;
            }
            var player = Object.FindFirstObjectByType<UnderwaterMover2D>();
            if (player == null) throw new System.InvalidOperationException("ExploreScene Player is missing.");
            if (!PrefabUtility.IsPartOfPrefabInstance(player))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Gameplay")) AssetDatabase.CreateFolder("Assets/Prefabs", "Gameplay");
                PrefabUtility.SaveAsPrefabAssetAndConnect(player.gameObject, "Assets/Prefabs/Gameplay/Player.prefab", InteractionMode.AutomatedAction);
                EditorSceneManager.SaveScene(scene);
            }
            var session = Object.FindFirstObjectByType<InventorySession>();
            if (session != null)
            {
                Debug.Log("Search inventory already installed; existing prefab overrides preserved.");
                return;
            }
            const string folder = "Assets/Prefabs/Inventory/";
            var uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "SearchInventoryUI.prefab");
            var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "SearchableContainer.prefab");
            if (uiPrefab == null || boxPrefab == null) throw new System.InvalidOperationException("Inventory prefabs could not be imported.");
            var ui = (GameObject)PrefabUtility.InstantiatePrefab(uiPrefab, scene);
            Undo.RegisterCreatedObjectUndo(ui, "Install Search Inventory");
            session = ui.GetComponent<InventorySession>();
            session.player = player;
            var positions = new[] { new Vector2(1.5f, 0), new Vector2(5, -2), new Vector2(-4, -4) };
            session.containers = new SearchableContainer[positions.Length];
            var parent = GameObject.Find("Gameplay/Containers");
            for (int i = 0; i < positions.Length; i++)
            {
                var box = (GameObject)PrefabUtility.InstantiatePrefab(boxPrefab, scene);
                Undo.RegisterCreatedObjectUndo(box, "Install Search Container");
                if (parent != null) box.transform.SetParent(parent.transform, false);
                box.transform.position = player.transform.position + (Vector3)positions[i];
                box.name = "SearchContainer_" + (i + 1);
                session.containers[i] = box.GetComponent<SearchableContainer>();
                session.containers[i].displayName = "沉船维修箱 #00" + (i + 1);
                PrefabUtility.RecordPrefabInstancePropertyModifications(box.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(session.containers[i]);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(session);
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var events = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(folder + "InventoryEventSystem.prefab"), scene);
                Undo.RegisterCreatedObjectUndo(events, "Install UI Input");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = ui;
            Debug.Log("Search inventory prefabs installed in ExploreScene. E: search, Tab: inventory, R: rotate during drag.");
        }
    }
}
