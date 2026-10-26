using System;
using LootTheDeep.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LootTheDeep.Gameplay.EditorTools
{
    public static class CombatPrototypeInstaller
    {
        private const string ScenePath = "Assets/Scenes/ExploreScene.unity";
        private const string PlayerPath = "Assets/Prefabs/Gameplay/Player.prefab";

        [MenuItem("LootTheDeep/Install Combat Prototype")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Exit Play Mode and wait for compilation first.");
            Scene scene = Application.isBatchMode ? EditorSceneManager.OpenScene(ScenePath) : SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open ExploreScene first.");
            Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/PrototypeSquare.png");
            Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            var shot = AssetDatabase.LoadAssetAtPath<ShootingProjectile2D>("Assets/Prefabs/Gameplay/SecondaryShot.prefab");
            var corpse = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Inventory/SearchableContainer.prefab");
            var scrap = AssetDatabase.LoadAssetAtPath<LootDefinition>("Assets/Data/Inventory/scrap.asset");
            var chip = AssetDatabase.LoadAssetAtPath<LootDefinition>("Assets/Data/Inventory/chip.asset");
            if (square == null || font == null || shot == null || corpse == null || scrap == null || chip == null)
                throw new InvalidOperationException("Combat prototype source assets are missing.");

            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var shooter = contents.GetComponent<PlayerShooter2D>();
                if (shooter == null) throw new InvalidOperationException("Install the shooting range first.");
                shooter.SelectWeapon(0);
                var hull = contents.GetComponent<CombatHealth2D>();
                if (hull == null) hull = contents.AddComponent<CombatHealth2D>();
                hull.Configure(CombatTeam.Player, 100, contents.transform.Find("Visual/Body").GetComponent<SpriteRenderer>());
                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            Transform gameplay = GameObject.Find("Gameplay")?.transform;
            Transform player = gameplay?.Find("Player");
            if (player == null) throw new InvalidOperationException("ExploreScene Player is missing.");
            var health = player.GetComponent<CombatHealth2D>();
            if (health == null) throw new InvalidOperationException("Player prefab instance has no hull health.");
            Transform enemies = gameplay.Find("Enemies");
            EnsureEnemy(enemies, "Scavenger", player.position + new Vector3(15f, 0f),
                EnemyCombat2D.AttackKind.Ranged, square, shot, corpse, chip, 70,
                new Color(.26f, .78f, .82f), 9f, 1.7f, 6f, 11);
            EnsureEnemy(enemies, "MutatedFish", player.position + new Vector3(21f, -1.5f),
                EnemyCombat2D.AttackKind.Lunge, square, null, corpse, scrap, 50,
                new Color(.89f, .38f, .37f), 7f, 2.6f, 3.2f, 17);
            Transform cover = GameObject.Find("LevelGeometry")?.transform;
            if (cover == null) throw new InvalidOperationException("LevelGeometry is missing.");
            EnsureCover(cover, "Cover_Left", player.position + new Vector3(12f, 0f), square);
            EnsureCover(cover, "Cover_Right", player.position + new Vector3(19f, 1f), square);
            EnsureExtraction(gameplay.Find("ExtractionPoints"), player.position + new Vector3(26f, 0f), square);
            EnsureShootingHud(font);
            EnsureSession(health, font);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Validate();
            Debug.Log("Combat prototype installed: reload, hull health, two AI threats, loot and extraction.");
        }

        [MenuItem("LootTheDeep/Validate Combat Prototype")]
        public static void Validate()
        {
            if (SceneManager.GetActiveScene().path != ScenePath) return;
            var player = Object.FindFirstObjectByType<PlayerShooter2D>();
            if (player?.GetComponent<CombatHealth2D>() == null ||
                Object.FindObjectsByType<EnemyCombat2D>(FindObjectsSortMode.None).Length != 2 ||
                Object.FindFirstObjectByType<ExtractionPoint2D>() == null ||
                Object.FindFirstObjectByType<CombatPrototypeSession2D>() == null)
                throw new InvalidOperationException("Combat prototype scene references are incomplete.");
            if (player.ActiveGunConfig == null || player.ActiveGunConfig.reloadSeconds <= 0f)
                throw new InvalidOperationException("Weapon reload time is missing.");
        }

        private static void EnsureEnemy(Transform parent, string name, Vector3 position,
            EnemyCombat2D.AttackKind kind, Sprite square, ShootingProjectile2D shot,
            GameObject corpsePrefab, LootDefinition drop, int hp, Color tint,
            float sight, float speed, float reach, int damage)
        {
            if (parent.Find(name) != null) return;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var body = root.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            root.AddComponent<CircleCollider2D>().radius = .47f;
            SpriteRenderer face = Sprite(root.transform, "Body", square, tint, Vector2.zero,
                kind == EnemyCombat2D.AttackKind.Ranged ? new Vector2(.65f, .9f) : new Vector2(.9f, .55f));
            SpriteRenderer marker = Sprite(root.transform, "AttackWarning", square,
                new Color(1f, .8f, .28f), new Vector2(0f, .74f), new Vector2(.48f, .1f));
            marker.gameObject.SetActive(false);
            var health = root.AddComponent<CombatHealth2D>();
            health.Configure(CombatTeam.Hostile, hp, face);
            var remains = (GameObject)PrefabUtility.InstantiatePrefab(corpsePrefab, parent.gameObject.scene);
            remains.transform.SetParent(root.transform, false);
            remains.transform.localPosition = new Vector3(0f, -.55f);
            var container = remains.GetComponent<SearchableContainer>();
            container.ConfigureFixedPrototype(kind == EnemyCombat2D.AttackKind.Ranged ? "打捞者遗留物" : "变异鱼残骸", drop);
            remains.SetActive(false);
            root.AddComponent<EnemyCombat2D>().Configure(kind, shot, marker, container, sight, speed, reach, damage);
        }

        private static void EnsureCover(Transform parent, string name, Vector3 position, Sprite square)
        {
            if (parent.Find(name) != null) return;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            Sprite(root.transform, "Wreckage", square, new Color(.22f, .41f, .42f),
                Vector2.zero, new Vector2(1.4f, 2.4f));
            root.AddComponent<BoxCollider2D>().size = new Vector2(1.4f, 2.4f);
        }

        private static void EnsureExtraction(Transform parent, Vector3 position, Sprite square)
        {
            if (parent.Find("TestExtraction") != null) return;
            var root = new GameObject("TestExtraction");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            Sprite(root.transform, "Dock", square, new Color(.2f, .78f, .53f),
                Vector2.zero, new Vector2(1.2f, 1.8f));
            Sprite(root.transform, "Beacon", square, new Color(.9f, 1f, .64f),
                new Vector2(0f, 1.05f), new Vector2(.38f, .18f));
            root.AddComponent<ExtractionPoint2D>();
        }

        private static void EnsureShootingHud(Font font)
        {
            var hud = Object.FindFirstObjectByType<ShootingHud2D>();
            if (hud == null) throw new InvalidOperationException("Shooting HUD is missing.");
            hud.GetComponent<Canvas>().sortingOrder = 120;
            if (hud.transform.Find("ReloadGroup") != null) return;
            RectTransform group = Rect(hud.transform, "ReloadGroup", Vector2.one,
                new Vector2(-26f, -90f), new Vector2(250f, 45f));
            Image track = Image(group, "Track", new Vector2(0f, -28f), new Vector2(250f, 8f),
                new Color(.08f, .2f, .23f));
            Image fill = Image(track.transform, "Fill", Vector2.zero, new Vector2(250f, 8f),
                new Color(.36f, .94f, .7f));
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            Text label = Label(group, "ReloadLabel", font, "换弹中", new Vector2(0f, -3f),
                new Vector2(250f, 26f), TextAnchor.MiddleRight, 18);
            hud.ConfigureReload(group, fill, label);
        }

        private static void EnsureSession(CombatHealth2D player, Font font)
        {
            if (Object.FindFirstObjectByType<CombatPrototypeSession2D>() != null) return;
            var root = new GameObject("CombatPrototypeHUD", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 130;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            RectTransform hp = Rect(root.transform, "HullStatus", new Vector2(0f, 1f),
                new Vector2(30f, -24f), new Vector2(250f, 56f));
            hp.pivot = new Vector2(0f, 1f);
            Text hpLabel = Label(hp, "HullText", font, "艇体 100 / 100", new Vector2(0f, 0f),
                new Vector2(250f, 30f), TextAnchor.MiddleLeft, 20);
            Image hpTrack = Image(hp, "HullTrack", new Vector2(0f, -40f),
                new Vector2(240f, 9f), new Color(.08f, .2f, .23f));
            Image hpFill = Image(hpTrack.transform, "HullFill", Vector2.zero,
                new Vector2(240f, 9f), new Color(.42f, .9f, .63f));
            hpFill.type = UnityEngine.UI.Image.Type.Filled;
            hpFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            Text exit = Label(root.transform, "ExtractionPrompt", font, "", new Vector2(0f, 95f),
                new Vector2(560f, 40f), TextAnchor.MiddleCenter, 24);
            var result = new GameObject("ResultPanel", typeof(RectTransform), typeof(Image));
            result.transform.SetParent(root.transform, false);
            var resultRect = (RectTransform)result.transform;
            resultRect.anchorMin = Vector2.zero; resultRect.anchorMax = Vector2.one;
            resultRect.offsetMin = resultRect.offsetMax = Vector2.zero;
            result.GetComponent<Image>().color = new Color(.025f, .1f, .13f, .85f);
            Text title = Label(result.transform, "Title", font, "", new Vector2(0f, 95f),
                new Vector2(650f, 55f), TextAnchor.MiddleCenter, 34);
            Text items = Label(result.transform, "Items", font, "", new Vector2(0f, 0f),
                new Vector2(650f, 135f), TextAnchor.MiddleCenter, 22);
            RectTransform buttonRect = Rect(result.transform, "Restart", new Vector2(.5f, .5f),
                new Vector2(0f, -120f), new Vector2(190f, 50f));
            var background = buttonRect.gameObject.AddComponent<Image>();
            background.color = new Color(.3f, .75f, .57f);
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            Label(buttonRect, "Label", font, "重新开始", Vector2.zero,
                new Vector2(190f, 50f), TextAnchor.MiddleCenter, 23);
            root.AddComponent<CombatPrototypeSession2D>().Configure(player, hpLabel, hpFill, exit,
                result, title, items, button);
            result.SetActive(false);
        }

        private static SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, Color tint,
            Vector2 offset, Vector2 size)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = offset;
            child.transform.localScale = size;
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.color = tint;
            renderer.sortingLayerName = "World"; renderer.sortingOrder = 4;
            return renderer;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor,
            Vector2 position, Vector2 size)
        {
            var child = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }

        private static Image Image(Transform parent, string name, Vector2 position, Vector2 size, Color tint)
        {
            RectTransform rect = Rect(parent, name, new Vector2(.5f, .5f), position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = tint; image.raycastTarget = false;
            return image;
        }

        private static Text Label(Transform parent, string name, Font font, string text,
            Vector2 position, Vector2 size, TextAnchor alignment, int fontSize)
        {
            RectTransform rect = Rect(parent, name, new Vector2(.5f, .5f), position, size);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = fontSize; label.text = text;
            label.alignment = alignment; label.color = new Color(.9f, 1f, .94f);
            label.raycastTarget = false;
            return label;
        }
    }
}
