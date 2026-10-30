using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace LootTheDeep.Gameplay.Maps
{
    [DisallowMultipleComponent]
    public sealed class MapRoot : MonoBehaviour
    {
        public string mapId = "Map_001";
        public string displayName = "新地图";
        [Min(1)] public int activeExtractionCount = 1;
        public Tilemap ground;
        public TilemapPathfinder2D navigation;
        public Transform terrain, obstacles, containers, playerSpawnPoints, extractionPoints, enemyRegions;

        private Vector3Int[] tilePositions = System.Array.Empty<Vector3Int>();
        private TileBase[] tiles = System.Array.Empty<TileBase>();

        // cellBounds can retain erased cells. Read occupied cells without changing the Tilemap
        // or including editor-preview tiles, so painting and Undo need no separate bounds state.
        public BoundsInt Bounds
        {
            get
            {
                if (ground == null) return default;
                var area = ground.cellBounds;
                if (area.size.x <= 0 || area.size.y <= 0 || area.size.z <= 0) return default;
                var last = area.max - Vector3Int.one;
                int count = ground.GetTilesRangeCount(area.min, last);
                if (count == 0) return default;
                if (tilePositions.Length < count)
                {
                    tilePositions = new Vector3Int[count];
                    tiles = new TileBase[count];
                }
                count = ground.GetTilesRangeNonAlloc(area.min, last, tilePositions, tiles);
                Vector3Int min = tilePositions[0], max = min;
                for (int i = 1; i < count; i++)
                {
                    min = Vector3Int.Min(min, tilePositions[i]);
                    max = Vector3Int.Max(max, tilePositions[i]);
                }
                return new BoundsInt(min, max - min + Vector3Int.one);
            }
        }

        public T[] All<T>() where T : Component => GetComponentsInChildren<T>(true);
        public PlayerSpawnPoint2D[] Spawns => All<PlayerSpawnPoint2D>().Where(p => p.isActiveAndEnabled).ToArray();
        public ExtractionPoint2D[] Exits => All<ExtractionPoint2D>().Where(p => p.isActiveAndEnabled).ToArray();

        public void RebuildNavigation()
        {
            foreach (var tileCollider in All<TilemapCollider2D>())
                if (tileCollider.hasTilemapChanges) tileCollider.ProcessTilemapChanges();
            Physics2D.SyncTransforms();
            foreach (var composite in All<CompositeCollider2D>())
                if (composite.generationType == CompositeCollider2D.GenerationType.Manual) composite.GenerateGeometry();
            Physics2D.SyncTransforms();
            var blockers = All<Collider2D>().Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger &&
                c.shapeCount > 0 && c.GetComponentInParent<CombatHealth2D>() == null &&
                (c.attachedRigidbody == null || c.attachedRigidbody.bodyType == RigidbodyType2D.Static) &&
                !(c is TilemapCollider2D && c.GetComponent<CompositeCollider2D>() != null)).ToArray();
            navigation.Configure(ground, blockers);
        }

        public Rect WorldBounds
        {
            get
            {
                var bounds = Bounds;
                Vector3 a = ground.CellToWorld(bounds.min), b = ground.CellToWorld(bounds.max);
                return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            }
        }
    }
}
