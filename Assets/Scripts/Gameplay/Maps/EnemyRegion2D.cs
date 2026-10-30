using System;
using System.Collections.Generic;
using UnityEngine;

namespace LootTheDeep.Gameplay.Maps
{
    [Serializable]
    public sealed class EnemyRegionEntry
    {
        public EnemyCombat2D prefab;
        [Min(1)] public int count = 1;
    }

    public sealed class EnemyRegion2D : MonoBehaviour
    {
        public string displayName = "敌人区域";
        public Vector2 size = new(6, 6);
        public List<EnemyRegionEntry> enemies = new();
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1, .4f, .2f, .8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0));
        }
    }
}
