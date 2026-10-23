using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Aseprite;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LootTheDeep.Gameplay.EditorTools
{
    public static class HeroAnimationInstaller
    {
        public const string SourcePath = "Assets/Art/Characters/Hero/Hero.aseprite";
        public const string ControllerPath = "Assets/Art/Characters/Hero/Hero.controller";
        private const string PlayerPath = "Assets/Prefabs/Gameplay/Player.prefab";

        [MenuItem("LootTheDeep/Player/Install Hero Animations")]
        public static void Install()
        {
            RequireExploreScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing Hero animations.");

            var importer = AssetImporter.GetAtPath(SourcePath) as AsepriteImporter;
            if (importer == null) throw new InvalidOperationException("Import Hero.aseprite first.");
            importer.importMode = FileImportModes.AnimatedSprite;
            importer.layerImportMode = LayerImportModes.MergeFrame;
            importer.pivotSpace = PivotSpaces.Canvas;
            importer.pivotAlignment = SpriteAlignment.Center;
            importer.spritePixelsPerUnit = 32f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.generatePhysicsShape = false;
            importer.generateAnimationClips = true;
            importer.generateModelPrefab = true;
            importer.SaveAndReimport();

            var imported = AssetDatabase.LoadAllAssetsAtPath(SourcePath);
            AnimationClip idle = imported.OfType<AnimationClip>().Single(x => x.name == "Idle");
            AnimationClip move = imported.OfType<AnimationClip>().Single(x => x.name == "Move");
            Sprite neutral = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath)
                .GetComponent<SpriteRenderer>().sprite;
            AnimatorController controller = EnsureController(idle, move);

            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                Transform visual = player.transform.Find("Visual");
                Transform body = visual.Find("Body");
                body.localPosition = Vector3.zero;
                body.localScale = Vector3.one;
                SpriteRenderer renderer = body.GetComponent<SpriteRenderer>();
                renderer.sprite = neutral;
                renderer.color = Color.white;
                visual.Find("FacingMarker").gameObject.SetActive(false);

                Animator animator = body.GetComponent<Animator>();
                if (animator == null) animator = body.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                var movement = new SerializedObject(player.GetComponent<UnderwaterMover2D>());
                movement.FindProperty("animator").objectReferenceValue = animator;
                movement.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }

            AssetDatabase.SaveAssets();
            Validate();
            Selection.activeGameObject = UnityEngine.Object.FindFirstObjectByType<UnderwaterMover2D>().gameObject;
            Debug.Log("HERO_ANIMATIONS_INSTALLED: Idle / Move, IsMoving, 32 PPU, Point filter.");
        }

        private static AnimatorController EnsureController(AnimationClip idle, AnimationClip move)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            if (!controller.parameters.Any(x => x.name == "IsMoving"))
                controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idleState = EnsureState(machine, "Idle", idle, new Vector3(240f, 100f));
            AnimatorState moveState = EnsureState(machine, "Move", move, new Vector3(480f, 100f));
            machine.defaultState = idleState;
            EnsureTransition(idleState, moveState, AnimatorConditionMode.If);
            EnsureTransition(moveState, idleState, AnimatorConditionMode.IfNot);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState EnsureState(AnimatorStateMachine machine, string name,
            AnimationClip clip, Vector3 position)
        {
            AnimatorState state = machine.states.FirstOrDefault(x => x.state.name == name).state;
            if (state == null) state = machine.AddState(name, position);
            state.motion = clip;
            state.writeDefaultValues = false;
            return state;
        }

        private static void EnsureTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode condition)
        {
            AnimatorStateTransition transition = from.transitions.FirstOrDefault(x => x.destinationState == to);
            if (transition == null) transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.conditions = new[] { new AnimatorCondition { mode = condition, parameter = "IsMoving" } };
        }

        [MenuItem("LootTheDeep/Player/Validate Hero Animations")]
        public static void Validate()
        {
            RequireExploreScene();
            var player = UnityEngine.Object.FindFirstObjectByType<UnderwaterMover2D>();
            if (player == null) throw new InvalidOperationException("Player is missing.");
            var data = new SerializedObject(player);
            var animator = data.FindProperty("animator").objectReferenceValue as Animator;
            if (animator == null || animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("Player Animator is not assigned.");
            if (animator.runtimeAnimatorController != AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath))
                throw new InvalidOperationException("Player has the wrong animation controller.");
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                float expectedLength = clip.name == "Idle" ? 1.2f : 0.68f;
                if (!clip.isLooping || Mathf.Abs(clip.length - expectedLength) > 0.02f)
                    throw new InvalidOperationException($"Invalid {clip.name} timing or loop setting: {clip.length}.");
                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                if (bindings.Length != 1 || bindings[0].path != "" || bindings[0].type != typeof(SpriteRenderer))
                    throw new InvalidOperationException($"Unexpected {clip.name} sprite binding.");
            }

            string result = $"dataPath={Application.dataPath}\nproduct={Application.productName}\n" +
                $"scene={SceneManager.GetActiveScene().path}\n" +
                string.Join("\n", animator.runtimeAnimatorController.animationClips.Select(x =>
                    $"{x.name}: {x.length:F2}s, looping={x.isLooping}"));
            WriteReport("hero-import-validation.txt", result);
            Debug.Log("HERO_ANIMATION_IMPORT_CHECKS_PASSED\n" + result);
        }

        internal static void RequireExploreScene()
        {
            if (Application.productName != "LootTheDeep" ||
                SceneManager.GetActiveScene().path != "Assets/Scenes/ExploreScene.unity")
                throw new InvalidOperationException("Run this command in LootTheDeep / ExploreScene.");
        }

        internal static void WriteReport(string name, string text)
        {
            const string directory = "output/aseprite/hero-animation-v1";
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name), text);
        }
    }
}
