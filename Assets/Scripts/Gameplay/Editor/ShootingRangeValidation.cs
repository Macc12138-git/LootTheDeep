using System;
using System.Reflection;
using LootTheDeep.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LootTheDeep.Gameplay.EditorTools
{
    // Batch validation runs in a disposable project copy so Play Mode cannot alter the user's editor state.
    public sealed class ShootingRangeValidation
    {
        private PlayerShooter2D shooter;
        private TargetDummy2D closeTarget;
        private TargetDummy2D middleTarget;
        private TargetDummy2D farTarget;
        private double nextCheckAt;
        private int step;
        private bool finished;

        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run shooting validation only in a disposable batch project.");
            EditorSceneManager.OpenScene("Assets/Scenes/ExploreScene.unity");
            ShootingRangeInstaller.Validate();
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            var validation = new ShootingRangeValidation();
            EditorApplication.playModeStateChanged += validation.OnPlayStateChanged;
            EditorApplication.update += validation.Tick;
            EditorApplication.isPlaying = true;
        }

        private void OnPlayStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                nextCheckAt = EditorApplication.timeSinceStartup + 1.5;
            if (state == PlayModeStateChange.EnteredEditMode && finished)
            {
                Debug.Log("SHOOTING_RANGE_PLAY_CHECKS_PASSED");
                EditorApplication.Exit(0);
            }
        }

        private void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextCheckAt || finished) return;
            try
            {
                if (step == 0)
                {
                    shooter = Object.FindFirstObjectByType<PlayerShooter2D>();
                    closeTarget = GameObject.Find("Target_Close")?.GetComponent<TargetDummy2D>();
                    middleTarget = GameObject.Find("Target_Middle")?.GetComponent<TargetDummy2D>();
                    farTarget = GameObject.Find("Target_Far")?.GetComponent<TargetDummy2D>();
                    Check(shooter != null && closeTarget != null && middleTarget != null && farTarget != null,
                        "player and targets loaded");
                    Check(Object.FindFirstObjectByType<ShootingHud2D>() != null, "HUD loaded");
                    Check(Object.FindFirstObjectByType<InventorySession>() != null, "inventory retained");
                    Check(shooter.ActiveWeaponIndex == 0 && shooter.ActiveWeapon.visual.activeSelf, "main weapon active");
                    FireAt(closeTarget);
                    step = 1;
                    nextCheckAt = EditorApplication.timeSinceStartup + 0.8;
                    return;
                }
                if (step == 1)
                {
                    Check(closeTarget.Health == 0, "main projectile destroyed close target");
                    shooter.SelectWeapon(1);
                    Check(shooter.ActiveWeaponIndex == 1 && shooter.ActiveWeapon.visual.activeSelf,
                        "secondary weapon selected");
                    FireAt(middleTarget);
                    step = 2;
                    nextCheckAt = EditorApplication.timeSinceStartup + 0.8;
                    return;
                }
                if (step == 2)
                {
                    Check(middleTarget.Health == 52, "secondary projectile dealt eight damage once");
                    typeof(PlayerShooter2D).GetField("aimWorldPoint", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(shooter, (Vector2)farTarget.transform.position);
                    typeof(PlayerShooter2D).GetMethod("TryFire", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(shooter, null);
                    Check(shooter.ActiveWeapon.Ammo == 23, "secondary shot spent one test round");
                    Check(shooter.ActiveWeapon.muzzleFlash.gameObject.activeSelf, "active muzzle flash shown");
                    step = 3;
                    nextCheckAt = EditorApplication.timeSinceStartup + 1.1;
                    return;
                }
                Check(farTarget.Health == 92, "controller shot hit far target once");
                finished = true;
                EditorApplication.update -= Tick;
                EditorApplication.isPlaying = false;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private void FireAt(TargetDummy2D target)
        {
            var weapon = shooter.ActiveWeapon;
            Vector2 start = weapon.muzzle.position;
            Vector2 direction = ((Vector2)target.transform.position - start).normalized;
            var projectile = Object.Instantiate(weapon.projectilePrefab, start, Quaternion.identity);
            projectile.Fire(direction, weapon.projectileSpeed, weapon.damage, shooter.GetComponent<Collider2D>());
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("SHOOTING_RANGE_PLAY_CHECK_FAILED: " + message);
        }
    }
}
