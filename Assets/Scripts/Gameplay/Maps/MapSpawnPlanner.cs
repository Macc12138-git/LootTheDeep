using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LootTheDeep.Gameplay.Maps
{
    // Shared by editor validation/preview and play. Does not instantiate or initialize loot.
    public static class MapSpawnPlanner
    {
        public readonly struct EnemyPlacement
        {
            public readonly EnemyCombat2D prefab;
            public readonly Vector2 position;
            public EnemyPlacement(EnemyCombat2D prefab, Vector2 position) { this.prefab = prefab; this.position = position; }
        }

        public static List<ExtractionPoint2D> ReachableExits(MapRoot map, Vector2 spawn)
        {
            var path = new List<Vector2>();
            return map.Exits.Where(e => map.navigation.CanTraverse(e.transform.position, e.transform.position) &&
                map.navigation.TryFindPath(spawn, e.transform.position, path)).ToList();
        }

        public static void Shuffle<T>(IList<T> values, System.Random random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            { int j = random.Next(i + 1); (values[i], values[j]) = (values[j], values[i]); }
        }

        public static bool PlanEnemies(MapRoot map, Vector2 spawn, int seed,
            out List<EnemyPlacement> placements, out string error)
        {
            placements = new List<EnemyPlacement>();
            var random = new System.Random(seed);
            float spacing = Mathf.Max(1.3f, map.navigation.AgentRadius * 2 + .1f);
            foreach (var region in map.All<EnemyRegion2D>().Where(r => r.isActiveAndEnabled))
            {
                if (region.size.x <= 0 || region.size.y <= 0 || region.size.x * region.size.y > 65536)
                { error = region.displayName + "：区域尺寸必须为正，面积不超过 65536。"; return false; }
                var candidates = new List<Vector2>();
                for (float y = -region.size.y / 2 + .65f; y <= region.size.y / 2 - .65f; y += .5f)
                    for (float x = -region.size.x / 2 + .65f; x <= region.size.x / 2 - .65f; x += .5f)
                    {
                        Vector2 p = region.transform.TransformPoint(new Vector3(x, y));
                        if (map.navigation.CanTraverse(p, p)) candidates.Add(p);
                    }
                Shuffle(candidates, random);
                foreach (var entry in region.enemies)
                {
                    if (entry.prefab == null || entry.count < 1)
                    { error = region.displayName + "：缺少敌人预制体或数量无效。"; return false; }
                    for (int n = 0; n < entry.count; n++)
                    {
                        bool found = false;
                        foreach (var p in candidates)
                        {
                            if (Vector2.Distance(p, spawn) < 3f || placements.Any(v => Vector2.Distance(p, v.position) < spacing)) continue;
                            placements.Add(new EnemyPlacement(entry.prefab, p)); found = true; break;
                        }
                        if (!found) { error = region.displayName + "：可通行空间不足，无法放下指定敌人（含出生点安全距离）。"; return false; }
                    }
                }
            }
            error = null; return true;
        }
    }
}
