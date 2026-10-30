using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace LootTheDeep.Gameplay.Maps
{
    public static class MapTextureBuilder
    {
        public static readonly Color32 EmptyColor = new(20, 27, 31, 255);
        public static readonly Color32 GroundColor = new(157, 172, 164, 255);
        public static readonly Color32 ObstacleColor = new(52, 65, 65, 255);

        public static Texture2D Build(MapRoot map, out Rect bounds)
        {
            bounds = map.WorldBounds;
            Vector3 origin = map.ground.CellToWorld(Vector3Int.zero);
            float cellSize = Mathf.Min((map.ground.CellToWorld(Vector3Int.right) - origin).magnitude,
                (map.ground.CellToWorld(Vector3Int.up) - origin).magnitude);
            float density = Mathf.Min(8f / cellSize, 1024f / Mathf.Max(bounds.width, bounds.height));
            int width = Mathf.Max(1, Mathf.CeilToInt(bounds.width * density));
            int height = Mathf.Max(1, Mathf.CeilToInt(bounds.height * density));
            // Only authored terrain and obstacles participate. Never capture gameplay renderers.
            var blockers = map.All<Collider2D>().Where(c =>
                (c.transform.IsChildOf(map.terrain) || c.transform.IsChildOf(map.obstacles)) &&
                c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.shapeCount > 0 &&
                (c.attachedRigidbody == null || c.attachedRigidbody.bodyType == RigidbodyType2D.Static) &&
                !(c is TilemapCollider2D && c.GetComponent<CompositeCollider2D>() != null))
                .Select(c => new ObstacleShape(c)).ToArray();
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Vector2 point = bounds.min + new Vector2((x + .5f) / width * bounds.width,
                        (y + .5f) / height * bounds.height);
                    Color32 color = map.ground.HasTile(map.ground.WorldToCell(point)) ? GroundColor : EmptyColor;
                    foreach (var blocker in blockers)
                        if (blocker.Contains(point))
                        { color = ObstacleColor; break; }
                    pixels[y * width + x] = color;
                }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = map.mapId + "_Map", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private sealed class ObstacleShape
        {
            private readonly Collider2D collider;
            private readonly Bounds bounds;
            private readonly Vector2[][] outlines;

            public ObstacleShape(Collider2D value)
            {
                collider = value;
                bounds = value.bounds;
                if (value is not CompositeCollider2D composite ||
                    composite.geometryType != CompositeCollider2D.GeometryType.Outlines) return;
                // Composite outlines are hollow physics edges. Fill their closed contours for the map,
                // using even/odd crossings across all paths so courtyards and other holes stay empty.
                outlines = new Vector2[composite.pathCount][];
                for (int path = 0; path < outlines.Length; path++)
                {
                    var points = new Vector2[composite.GetPathPointCount(path)];
                    composite.GetPath(path, points);
                    for (int i = 0; i < points.Length; i++)
                        points[i] = composite.transform.TransformPoint(points[i] + composite.offset);
                    outlines[path] = points;
                }
            }

            public bool Contains(Vector2 point)
            {
                if (!bounds.Contains(new Vector3(point.x, point.y, bounds.center.z))) return false;
                if (outlines == null) return collider.OverlapPoint(point);
                bool inside = false;
                foreach (var path in outlines)
                    for (int i = 0, j = path.Length - 1; i < path.Length; j = i++)
                    {
                        Vector2 a = path[i], b = path[j];
                        if ((a.y > point.y) != (b.y > point.y) &&
                            point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                            inside = !inside;
                    }
                return inside;
            }
        }
    }
}
