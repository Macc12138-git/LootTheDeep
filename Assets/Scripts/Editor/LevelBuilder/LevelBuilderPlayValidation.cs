using System;
using System.IO;
using System.Linq;
using LootTheDeep.Gameplay;
using LootTheDeep.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LootTheDeep.LevelBuilder
{
    [InitializeOnLoad]
    public static class LevelBuilderPlayValidation
    {
        private const string Key = "LootTheDeep.LevelBuilder.PlayValidation";
        private static double readyAt;

        [Serializable] private sealed class Snapshot { public Row[] rows; }
        [Serializable] private sealed class Row
        {
            public string path;
            public Vector3 position, scale;
            public Quaternion rotation;
            public bool active;
            public int health, columns, rows;
            public float searchSeconds;
        }

        static LevelBuilderPlayValidation() => EditorApplication.playModeStateChanged += StateChanged;

        private static string Capture()
        {
            var scene = SceneManager.GetActiveScene();
            var transforms = LevelBuilderScene.All<PlayerShooter2D>(scene).Select(p => p.transform)
                .Concat(LevelBuilderScene.All<EnemyCombat2D>(scene).Select(e => e.transform))
                .Concat(LevelBuilderScene.All<SearchableContainer>(scene).Select(b => b.transform));
            return JsonUtility.ToJson(new Snapshot { rows = transforms.Select(t =>
            {
                var health = t.GetComponent<CombatHealth2D>();
                var box = t.GetComponent<SearchableContainer>();
                string path = t.name;
                for (Transform parent = t.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
                return new Row { path = path, position = t.position, rotation = t.rotation, scale = t.localScale,
                    active = t.gameObject.activeSelf, health = health != null ? health.MaxHealth : 0,
                    columns = box != null ? box.Columns : 0, rows = box != null ? box.Rows : 0, searchSeconds = box != null ? box.searchSeconds : 0 };
            }).OrderBy(r => r.path, StringComparer.Ordinal).ToArray() });
        }

        [MenuItem("LootTheDeep/开发者模式/验证试玩隔离（运行测试）")]
        public static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != LevelBuilderScene.ExplorePath || scene.isDirty)
                throw new InvalidOperationException("请先保存 ExploreScene，在编辑模式运行试玩隔离检查。");
            if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                throw new InvalidOperationException("此验证需要启用 Scene Reload。");
            SessionState.SetString(Key + ".Snapshot", Capture());
            SessionState.SetString(Key + ".File", Hash128.Compute(File.ReadAllText(scene.path)).ToString());
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".Mutated", false);
            Debug.Log($"LEVEL_BUILDER_PLAY_VALIDATION_START: dataPath={Application.dataPath}; product={Application.productName}; scene={scene.path}");
            EditorApplication.isPlaying = true;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                readyAt = EditorApplication.timeSinceStartup + .6;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= Tick;
                SessionState.SetBool(Key, false);
                var scene = SceneManager.GetActiveScene();
                bool restored = Capture() == SessionState.GetString(Key + ".Snapshot", "") &&
                    Hash128.Compute(File.ReadAllText(scene.path)).ToString() == SessionState.GetString(Key + ".File", "") &&
                    LevelBuilderScene.All<SearchableContainer>(scene).All(b => b.FoundCount == 0 && !b.Searching);
                if (!restored || !SessionState.GetBool(Key + ".Mutated", false)) Debug.LogError("LEVEL_BUILDER_PLAY_CHECK_FAILED: 运行变化或编辑布局恢复不符合预期。");
                else Debug.Log("LEVEL_BUILDER_PLAY_CHECKS_PASSED: runtime movement, enemy death and container search discarded; saved layout and instance properties restored.");
            }
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < readyAt) { EditorApplication.QueuePlayerLoopUpdate(); return; }
            EditorApplication.update -= Tick;
            try
            {
                var scene = SceneManager.GetActiveScene();
                var enemy = LevelBuilderScene.All<EnemyCombat2D>(scene).FirstOrDefault();
                if (enemy != null) { enemy.transform.position += Vector3.one * 3; enemy.GetComponent<CombatHealth2D>().TakeDamage(int.MaxValue); }
                var box = LevelBuilderScene.All<SearchableContainer>(scene).FirstOrDefault(b => b.Current != null);
                if (box != null) { box.BeginSearch(); box.Tick(box.searchSeconds + 1f); if (box.FoundCount == 0) throw new Exception("未推进搜索。"); }
                var player = LevelBuilderScene.All<PlayerShooter2D>(scene).First();
                player.transform.position += Vector3.right * 5;
                SessionState.SetBool(Key + ".Mutated", true);
            }
            catch (Exception exception) { SessionState.SetBool(Key + ".Mutated", false); Debug.LogException(exception); }
            finally { EditorApplication.isPlaying = false; }
        }
    }
}
