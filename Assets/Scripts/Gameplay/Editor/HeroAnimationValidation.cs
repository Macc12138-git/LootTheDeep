using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LootTheDeep.Gameplay.EditorTools
{
    [InitializeOnLoad]
    public static class HeroAnimationValidation
    {
        private const string PendingKey = "LootTheDeep.HeroAnimationPlayCheck";
        private static Probe probe;

        static HeroAnimationValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("LootTheDeep/Player/Run Hero Animation Play Check")]
        public static void Run()
        {
            HeroAnimationInstaller.Validate();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Start the play check from Edit Mode.");
            SessionState.SetBool(PendingKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
            {
                probe = new Probe();
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.update -= Tick;
                probe?.Dispose();
                probe = null;
                SessionState.SetBool(PendingKey, false);
            }
        }

        private static void Tick()
        {
            try
            {
                if (!probe.Tick()) return;
                HeroAnimationInstaller.WriteReport("hero-runtime-validation.txt",
                    "PASSED: initial Idle, both sprite loops, movement, left/right facing, vertical movement, " +
                    "release to Idle, inventory lock/resume, combat lock/resume, disabled movement to Idle.\n" +
                    $"dataPath={Application.dataPath}\n");
                Debug.Log("HERO_ANIMATION_PLAY_CHECKS_PASSED");
            }
            catch (Exception error)
            {
                HeroAnimationInstaller.WriteReport("hero-runtime-validation.txt", "FAILED: " + error);
                Debug.LogException(error);
            }
            EditorApplication.update -= Tick;
            probe?.Dispose();
            probe = null;
            SessionState.SetBool(PendingKey, false);
            EditorApplication.isPlaying = false;
        }

        private sealed class Probe : IDisposable
        {
            private readonly HashSet<Sprite> idleSprites = new();
            private readonly HashSet<Sprite> movingSprites = new();
            private UnderwaterMover2D mover;
            private Rigidbody2D body;
            private Animator animator;
            private SpriteRenderer renderer;
            private Transform visual;
            private Gamepad pad;
            private Vector2 startPosition;
            private int step;
            private double nextAt = EditorApplication.timeSinceStartup + 0.5;

            public bool Tick()
            {
                if (renderer != null && renderer.sprite != null)
                {
                    if (step == 1) idleSprites.Add(renderer.sprite);
                    if (step == 2) movingSprites.Add(renderer.sprite);
                }
                if (EditorApplication.timeSinceStartup < nextAt) return false;
                double delay = 0.25;
                switch (step)
                {
                    case 0:
                        mover = UnityEngine.Object.FindFirstObjectByType<UnderwaterMover2D>();
                        body = mover.GetComponent<Rigidbody2D>();
                        visual = mover.transform.Find("Visual");
                        animator = visual.Find("Body").GetComponent<Animator>();
                        renderer = animator.GetComponent<SpriteRenderer>();
                        pad = InputSystem.AddDevice<Gamepad>();
                        mover.GetComponent<PlayerInput>().SwitchCurrentControlScheme("Gamepad", pad);
                        Drive(Vector2.zero);
                        delay = 1.3;
                        break;
                    case 1:
                        CheckState(false);
                        Check(idleSprites.Count > 1, "Idle sprite loop advances");
                        startPosition = body.position;
                        Drive(Vector2.right);
                        delay = 0.55;
                        break;
                    case 2:
                        CheckState(true);
                        Check(movingSprites.Count > 1, "Move sprite loop advances");
                        Check(body.position.x > startPosition.x + 0.1f, "input moves the Rigidbody2D");
                        Check(visual.localScale.x > 0f, "right-facing");
                        Drive(Vector2.left);
                        break;
                    case 3:
                        CheckState(true);
                        Check(visual.localScale.x < 0f, "left-facing");
                        Drive(Vector2.up);
                        break;
                    case 4:
                        CheckState(true);
                        Check(visual.localScale.x < 0f, "vertical input retains facing");
                        Drive(Vector2.zero);
                        delay = 0.4;
                        break;
                    case 5:
                        CheckState(false);
                        Check(body.linearVelocity.sqrMagnitude < 0.001f, "release stops movement");
                        Drive(Vector2.right);
                        break;
                    case 6:
                        CheckState(true);
                        mover.SetInventoryOpen(true);
                        break;
                    case 7:
                        CheckState(false);
                        Check(body.linearVelocity == Vector2.zero, "inventory locks movement");
                        mover.SetInventoryOpen(false);
                        break;
                    case 8:
                        CheckState(true);
                        mover.SetCombatEnded(true);
                        break;
                    case 9:
                        CheckState(false);
                        Check(body.linearVelocity == Vector2.zero, "combat end locks movement");
                        mover.SetCombatEnded(false);
                        break;
                    case 10:
                        CheckState(true);
                        mover.enabled = false;
                        break;
                    case 11:
                        CheckState(false);
                        return true;
                }
                step++;
                nextAt = EditorApplication.timeSinceStartup + delay;
                return false;
            }

            private void Drive(Vector2 direction)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = direction });
            }

            private void CheckState(bool moving)
            {
                string expected = moving ? "Move" : "Idle";
                Check(animator.GetBool("IsMoving") == moving &&
                    animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer." + expected),
                    $"step {step}: expected {expected}");
            }

            public void Dispose()
            {
                if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            }

            private static void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException("Hero play check: " + message);
            }
        }
    }
}
