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
    public sealed class CombatPrototypeValidation
    {
        private int step;
        private double nextCheckAt;
        private PlayerShooter2D shooter;
        private InventorySession inventory;
        private CombatPrototypeSession2D session;
        private CombatHealth2D player;
        private int healthBeforeFish;

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a disposable batch project.");
            EditorSceneManager.OpenScene("Assets/Scenes/ExploreScene.unity");
            CombatPrototypeInstaller.Validate();
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            var test = new CombatPrototypeValidation();
            EditorApplication.playModeStateChanged += test.OnPlayStateChanged;
            EditorApplication.update += test.Tick;
            EditorApplication.isPlaying = true;
        }

        private void OnPlayStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                nextCheckAt = EditorApplication.timeSinceStartup + 1.2;
        }

        private void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextCheckAt) return;
            try
            {
                if (step == 0)
                {
                    shooter = Object.FindFirstObjectByType<PlayerShooter2D>();
                    inventory = Object.FindFirstObjectByType<InventorySession>();
                    session = Object.FindFirstObjectByType<CombatPrototypeSession2D>();
                    player = shooter.GetComponent<CombatHealth2D>();
                    Check(inventory != null && session != null && player.CurrentHealth == 100, "combat loaded");
                    Check(Object.FindObjectsByType<EnemyCombat2D>(FindObjectsSortMode.None).Length == 2,
                        "both enemies loaded");
                    for (int i = 0; i < shooter.ActiveGunConfig.magazineCapacity - 2; i++)
                        shooter.ActiveGun.TryFire(Vector2.right, i * (shooter.ActiveGunConfig.ShotInterval + .001f), out _);
                    shooter.Reload();
                    Check(shooter.ActiveGun.IsReloading, "main reload started");
                    inventory.Open(null);
                    Check(inventory.IsOpen, "inventory open during reload");
                    step = 1;
                    nextCheckAt = EditorApplication.timeSinceStartup + .25;
                    return;
                }
                if (step == 1)
                {
                    Transform bar = Object.FindFirstObjectByType<ShootingHud2D>().transform.Find("ReloadGroup");
                    Image fill = bar?.Find("Track/Fill")?.GetComponent<Image>();
                    Check(bar != null && bar.gameObject.activeSelf && fill != null && fill.fillAmount > 0f,
                        "reload bar remains visible over inventory");
                    shooter.SelectWeapon(1);
                    step = 2;
                    nextCheckAt = EditorApplication.timeSinceStartup + 2f;
                    return;
                }
                if (step == 2)
                {
                    shooter.SelectWeapon(0);
                    Check(shooter.ActiveGun.Ammo == 6 && !shooter.ActiveGun.IsReloading,
                        "reload completed after switching weapons and opening inventory");
                    inventory.Close();
                    shooter.GetComponent<Rigidbody2D>().position = new Vector2(15f, -4.5f);
                    step = 3;
                    nextCheckAt = EditorApplication.timeSinceStartup + 2.3;
                    return;
                }
                if (step == 3)
                {
                    Check(player.CurrentHealth < 100, "ranged enemy acquired and hit player");
                    shooter.GetComponent<Rigidbody2D>().position = new Vector2(0f, -8f);
                    var enemy = GameObject.Find("Scavenger").GetComponent<CombatHealth2D>();
                    Vector2 origin = (Vector2)enemy.transform.position + Vector2.left;
                    var projectile = Object.Instantiate(shooter.ActiveGunConfig.projectilePrefab, origin, Quaternion.identity);
                    projectile.Fire(Vector2.right, 8f, 30, shooter.GetComponent<Collider2D>());
                    step = 4;
                    nextCheckAt = EditorApplication.timeSinceStartup + .5;
                    return;
                }
                if (step == 4)
                {
                    var enemy = GameObject.Find("Scavenger").GetComponent<CombatHealth2D>();
                    Check(enemy.CurrentHealth == 40,
                        "projectile dealt damage once to hostile");
                    enemy.TakeDamage(40);
                    var remains = GameObject.Find("Scavenger").GetComponentInChildren<SearchableContainer>();
                    Check(remains != null && remains.isActiveAndEnabled && remains.Contents != null &&
                        remains.Contents.Items.Count == 1, "enemy remains are searchable");
                    int before = player.CurrentHealth;
                    player.TakeDamage(11);
                    Check(player.CurrentHealth == before - 11, "player took damage");
                    healthBeforeFish = player.CurrentHealth;
                    shooter.GetComponent<Rigidbody2D>().position = new Vector2(22.5f, -9.5f);
                    step = 5;
                    nextCheckAt = EditorApplication.timeSinceStartup + 2.5;
                    return;
                }
                if (step == 5)
                {
                    Check(player.CurrentHealth < healthBeforeFish, "fish lunged and hit player");
                    session.Finish(true);
                    Check(session.IsFinished && Time.timeScale == 0f, "extraction result freezes encounter");
                    session.Restart();
                    step = 6;
                    nextCheckAt = EditorApplication.timeSinceStartup + 1f;
                    return;
                }
                player = Object.FindFirstObjectByType<PlayerShooter2D>().GetComponent<CombatHealth2D>();
                session = Object.FindFirstObjectByType<CombatPrototypeSession2D>();
                player.TakeDamage(100);
                Check(player.IsDead && session.IsFinished, "death ends encounter");
                Debug.Log("COMBAT_PROTOTYPE_PLAY_CHECKS_PASSED");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("COMBAT_PROTOTYPE_PLAY_CHECK_FAILED: " + message);
        }
    }
}
