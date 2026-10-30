using System;
using System.Linq;
using LootTheDeep.Inventory;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LootTheDeep.Gameplay.Maps
{
    [DefaultExecutionOrder(-300)]
    public sealed class MapPlaytestController : MonoBehaviour
    {
        public MapRoot mapPrefab;
        public GameObject gameplayRig;
        public int seed = 12345;
        public int spawnIndex = -1;
        public bool showAllCandidates;
        public MapRoot LoadedMap { get; private set; }
        public PlayerSpawnPoint2D SelectedSpawn { get; private set; }
        public int ActiveExitCount { get; private set; }

        private void Awake()
        {
            if (mapPrefab == null || gameplayRig == null)
            { Debug.LogError("MAP_LOAD_FAILED: 请从关卡编辑器保存并试玩，或为加载器指定 Map 与试玩环境。", this); return; }
            LoadedMap = Instantiate(mapPrefab);
            LoadedMap.name = mapPrefab.name;
            LoadedMap.RebuildNavigation();
            var spawns = LoadedMap.Spawns;
            var random = new System.Random(seed);
            if (spawns.Length == 0) { Debug.LogError("MAP_LOAD_FAILED: 缺少出生点。", this); return; }
            SelectedSpawn = spawns[spawnIndex >= 0 ? Mathf.Clamp(spawnIndex, 0, spawns.Length - 1) : random.Next(spawns.Length)];
            var exits = MapSpawnPlanner.ReachableExits(LoadedMap, SelectedSpawn.transform.position);
            if (exits.Count < LoadedMap.activeExtractionCount || !LoadedMap.navigation.CanTraverse(SelectedSpawn.transform.position, SelectedSpawn.transform.position))
            { Debug.LogError("MAP_LOAD_FAILED: 出生点无效或可达撤离点数量不足。", this); return; }
            MapSpawnPlanner.Shuffle(exits, random);
            foreach (var exit in LoadedMap.All<ExtractionPoint2D>())
            {
                bool selected = exits.Take(LoadedMap.activeExtractionCount).Contains(exit);
                exit.gameObject.SetActive(selected);
                if (selected) ActiveExitCount++;
            }
            if (!MapSpawnPlanner.PlanEnemies(LoadedMap, SelectedSpawn.transform.position, seed, out var planned, out string error))
            { Debug.LogError("MAP_LOAD_FAILED: " + error, this); return; }

            var rig = Instantiate(gameplayRig);
            var player = rig.GetComponentInChildren<UnderwaterMover2D>(true);
            player.transform.position = SelectedSpawn.transform.position;
            player.GetComponent<Rigidbody2D>().position = player.transform.position;
            player.ConfigureBounds(LoadedMap.WorldBounds);
            player.ConfigureGround(LoadedMap.ground);
            foreach (var camera in rig.GetComponentsInChildren<CameraFollow2D>(true))
            { camera.ConfigureBounds(LoadedMap.WorldBounds); camera.Configure(player.transform); }
            foreach (var p in planned)
            {
                // Instantiate at final position: EnemyCombat2D.Awake records its home here.
                var enemy = Instantiate(p.prefab, p.position, Quaternion.identity);
                enemy.ConfigureNavigation(LoadedMap.navigation);
            }
            Physics2D.SyncTransforms();
            var mapView = rig.GetComponentInChildren<MapView2D>(true);
            if (mapView != null)
            {
                mapView.Initialize(LoadedMap);
                rig.GetComponentInChildren<CombatPrototypeSession2D>(true).ConfigureMap(mapView);
            }
            Debug.Log($"MAP_LOADED: {LoadedMap.mapId}; seed={seed}; spawn={SelectedSpawn.name}; exits={ActiveExitCount}; enemies={planned.Count}; containers={LoadedMap.All<SearchableContainer>().Length}");
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !showAllCandidates || LoadedMap == null) return;
            Gizmos.color = Color.yellow;
            foreach (var exit in LoadedMap.All<ExtractionPoint2D>()) Gizmos.DrawWireSphere(exit.transform.position, exit.InteractDistance);
        }

        public void Restart()
        {
            Time.timeScale = 1;
            SceneManager.LoadScene(gameObject.scene.name);
        }
    }
}
