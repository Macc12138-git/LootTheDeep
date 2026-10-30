using System;
using System.Collections.Generic;
using System.Linq;
using LootTheDeep.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LootTheDeep.Inventory.Editor
{
    [InitializeOnLoad]
    public static class WorldSearchValidation
    {
        private const string RunningKey = "LootTheDeep.WorldSearchValidation.Running";
        private static int step, checks, ammo;
        private static float nextTime;
        private static double deadline, nextEditorTime;
        private static InventorySession inventory;
        private static SearchableContainer box, testBox;
        private static LootItem[] originalItems;
        private static PlayerShooter2D shooter;
        private static Vector2 origin;
        private static GameObject wall;

        static WorldSearchValidation() { EditorApplication.playModeStateChanged += OnPlayState; }

        // Five boxes and a box near spawn are fixtures of the demonstration layout only.
        [MenuItem("LootTheDeep/搜索/验证演示布局搜索（运行测试） %#F4")]
        public static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty ||
                scene.path != "Assets/Scenes/ExploreScene.unity" || Object.FindFirstObjectByType<InventorySession>() == null)
                throw new InvalidOperationException("请先接入搜索并保存 ExploreScene，在编辑模式运行验证。");
            Debug.Log($"WORLD_SEARCH_VALIDATION_START: dataPath={Application.dataPath}; product={Application.productName}; scene={scene.path}");
            SessionState.SetBool(RunningKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunningKey, false))
            {
                step = checks = 0;
                nextTime = .1f;
                nextEditorTime = EditorApplication.timeSinceStartup + .5;
                deadline = EditorApplication.timeSinceStartup + 45;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= Tick;
                SessionState.SetBool(RunningKey, false);
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.timeSinceStartup < nextEditorTime) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new Exception("WORLD_SEARCH_CHECK_FAILED: timeout at step " + step);
                EditorApplication.QueuePlayerLoopUpdate();
                if (Time.time < nextTime) return;
                switch (step)
                {
                    case 0:
                        inventory = Object.FindFirstObjectByType<InventorySession>();
                        shooter = inventory.player.GetComponent<PlayerShooter2D>();
                        origin = inventory.player.transform.position;
                        var boxes = GameObject.Find("MapContainers").GetComponentsInChildren<SearchableContainer>();
                        Check(boxes.Length == 5 && Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1,
                            "五个容器与唯一的 UI 输入系统");
                        var navigation = Object.FindFirstObjectByType<TilemapPathfinder2D>();
                        foreach (var placed in boxes)
                        {
                            Check(placed.Contents != null && placed.Contents.Items.Count >= 2 && placed.FoundCount == 0 &&
                                placed.Contents.Items.All(item => !item.Revealed), "随机物资只初始化为未发现物品：" + placed.name);
                            Check(navigation.TryFindPath(origin, placed.transform.position, new List<Vector2>()), "容器可从出生点抵达：" + placed.name);
                            Check(placed.GetComponent<SpriteRenderer>().bounds.size.x >= 1f, "世界容器显示尺寸：" + placed.name);
                        }
                        box = boxes.OrderBy(c => Vector2.Distance(origin, c.transform.position)).First();
                        originalItems = box.Contents.Items.ToArray();
                        Check(Vector2.Distance(origin, box.transform.position) <= inventory.interactDistance, "出生点可直接体验搜索");
                        GameObject.Find("CombatEncounter").SetActive(false);
                        box.searchSeconds = 2f;
                        Keys(Key.F);
                        Advance(.12f);
                        break;
                    case 1:
                        Check(inventory.IsOpen && inventory.Target == box && box.Searching, "F 键打开最近容器并开始搜索");
                        Check(Time.timeScale == 1f && !originalItems[0].Revealed && !inventory.Backpack.TryAddFirst(originalItems[0]),
                            "搜索不暂停世界，未知物品不能转移");
                        ammo = shooter.ActiveGun.Ammo;
                        Keys(Key.W, Key.R);
                        InputSystem.QueueStateEvent(Mouse.current,
                            new MouseState { position = new Vector2(Screen.width * .75f, Screen.height * .5f) }.WithButton(MouseButton.Left));
                        Advance(.3f);
                        break;
                    case 2:
                        Check(Vector2.Distance(origin, inventory.player.transform.position) < .01f &&
                            inventory.player.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero, "搜索时移动输入被锁定");
                        Check(shooter.ActiveGun.Ammo == ammo && !shooter.ActiveGun.IsReloading &&
                            Object.FindObjectsByType<ShootingProjectile2D>(FindObjectsSortMode.None).Length == 0, "搜索时点击和 R 不会射击或换弹");
                        Keys();
                        InputSystem.QueueStateEvent(Mouse.current, new MouseState());
                        Canvas.ForceUpdateCanvases();
                        var progress = inventory.GetComponentsInChildren<Image>().First(i => i.type == Image.Type.Filled);
                        var mesh = progress.canvasRenderer.GetMesh();
                        Check(progress.sprite != null && progress.fillAmount > 0f && progress.fillAmount < 1f && mesh != null &&
                            Mathf.Abs(mesh.bounds.size.x - progress.rectTransform.rect.width * progress.fillAmount) <=
                                1.1f / progress.canvas.scaleFactor,
                            $"逐件搜索进度条实际绘制长度正确 (sprite={progress.sprite}, fill={progress.fillAmount}, " +
                            $"width={progress.rectTransform.rect.width}, mesh={mesh?.bounds.size.x}, scale={progress.canvas.scaleFactor})");
                        Keys(Key.Escape);
                        Advance(.1f);
                        break;
                    case 3:
                        Check(!inventory.IsOpen && !box.Searching && box.Progress == 0f && box.FoundCount == 0, "Esc 关闭并中断当前单件搜索");
                        Keys();
                        Advance(.1f);
                        break;
                    case 4:
                        Keys(Key.F);
                        Advance(.1f);
                        break;
                    case 5:
                        Keys();
                        Check(inventory.IsOpen && box.Contents.Items.SequenceEqual(originalItems), "重开不会重新抽取物资");
                        box.searchSeconds = .75f;
                        Advance(.8f);
                        break;
                    case 6:
                        Check(box.FoundCount > 0 && originalItems.Any(i => i.Revealed), "计时结束揭示物品");
                        if (box.Current != null)
                        {
                            Button("SearchToggle").onClick.Invoke();
                            Check(!box.Searching, "暂停搜索按钮生效");
                            Button("SearchToggle").onClick.Invoke();
                            Check(box.Searching, "继续搜索按钮生效");
                        }
                        var revealed = originalItems.First(i => i.Revealed);
                        int before = box.Contents.Items.Count + inventory.Backpack.Items.Count;
                        inventory.View.Select(revealed);
                        Button("Transfer").onClick.Invoke();
                        Check(revealed.Owner == inventory.Backpack && !box.Contents.Items.Contains(revealed) &&
                            box.Contents.Items.Count + inventory.Backpack.Items.Count == before, "按钮转移物品，无复制或丢失");
                        inventory.QuickTransfer(revealed);
                        var single = AssetDatabase.LoadAssetAtPath<LootDefinition>("Assets/Data/Inventory/bandage.asset");
                        while (inventory.Backpack.TryAddFirst(new LootItem(single))) { }
                        inventory.QuickTransfer(revealed);
                        Check(revealed.Owner == box.Contents, "背包满时保留原容器物品");
                        inventory.Close();
                        inventory.Open(box);
                        Check(revealed.Revealed && box.FoundCount > 0, "已发现物品在关闭重开后保留");
                        MovePlayer(new Vector2(0, -200));
                        Advance(.1f);
                        break;
                    case 7:
                        Keys();
                        Check(!inventory.IsOpen, "超出距离自动关闭搜索");
                        inventory.Open(box);
                        Check(!inventory.IsOpen, "远距离不能打开容器");
                        var testRoot = new GameObject("SearchValidationContainer");
                        testRoot.transform.position = new Vector2(1.5f, -200);
                        testBox = testRoot.AddComponent<SearchableContainer>();
                        testBox.ConfigureFixedPrototype("验证箱", originalItems[0].Definition);
                        inventory.RegisterPrototypeContainer(testBox);
                        wall = new GameObject("SearchValidationWall");
                        wall.transform.position = new Vector2(.75f, -200);
                        wall.AddComponent<BoxCollider2D>().size = new Vector2(.15f, 3f);
                        Physics2D.SyncTransforms();
                        inventory.Open(testBox);
                        Check(!inventory.IsOpen, "墙体遮挡时不能打开容器");
                        wall.GetComponent<Collider2D>().enabled = false;
                        inventory.Open(testBox);
                        Check(inventory.IsOpen, "移除遮挡后可以搜索");
                        wall.GetComponent<Collider2D>().enabled = true;
                        Physics2D.SyncTransforms();
                        Advance(.1f);
                        break;
                    case 8:
                        Check(!inventory.IsOpen && !testBox.Searching, "搜索过程中被墙体阻隔会中断");
                        wall.GetComponent<Collider2D>().enabled = false;
                        inventory.Open(testBox);
                        testBox.gameObject.SetActive(false);
                        Advance(.1f);
                        break;
                    case 9:
                        Check(!inventory.IsOpen, "目标失活后关闭页面");
                        Keys(Key.Tab);
                        Advance(.1f);
                        break;
                    case 10:
                        Check(inventory.IsOpen && inventory.Target == null, "Tab 打开随身物品");
                        Keys(Key.Escape);
                        Advance(.1f);
                        break;
                    case 11:
                        Check(!inventory.IsOpen, "Esc 关闭随身物品");
                        Keys(Key.W);
                        Advance(.3f);
                        break;
                    case 12:
                        Check(Vector2.Distance(new Vector2(0, -200), inventory.player.transform.position) > .05f, "关闭后恢复移动");
                        Keys();
                        MovePlayer(new Vector2(0, -200));
                        testBox.gameObject.SetActive(true);
                        inventory.Open(testBox);
                        inventory.player.GetComponent<CombatHealth2D>().TakeDamage(1000);
                        var combat = Object.FindFirstObjectByType<CombatPrototypeSession2D>();
                        Check(combat.IsFinished && Time.timeScale == 0 && !inventory.IsOpen && !inventory.enabled && !testBox.Searching,
                            "搜索中死亡会中断搜索并进入失败界面");
                        combat.Restart();
                        nextTime = 0;
                        step++;
                        nextEditorTime = EditorApplication.timeSinceStartup + 1;
                        break;
                    default:
                        var restarted = Object.FindFirstObjectByType<InventorySession>();
                        Check(restarted.enabled && !restarted.IsOpen && Time.timeScale == 1f &&
                            Object.FindObjectsByType<SearchableContainer>(FindObjectsSortMode.None).Length == 5 &&
                            Object.FindObjectsByType<SearchableContainer>(FindObjectsSortMode.None).All(c => c.FoundCount == 0),
                            "重试恢复搜索状态与地图容器");
                        Debug.Log($"WORLD_SEARCH_PLAY_CHECKS_PASSED: {checks} checks; placement, F/Esc/Tab, reveal, rendered progress, transfer, full backpack, cover, movement/shooting lock, death and restart.");
                        Stop();
                        break;
                }
            }
            catch (Exception error) { Debug.LogException(error); Stop(); }
        }

        private static Button Button(string name) => inventory.GetComponentsInChildren<Button>().First(b => b.name == name);
        private static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        private static void MovePlayer(Vector2 position)
        {
            inventory.player.GetComponent<Rigidbody2D>().position = position;
            inventory.player.transform.position = position;
            Physics2D.SyncTransforms();
        }
        private static void Advance(float seconds) { step++; nextTime = Time.time + seconds; }
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception("WORLD_SEARCH_CHECK_FAILED: " + message);
            checks++;
        }
        private static void Stop()
        {
            if (Keyboard.current != null) Keys();
            if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
        }
    }
}
