using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace LootTheDeep.LevelBuilder
{
    // Editor-only terrain resource. Layouts themselves stay in the scene.
    public sealed class LevelTileModule : ScriptableObject
    {
        [Serializable]
        public struct Cell
        {
            public Vector3Int position;
            public TileBase tile;
            public Color color;
            public Matrix4x4 transform;
            public TileFlags flags;
            public Tile.ColliderType collider;
        }

        public Vector2Int size;
        public Vector3 sourceCellSize = Vector3.one;
        public List<Cell> cells = new();

        public Vector2Int RotatedSize(int turns) => turns % 2 == 0 ? size : new Vector2Int(size.y, size.x);

        public Vector3Int RotateCell(Vector3Int p, int turns)
        {
            switch ((turns % 4 + 4) % 4)
            {
                case 1: return new Vector3Int(size.y - 1 - p.y, p.x, 0);
                case 2: return new Vector3Int(size.x - 1 - p.x, size.y - 1 - p.y, 0);
                case 3: return new Vector3Int(p.y, size.x - 1 - p.x, 0);
                default: return p;
            }
        }

        public static LevelTileModule Capture(Tilemap map, BoundsInt bounds)
        {
            var module = CreateInstance<LevelTileModule>();
            module.size = new Vector2Int(bounds.size.x, bounds.size.y);
            module.sourceCellSize = map.layoutGrid.cellSize;
            foreach (Vector3Int p in bounds.allPositionsWithin)
                module.cells.Add(new Cell
                {
                    position = p - bounds.min,
                    tile = map.GetTile(p), color = map.GetColor(p), transform = map.GetTransformMatrix(p),
                    flags = map.GetTileFlags(p), collider = map.GetColliderType(p)
                });
            return module;
        }
    }
}
