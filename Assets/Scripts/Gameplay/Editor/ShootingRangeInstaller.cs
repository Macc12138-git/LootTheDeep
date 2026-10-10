using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LootTheDeep.Gameplay.EditorTools
{
    public static class ShootingRangeInstaller
    {
        private const string ScenePath = "Assets/Scenes/ExploreScene.unity";
        private const string PlayerPath = "Assets/Prefabs/Gameplay/Player.prefab";
        private const string MainShotPath = "Assets/Prefabs/Gameplay/MainShot.prefab";
        private const string SecondaryShotPath = "Assets/Prefabs/Gameplay/SecondaryShot.prefab";
        private const string TargetPath = "Assets/Prefabs/Gameplay/TargetDummy.prefab";
        private const string SquarePath = "Assets/Sprites/PrototypeSquare.png";
        private const string FontPath = "Assets/Fonts/NotoSansCJKsc-Regular.otf";

        [MenuItem("LootTheDeep/Install Shooting Range")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Exit Play Mode and wait for compilation before installing the shooting range.");

            Scene scene = Application.isBatchMode
                ? EditorSceneManager.OpenScene(ScenePath)
                : SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                throw new InvalidOperationException("Open ExploreScene before installing the shooting range.");

            Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (square == null || font == null) throw new InvalidOperationException("Prototype sprite or HUD font is missing.");

            ShootingProjectile2D mainShot = EnsureProjectile(MainShotPath, square, new Color(1f, 0.79f, 0.36f), 0.22f);
            ShootingProjectile2D secondaryShot = EnsureProjectile(SecondaryShotPath, square, new Color(0.37f, 0.89f, 0.91f), 0.13f);
            TargetDummy2D target = EnsureTarget(square);
            EnsurePlayer(square, mainShot, secondaryShot);

            GameObject gameplay = GameObject.Find("Gameplay");
            if (gameplay == null) throw new InvalidOperationException("ExploreScene Gameplay root is missing.");
            Transform player = gameplay.transform.Find("Player");
            if (player == null || player.GetComponent<PlayerShooter2D>() == null)
                throw new InvalidOperationException("ExploreScene Player prefab instance did not import the shooting component.");

            EnsureHud(font);
            EnsureTargets(gameplay.transform, player.position, target);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Validate();
            Debug.Log("Shooting range installed: mouse aim, 1/2 weapons, three static targets, crosshair and test ammo.");
        }

        [MenuItem("LootTheDeep/Validate Shooting Range")]
        public static void Validate()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            if (actions?.FindAction("Player/AimPoint") == null || actions.FindAction("Player/Attack") == null)
                throw new InvalidOperationException("Player/AimPoint or Player/Attack input is missing.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath)?.GetComponent<PlayerShooter2D>() == null)
                throw new InvalidOperationException("Player prefab is missing its shooting component.");
            if (SceneManager.GetActiveScene().path != ScenePath) return;
            if (Object.FindFirstObjectByType<ShootingHud2D>() == null ||
                Object.FindObjectsByType<TargetDummy2D>(FindObjectsSortMode.None).Length < 3)
                throw new InvalidOperationException("Shooting HUD or three target dummies are missing from ExploreScene.");
            Debug.Log("Shooting range assets and scene references validated.");
        }

        private static ShootingProjectile2D EnsureProjectile(string path, Sprite sprite, Color tint, float size)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) return prefab.GetComponent<ShootingProjectile2D>();

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                root.transform.localScale = new Vector3(size, size, 1f);
                var renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = tint;
                renderer.sortingLayerName = "World";
                renderer.sortingOrder = 5;
                var body = root.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                var hitbox = root.AddComponent<CircleCollider2D>();
                hitbox.isTrigger = true;
                hitbox.radius = 0.48f;
                root.AddComponent<ShootingProjectile2D>();
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
            return prefab.GetComponent<ShootingProjectile2D>();
        }

        private static TargetDummy2D EnsureTarget(Sprite sprite)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath);
            if (prefab != null) return prefab.GetComponent<TargetDummy2D>();

            var root = new GameObject("TargetDummy");
            try
            {
                var hitbox = root.AddComponent<BoxCollider2D>();
                hitbox.size = new Vector2(0.82f, 0.82f);
                var face = SpriteChild(root.transform, "Face", sprite, new Color(0.9f, 0.58f, 0.24f),
                    Vector3.zero, new Vector3(0.82f, 0.82f, 1f), 3);
                SpriteChild(root.transform, "Bullseye", sprite, new Color(0.08f, 0.2f, 0.24f),
                    Vector3.zero, new Vector3(0.26f, 0.26f, 1f), 4);
                SpriteChild(root.transform, "HealthTrack", sprite, new Color(0.08f, 0.2f, 0.24f),
                    new Vector3(0f, 0.63f, 0f), new Vector3(0.96f, 0.14f, 1f), 3);
                var fill = SpriteChild(root.transform, "HealthFill", sprite, new Color(0.31f, 0.91f, 0.6f),
                    new Vector3(0f, 0.63f, 0f), new Vector3(0.9f, 0.08f, 1f), 4);
                root.AddComponent<TargetDummy2D>().Configure(30, face, fill, face.color);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, TargetPath);
            }
            finally { Object.DestroyImmediate(root); }
            return prefab.GetComponent<TargetDummy2D>();
        }

        private static void EnsurePlayer(Sprite sprite, ShootingProjectile2D mainShot, ShootingProjectile2D secondaryShot)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                PlayerShooter2D shooter = contents.GetComponent<PlayerShooter2D>();
                if (shooter != null)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        shooter.SelectWeapon(i);
                        var weapon = shooter.ActiveWeapon;
                        weapon.muzzleFlash = EnsureFlash(weapon.muzzle, sprite);
                    }
                    shooter.SelectWeapon(0);
                    PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath);
                    return;
                }
                Transform combatRoot = Child(contents.transform, "CombatRoot");
                Transform pivot = Child(combatRoot, "WeaponPivot");
                pivot.localPosition = new Vector3(0f, 0.24f, 0f);

                PlayerShooter2D.WeaponSlot main = MakeWeapon(pivot, "MainWeapon", sprite,
                    new Color(1f, 0.79f, 0.36f), new Vector3(0.38f, 0f, 0f),
                    new Vector3(0.68f, 0.17f, 1f), 0.76f, mainShot, 30, 0.7f, 10f, 6);
                PlayerShooter2D.WeaponSlot secondary = MakeWeapon(pivot, "SecondaryWeapon", sprite,
                    new Color(0.37f, 0.89f, 0.91f), new Vector3(0.31f, 0f, 0f),
                    new Vector3(0.5f, 0.11f, 1f), 0.63f, secondaryShot, 8, 0.15f, 17f, 24);

                contents.AddComponent<PlayerShooter2D>().Configure(pivot, main, secondary);
                secondary.visual.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static PlayerShooter2D.WeaponSlot MakeWeapon(Transform pivot, string name, Sprite sprite,
            Color tint, Vector3 position, Vector3 scale, float muzzleX, ShootingProjectile2D shot,
            int damage, float interval, float speed, int ammo)
        {
            Transform visual = Child(pivot, name);
            SpriteRenderer renderer = visual.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = tint;
            renderer.sortingLayerName = "World";
            renderer.sortingOrder = 3;
            visual.localPosition = position;
            visual.localScale = scale;
            // Muzzle inherits visual scale: convert its desired pivot-space X back to local space.
            Transform muzzle = Child(visual, "Muzzle");
            muzzle.localPosition = new Vector3((muzzleX - position.x) / scale.x, 0f, 0f);
            return new PlayerShooter2D.WeaponSlot
            {
                displayName = name == "MainWeapon" ? "MAIN WEAPON" : "SECONDARY WEAPON",
                visual = visual.gameObject,
                muzzle = muzzle,
                muzzleFlash = EnsureFlash(muzzle, sprite),
                projectilePrefab = shot,
                damage = damage,
                fireInterval = interval,
                projectileSpeed = speed,
                initialAmmo = ammo
            };
        }

        private static SpriteRenderer EnsureFlash(Transform muzzle, Sprite sprite)
        {
            Transform flash = muzzle.Find("MuzzleFlash");
            if (flash == null) flash = Child(muzzle, "MuzzleFlash");
            flash.localScale = new Vector3(0.17f / muzzle.lossyScale.x, 0.17f / muzzle.lossyScale.y, 1f);
            SpriteRenderer renderer = flash.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = flash.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(1f, 0.98f, 0.74f);
            renderer.sortingLayerName = "World";
            renderer.sortingOrder = 6;
            flash.gameObject.SetActive(false);
            return renderer;
        }

        private static void EnsureTargets(Transform gameplay, Vector3 playerPosition, TargetDummy2D prefab)
        {
            Transform group = gameplay.Find("ShootingTargets");
            if (group == null) group = Child(gameplay, "ShootingTargets");
            var offsets = new[] { new Vector2(3f, 0.2f), new Vector2(6f, 1.5f), new Vector2(10f, -0.6f) };
            var names = new[] { "Target_Close", "Target_Middle", "Target_Far" };
            var health = new[] { 30, 60, 100 };
            for (int i = 0; i < names.Length; i++)
            {
                if (group.Find(names[i]) != null) continue;
                var target = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, gameplay.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(target, "Install Shooting Target");
                target.name = names[i];
                target.transform.SetParent(group, true);
                target.transform.position = playerPosition + (Vector3)offsets[i];
                var dummy = target.GetComponent<TargetDummy2D>();
                var face = target.transform.Find("Face").GetComponent<SpriteRenderer>();
                var fill = target.transform.Find("HealthFill").GetComponent<SpriteRenderer>();
                dummy.Configure(health[i], face, fill, face.color);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(dummy);
            }
        }

        private static void EnsureHud(Font font)
        {
            if (Object.FindFirstObjectByType<ShootingHud2D>() != null) return;
            var root = new GameObject("ShootingHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(root, "Install Shooting HUD");
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform marker = UiChild(root.transform, "Crosshair");
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(30f, 30f);
            var parts = new Graphic[5];
            parts[0] = Bar(marker, "Center", Vector2.zero, new Vector2(3f, 3f));
            parts[1] = Bar(marker, "Left", new Vector2(-9f, 0f), new Vector2(6f, 2f));
            parts[2] = Bar(marker, "Right", new Vector2(9f, 0f), new Vector2(6f, 2f));
            parts[3] = Bar(marker, "Top", new Vector2(0f, 9f), new Vector2(2f, 6f));
            parts[4] = Bar(marker, "Bottom", new Vector2(0f, -9f), new Vector2(2f, 6f));

            Text label = HudLabel(root.transform, "CurrentWeapon", font, new Vector2(-26f, -24f));
            Text ammo = HudLabel(root.transform, "TestAmmo", font, new Vector2(-26f, -54f));
            root.AddComponent<ShootingHud2D>().Configure(marker, parts, label, ammo);
        }

        private static Image Bar(Transform parent, string name, Vector2 position, Vector2 size)
        {
            RectTransform rect = UiChild(parent, name);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.color = new Color(0.92f, 1f, 0.88f);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.02f, 0.1f, 0.13f, 0.95f);
            outline.effectDistance = new Vector2(1f, -1f);
            return image;
        }

        private static Text HudLabel(Transform parent, string name, Font font, Vector2 position)
        {
            RectTransform rect = UiChild(parent, name);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(270f, 27f);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = 19;
            label.alignment = TextAnchor.MiddleRight;
            label.color = new Color(0.92f, 1f, 0.88f);
            label.raycastTarget = false;
            return label;
        }

        private static SpriteRenderer SpriteChild(Transform parent, string name, Sprite sprite, Color tint,
            Vector3 position, Vector3 scale, int order)
        {
            Transform child = Child(parent, name);
            child.localPosition = position;
            child.localScale = scale;
            var renderer = child.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = tint;
            renderer.sortingLayerName = "World";
            renderer.sortingOrder = order;
            return renderer;
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static RectTransform UiChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return (RectTransform)child.transform;
        }
    }
}
