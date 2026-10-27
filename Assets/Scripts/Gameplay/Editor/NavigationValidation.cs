using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace LootTheDeep.Gameplay.EditorTools
{
    [InitializeOnLoad]
    public static class NavigationValidation
    {
        private const string RunningKey = "LootTheDeep.NavigationValidation.Running";
        private static readonly Vector2 Origin = new(0f, -250f);
        private static readonly List<Vector2> route = new();
        private static readonly List<Collider2D> walls = new();
        private static Tilemap map;
        private static Tile tile;
        private static TilemapPathfinder2D navigation;
        private static EnemyCombat2D enemy;
        private static PlayerShooter2D player;
        private static int step, checks;
        private static float nextTime, maxDetour;
        private static float motionStarted, minimumVelocityX;
        private static double deadline;
        private static Vector2 home;

        static NavigationValidation() => EditorApplication.playModeStateChanged += OnPlayState;

        [MenuItem("LootTheDeep/战斗/验证敌人寻路（运行测试） %#F6")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty ||
                scene.path != "Assets/Scenes/ExploreScene.unity" ||
                Object.FindFirstObjectByType<TilemapPathfinder2D>() == null)
                throw new InvalidOperationException("请先在已保存的 ExploreScene 中接入敌人寻路，再运行检查。");
            Debug.Log($"WORLD_NAVIGATION_VALIDATION_START: dataPath={Application.dataPath}; product={Application.productName}; scene={scene.path}");
            SessionState.SetBool(RunningKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunningKey, false))
            {
                step = checks = 0;
                deadline = EditorApplication.timeSinceStartup + 120;
                nextTime = Time.time + .2f;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= Tick;
                SessionState.SetBool(RunningKey, false);
                if (tile != null) Object.DestroyImmediate(tile);
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new Exception($"WORLD_NAVIGATION_CHECK_FAILED: timeout at step {step}");
                EditorApplication.QueuePlayerLoopUpdate();
                if (step == 1)
                {
                    float elapsed = Time.time - motionStarted;
                    MovePlayer(new Vector2(4f + elapsed * .8f, 1f));
                    enemy.Hear(player.transform.position, 20f);
                    minimumVelocityX = Mathf.Min(minimumVelocityX, enemy.GetComponent<Rigidbody2D>().linearVelocity.x);
                }
                if (step == 2 || step == 4) maxDetour = Mathf.Max(maxDetour, Mathf.Abs(enemy.transform.position.y - Origin.y));
                if (Time.time < nextTime) return;
                if (step == 0)
                {
                    CheckRealMap();
                    BuildTestMap();
                    CheckStaticRoutes();
                    SetWalls(new[] { (new Vector2(0f, 0f), new Vector2(1f, 6f)) });
                    MovePlayer(new Vector2(4f, 1f));
                    Spawn(WorldCombatInstaller.GuardPath);
                    enemy.Hear(player.transform.position, 20f);
                    motionStarted = Time.time;
                    minimumVelocityX = 0f;
                    Next(1.4f);
                }
                else if (step == 1)
                {
                    Check(minimumVelocityX >= -.01f,
                        $"移动目标触发路径重算时，开阔路段不反向抽动 (minVelocityX={minimumVelocityX})");
                    Check(enemy.transform.position.x > home.x + .5f, "连续重算期间保持向前移动，不停在原地");
                    Object.Destroy(enemy.gameObject);
                    MovePlayer(new Vector2(4f, 0f));
                    Spawn(WorldCombatInstaller.GuardPath);
                    Next(7f);
                }
                else if (step == 2)
                {
                    Check(enemy.transform.position.x > .5f && maxDetour > 3.5f &&
                        Vector2.Distance(enemy.transform.position, Origin + new Vector2(4f, 0f)) < 5f,
                        $"枪手实际绕过墙端接近目标 (position={enemy.transform.position}, detour={maxDetour})");
                    MovePlayer(new Vector2(30f, 0f));
                    Next(15f);
                }
                else if (step == 3)
                {
                    Check(Vector2.Distance(enemy.transform.position, home) < .5f,
                        $"调查超时后沿可行走路线返回出生点 (position={enemy.transform.position})");
                    Object.Destroy(enemy.gameObject);
                    MovePlayer(new Vector2(4f, 0f));
                    Spawn(WorldCombatInstaller.RaiderPath);
                    Next(6f);
                }
                else if (step == 4)
                {
                    Check(enemy.transform.position.x > 1f && maxDetour > 3.5f,
                        $"突进敌人实际绕过墙端接近目标 (position={enemy.transform.position}, detour={maxDetour})");
                    Object.Destroy(enemy.gameObject);
                    SetWalls(new[] { (new Vector2(0f, -6.25f), new Vector2(1f, 11.5f)),
                        (new Vector2(0f, 6.25f), new Vector2(1f, 11.5f)) });
                    MovePlayer(new Vector2(4f, 0f));
                    Spawn(WorldCombatInstaller.GuardPath);
                    Next(1.5f);
                }
                else
                {
                    Check(Vector2.Distance(enemy.transform.position, home) < .1f &&
                        enemy.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero,
                        "路径不可达时停止移动，不持续向墙施加速度");
                    Debug.Log($"WORLD_NAVIGATION_PLAY_CHECKS_PASSED: {checks} checks; real Tilemap, L/U detours, body clearance, stable moving-target repaths, unreachable goals, ranged/melee movement and return home.");
                    SessionState.SetBool(RunningKey, false);
                    EditorApplication.update -= Tick;
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                SessionState.SetBool(RunningKey, false);
                EditorApplication.update -= Tick;
                EditorApplication.isPlaying = false;
            }
        }

        private static void CheckRealMap()
        {
            var actual = Object.FindFirstObjectByType<TilemapPathfinder2D>();
            var guard = GameObject.Find("CombatEncounter/RangedGuard");
            var raider = GameObject.Find("CombatEncounter/MeleeRaider");
            player = Object.FindFirstObjectByType<PlayerShooter2D>();
            Check(actual.TryFindPath(guard.transform.position, player.transform.position, route), "当前地图枪手出生点到玩家出生点存在通路");
            CheckRoute(actual, guard.transform.position);
            Check(actual.TryFindPath(raider.transform.position, player.transform.position, route), "当前地图突进敌人出生点到玩家出生点存在通路");
            CheckRoute(actual, raider.transform.position);
            GameObject.Find("CombatEncounter").SetActive(false);
            player.enabled = false;
        }

        private static void BuildTestMap()
        {
            var root = new GameObject("NavigationTestGrid", typeof(Grid));
            root.transform.position = Origin;
            var floor = new GameObject("NavigationTestFloor", typeof(Tilemap));
            floor.transform.SetParent(root.transform, false);
            map = floor.GetComponent<Tilemap>();
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.colliderType = Tile.ColliderType.None;
            for (int y = -12; y < 12; y++)
                for (int x = -12; x < 12; x++) map.SetTile(new Vector3Int(x, y, 0), tile);
            navigation = root.AddComponent<TilemapPathfinder2D>();
        }

        private static void CheckStaticRoutes()
        {
            SetWalls(new[] { (new Vector2(0f, 0f), new Vector2(1f, 6f)),
                (new Vector2(2f, 2.5f), new Vector2(5f, 1f)) });
            Vector2 start = Origin + new Vector2(-4f, 0f), goal = Origin + new Vector2(4f, 0f);
            Check(!navigation.CanTraverse(start, goal) && navigation.TryFindPath(start, goal, route), "L形墙阻断直线时找到绕行路径");
            CheckRoute(navigation, start);
            Check(route[0].x > start.x,
                $"绕墙开始时向前推进，不先退回身后的网格中心 (start={start}, first={route[0]})");

            SetWalls(new[] { (new Vector2(-2f, 0f), new Vector2(1f, 6f)),
                (new Vector2(2f, 0f), new Vector2(1f, 6f)), (new Vector2(0f, -3f), new Vector2(5f, 1f)) });
            start = Origin;
            goal = Origin + new Vector2(0f, -6f);
            Check(navigation.TryFindPath(start, goal, route) && route.Exists(p => p.y > Origin.y + 3.5f),
                "U形障碍内先向出口移动，再绕到目标一侧");
            CheckRoute(navigation, start);

            SetWalls(new[] { (new Vector2(-.5f, .5f), Vector2.one), (new Vector2(.5f, -.5f), Vector2.one) });
            start = Origin + new Vector2(-1.5f, -1.5f);
            goal = Origin + new Vector2(1.5f, 1.5f);
            Check(!navigation.CanTraverse(start, goal) && navigation.TryFindPath(start, goal, route), "对角相接墙角不能斜穿，仍能从外侧绕行");
            CheckRoute(navigation, start);

            SetWalls(new[] { (new Vector2(0f, -6.25f), new Vector2(1f, 11.5f)),
                (new Vector2(0f, 6.25f), new Vector2(1f, 11.5f)) });
            start = Origin + new Vector2(-4f, 0f);
            goal = Origin + new Vector2(4f, 0f);
            Check(!navigation.TryFindPath(start, goal, route) && route.Count == 0, "小于身体直径的窄道判为不可达，不从地图外绕行");
            SetWalls(new[] { (new Vector2(0f, -6.5f), new Vector2(1f, 11f)),
                (new Vector2(0f, 6.5f), new Vector2(1f, 11f)) });
            Check(navigation.TryFindPath(start, goal, route), "足够宽的通道允许身体通过");

            SetWalls(new[] { (new Vector2(2f, 0f), new Vector2(1f, 5f)),
                (new Vector2(6f, 0f), new Vector2(1f, 5f)),
                (new Vector2(4f, 2f), new Vector2(5f, 1f)),
                (new Vector2(4f, -2f), new Vector2(5f, 1f)) });
            Check(!navigation.TryFindPath(start, goal, route), "目标位于封闭区域时不会跨墙吸附到外部节点");
            Check(!navigation.TryFindPath(start, Origin + new Vector2(2f, 0f), route), "墙体内部的目标判为不可达");
            CheckCompositeWallExit();
        }

        private static void CheckCompositeWallExit()
        {
            SetWalls(Array.Empty<(Vector2, Vector2)>());
            var root = new GameObject("NavigationTestComposite", typeof(Rigidbody2D));
            root.transform.position = Origin;
            root.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            var composite = root.AddComponent<CompositeCollider2D>();
            composite.generationType = CompositeCollider2D.GenerationType.Manual;
            for (int i = 0; i < 2; i++)
            {
                var wall = root.AddComponent<BoxCollider2D>();
                wall.offset = new Vector2(i * 3f, 0f);
                wall.size = new Vector2(1f, 6f);
                wall.compositeOperation = Collider2D.CompositeOperation.Merge;
            }
            Physics2D.SyncTransforms();
            composite.GenerateGeometry();
            walls.Add(composite);
            navigation.Configure(map, walls.ToArray());
            Vector2 start = composite.ClosestPoint(Origin + new Vector2(1.1f, 0f)) +
                Vector2.right * (navigation.AgentRadius + .005f);
            Check(navigation.CanTraverse(start, Origin + new Vector2(1.8f, 0f)),
                $"贴墙身体能够向空地离开导航安全间隙 (clearance={Vector2.Distance(start, composite.ClosestPoint(start))}, paths={composite.pathCount})");
            Check(!navigation.CanTraverse(start, Origin + new Vector2(5f, 0f)) &&
                navigation.TryFindPath(start, Origin + new Vector2(5f, 0f), route),
                "离开贴墙位置后仍阻挡同一 CompositeCollider2D 的后续墙段");
            CheckRoute(navigation, start);
        }

        private static void CheckRoute(TilemapPathfinder2D finder, Vector2 start)
        {
            bool clear = true;
            foreach (Vector2 point in route)
            {
                clear &= finder.CanTraverse(start, point);
                start = point;
            }
            Check(clear, "整条路线及平滑后的转角满足身体通行空间");
        }

        private static void SetWalls((Vector2 position, Vector2 size)[] definitions)
        {
            foreach (Collider2D wall in walls)
            {
                wall.enabled = false;
                Object.Destroy(wall.gameObject);
            }
            walls.Clear();
            foreach (var definition in definitions)
            {
                var root = new GameObject("NavigationTestWall");
                root.transform.position = Origin + definition.position;
                var collider = root.AddComponent<BoxCollider2D>();
                collider.size = definition.size;
                walls.Add(collider);
            }
            navigation.Configure(map, walls.ToArray());
        }

        private static void Spawn(string prefabPath)
        {
            home = Origin + new Vector2(-4f, 0f);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), home, Quaternion.identity);
            enemy = root.GetComponent<EnemyCombat2D>();
            enemy.ConfigureNavigation(navigation);
            enemy.Hear(Origin + new Vector2(4f, 0f), 20f);
            maxDetour = 0f;
        }

        private static void MovePlayer(Vector2 localPosition)
        {
            Vector2 position = Origin + localPosition;
            player.GetComponent<Rigidbody2D>().position = position;
            player.transform.position = position;
            Physics2D.SyncTransforms();
        }

        private static void Next(float seconds)
        {
            step++;
            nextTime = Time.time + seconds;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("WORLD_NAVIGATION_CHECK_FAILED: " + message);
            checks++;
        }
    }
}
