using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace LootTheDeep.Gameplay.EditorTools
{
    public static class WorldCombatInstaller
    {
        public const string GuardPath = "Assets/Prefabs/Gameplay/RangedGuard.prefab";
        public const string RaiderPath = "Assets/Prefabs/Gameplay/MeleeRaider.prefab";

        [MenuItem("LootTheDeep/战斗/接入首版战斗 %#F8")]
        public static void Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/ExploreScene.unity" ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在 ExploreScene 编辑模式中接入战斗，等待编译完成。");

            var player = Object.FindFirstObjectByType<PlayerShooter2D>();
            var tilemap = Object.FindFirstObjectByType<Tilemap>();
            var square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/PrototypeSquare.png");
            var hero = AssetDatabase.LoadAssetAtPath<GameObject>(HeroAnimationInstaller.SourcePath);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HeroAnimationInstaller.ControllerPath);
            var shot = AssetDatabase.LoadAssetAtPath<ShootingProjectile2D>("Assets/Prefabs/Gameplay/SecondaryShot.prefab");
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            if (player == null || tilemap == null || square == null || hero == null || controller == null || shot == null || font == null)
                throw new InvalidOperationException("缺少角色、Tilemap 或首版战斗所需资源。");

            Physics2D.SyncTransforms();
            Vector2 north = FindSpawn(tilemap, player.transform.position, Vector2.up);
            Vector2 south = FindSpawn(tilemap, player.transform.position, Vector2.down);
            var playerHealth = player.GetComponent<CombatHealth2D>();
            if (playerHealth == null) playerHealth = Undo.AddComponent<CombatHealth2D>(player.gameObject);
            Undo.RecordObject(playerHealth, "接入角色生命值");
            playerHealth.Configure(CombatTeam.Player, 100, player.transform.Find("Visual/Body").GetComponent<SpriteRenderer>());
            PrefabUtility.RecordPrefabInstancePropertyModifications(playerHealth);

            Sprite actor = hero.GetComponent<SpriteRenderer>().sprite;
            GameObject guard = EnsureEnemyPrefab(GuardPath, EnemyCombat2D.AttackKind.Ranged, actor, controller, square, shot);
            GameObject raider = EnsureEnemyPrefab(RaiderPath, EnemyCombat2D.AttackKind.Lunge, actor, controller, square, null);
            Transform encounter = GameObject.Find("CombatEncounter")?.transform;
            if (encounter == null)
            {
                var root = new GameObject("CombatEncounter");
                Undo.RegisterCreatedObjectUndo(root, "接入首版战斗");
                encounter = root.transform;
            }
            PlaceEnemy(encounter, "RangedGuard", guard, north);
            PlaceEnemy(encounter, "MeleeRaider", raider, south);
            EnsureNavigation(tilemap);
            CombatPrototypeInstaller.EnsureSession(playerHealth, font);
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("CombatEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                Undo.RegisterCreatedObjectUndo(events, "接入战斗交互");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"WORLD_COMBAT_INSTALLED: dataPath={Application.dataPath}; product={Application.productName}; " +
                $"scene={scene.path}; guard={north}; raider={south}. 玩家100生命，北侧枪手，南侧突进敌人。");
        }

        [MenuItem("LootTheDeep/战斗/接入敌人寻路 %#F7")]
        public static void InstallNavigation()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/ExploreScene.unity" ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在 ExploreScene 编辑模式中接入寻路，等待编译完成。");
            EnsureNavigation(Object.FindFirstObjectByType<Tilemap>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"WORLD_NAVIGATION_INSTALLED: dataPath={Application.dataPath}; product={Application.productName}; scene={scene.path}");
        }

        private static void EnsureNavigation(Tilemap map)
        {
            if (Object.FindFirstObjectByType<TilemapPathfinder2D>() != null) return;
            if (map == null) throw new InvalidOperationException("当前场景缺少用于导航的 Tilemap。");
            var blockers = new System.Collections.Generic.List<Collider2D>();
            foreach (Collider2D collider in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
            {
                if (collider.isTrigger || collider.GetComponentInParent<CombatHealth2D>() != null ||
                    collider.GetComponentInParent<ShootingProjectile2D>() != null ||
                    collider.attachedRigidbody != null && collider.attachedRigidbody.bodyType != RigidbodyType2D.Static)
                    continue;
                if (collider is TilemapCollider2D && collider.GetComponent<CompositeCollider2D>() != null) continue;
                blockers.Add(collider);
            }
            var root = new GameObject("CombatNavigation");
            Undo.RegisterCreatedObjectUndo(root, "接入敌人寻路");
            root.AddComponent<TilemapPathfinder2D>().Configure(map, blockers.ToArray());
        }

        private static Vector2 FindSpawn(Tilemap map, Vector2 origin, Vector2 direction)
        {
            Vector2 sideways = new Vector2(-direction.y, direction.x);
            for (int forward = 12; forward <= 18; forward += 2)
                for (int lateral = 0; lateral <= 8; lateral += 2)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector2 candidate = origin + direction * forward + sideways * (lateral * sign);
                        if (!map.HasTile(map.WorldToCell(candidate))) continue;
                        bool blocked = false;
                        foreach (Collider2D hit in Physics2D.OverlapCircleAll(candidate, 1f))
                            if (!hit.isTrigger) { blocked = true; break; }
                        if (!blocked) return candidate;
                    }
            throw new InvalidOperationException("无法在出生点南北侧找到安全测试位置，请检查 Tilemap 通路。");
        }

        private static GameObject EnsureEnemyPrefab(string path, EnemyCombat2D.AttackKind kind, Sprite actor,
            RuntimeAnimatorController controller, Sprite square, ShootingProjectile2D projectile)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            bool ranged = kind == EnemyCombat2D.AttackKind.Ranged;
            var root = new GameObject(ranged ? "RangedGuard" : "MeleeRaider");
            try
            {
                var body = root.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                root.AddComponent<CircleCollider2D>().radius = .6f;
                var visual = new GameObject("Visual").transform;
                visual.SetParent(root.transform, false);
                var renderer = CreateSprite(visual, "Body", actor,
                    ranged ? new Color(1f, .55f, .45f) : new Color(1f, .75f, .35f), Vector2.zero, Vector2.one);
                var animation = renderer.gameObject.AddComponent<Animator>();
                animation.runtimeAnimatorController = controller;
                var warning = CreateSprite(root.transform, "AttackWarning", square,
                    new Color(1f, .7f, .2f, .7f), Vector2.zero, new Vector2(1f, .06f));
                warning.sortingOrder = 11;
                warning.gameObject.SetActive(false);
                var health = root.AddComponent<CombatHealth2D>();
                health.Configure(CombatTeam.Hostile, ranged ? 120 : 90, renderer);
                var enemy = root.AddComponent<EnemyCombat2D>();
                enemy.Configure(kind, projectile, warning, null, 10f, ranged ? 2.8f : 3.6f, ranged ? 7f : 3.4f, ranged ? 12 : 18);
                enemy.ConfigurePresentation(visual, animation, true);
                var track = CreateSprite(root.transform, "HealthBar", square, new Color(.12f, .08f, .1f),
                    new Vector2(0f, 1f), new Vector2(1.4f, .1f));
                var fill = CreateSprite(track.transform, "Fill", square, ranged ? new Color(1f, .35f, .3f) : new Color(1f, .65f, .2f),
                    Vector2.zero, Vector2.one);
                track.sortingOrder = 13;
                fill.sortingOrder = 14;
                track.gameObject.AddComponent<CombatHealthBar2D>().Configure(health, fill.transform);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static SpriteRenderer CreateSprite(Transform parent, string name, Sprite sprite, Color color,
            Vector2 position, Vector2 scale)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            child.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingLayerName = "World";
            renderer.sortingOrder = 12;
            return renderer;
        }

        private static void PlaceEnemy(Transform parent, string name, GameObject prefab, Vector2 position)
        {
            if (parent.Find(name) != null) return;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(instance, "放置测试敌人");
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
        }
    }
}
