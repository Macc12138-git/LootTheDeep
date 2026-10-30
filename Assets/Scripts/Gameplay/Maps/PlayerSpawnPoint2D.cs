using UnityEngine;

namespace LootTheDeep.Gameplay.Maps
{
    public sealed class PlayerSpawnPoint2D : MonoBehaviour
    {
        public string displayName = "玩家出生点";
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, .6f);
        }
    }
}
