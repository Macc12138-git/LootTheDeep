using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace LootTheDeep.LevelBuilder
{
    public enum LevelAssetKind { Enemy, Container, Obstacle }

    [Serializable]
    public sealed class LevelPrefabEntry
    {
        public string label;
        public LevelAssetKind kind;
        public GameObject prefab;
        public Vector3 scale = Vector3.one;
        public bool allowRotation;
    }

    public sealed class LevelBuilderLibrary : ScriptableObject
    {
        public const string Folder = "Assets/Editor/LevelBuilder";
        public const string AssetPath = Folder + "/素材库.asset";
        public List<TileBase> tiles = new();
        public List<LevelPrefabEntry> prefabs = new();
        public List<LevelTileModule> modules = new();

        public static LevelBuilderLibrary LoadOrCreate()
        {
            var library = AssetDatabase.LoadAssetAtPath<LevelBuilderLibrary>(AssetPath);
            if (library != null) return library;
            EnsureFolder(Folder);
            library = CreateInstance<LevelBuilderLibrary>();
            library.tiles = AssetDatabase.FindAssets("t:Tile", new[] { "Assets/Map/Tile" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<TileBase>).Where(t => t != null).ToList();
            library.AddPrefab("枪手", LevelAssetKind.Enemy, "Assets/Prefabs/Gameplay/RangedGuard.prefab");
            library.AddPrefab("突进敌人", LevelAssetKind.Enemy, "Assets/Prefabs/Gameplay/MeleeRaider.prefab");
            library.AddPrefab("可搜索物资箱", LevelAssetKind.Container, "Assets/Prefabs/Inventory/SearchableContainer.prefab");
            AssetDatabase.CreateAsset(library, AssetPath);
            var floor = library.tiles.OfType<Tile>().FirstOrDefault(t => t.colliderType == Tile.ColliderType.None);
            if (floor != null)
            {
                var module = CreateInstance<LevelTileModule>();
                module.size = new Vector2Int(5, 5);
                module.sourceCellSize = new Vector3(1, 1, 0);
                for (int y = 0; y < 5; y++)
                    for (int x = 0; x < 5; x++)
                        module.cells.Add(new LevelTileModule.Cell { position = new Vector3Int(x, y), tile = floor,
                            color = Color.white, transform = Matrix4x4.identity, flags = floor.flags, collider = floor.colliderType });
                AssetDatabase.CreateAsset(module, Folder + "/开阔地5x5.asset");
                library.modules.Add(module);
                EditorUtility.SetDirty(library);
            }
            AssetDatabase.SaveAssets();
            return library;
        }

        private void AddPrefab(string label, LevelAssetKind kind, string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;
            Vector3 scale = prefab.transform.localScale;
            if (kind == LevelAssetKind.Container)
            {
                var renderer = prefab.GetComponent<SpriteRenderer>();
                if (renderer != null && renderer.sprite != null)
                    scale = Vector3.one * (1.1f / Mathf.Max(renderer.sprite.bounds.size.x, renderer.sprite.bounds.size.y));
            }
            prefabs.Add(new LevelPrefabEntry { label = label, kind = kind, prefab = prefab, scale = scale });
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int slash = folder.LastIndexOf('/');
            EnsureFolder(folder.Substring(0, slash));
            AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
        }
    }
}
