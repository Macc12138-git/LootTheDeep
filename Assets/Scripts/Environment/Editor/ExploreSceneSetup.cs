using System;
using System.IO;
using System.Linq;
using LootTheDeep.Environment;
using LootTheDeep.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LootTheDeep.Editor
{
    public static class ExploreSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/ExploreScene.unity";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string MaterialPath = "Assets/Materials/Environment/UnderwaterDepthBackground.mat";
        private const string ShaderName = "LootTheDeep/Environment/UnderwaterDepthBackground";

        [MenuItem("LootTheDeep/Create Explore Scene")]
        public static void BuildExploreScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Exit Play Mode before rebuilding ExploreScene.");
                return;
            }

            SceneAsset existingScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (existingScene != null)
            {
                AddSceneToBuildSettings(ScenePath);
                AssetDatabase.SaveAssets();
                Selection.activeObject = existingScene;
                EditorGUIUtility.PingObject(existingScene);
                Debug.LogWarning("ExploreScene already exists. Scene creation was skipped to preserve its contents.");
                return;
            }

            EnsureSortingLayers("BG_Water", "BG_Far", "BG_Mid", "World", "Foreground", "Effects", "UI");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject systems = CreateRoot("_Systems");
            GameObject environment = CreateRoot("Environment");
            GameObject gameplay = CreateRoot("Gameplay");
            CreateRoot("UI");

            CreateChild("SpawnPoints", gameplay.transform);
            GameObject player = CreatePlayer(gameplay.transform);
            CreateChild("Containers", gameplay.transform);
            CreateChild("Enemies", gameplay.transform);
            CreateChild("ExtractionPoints", gameplay.transform);

            Camera camera = CreateCamera(systems.transform, player.transform);
            Light2D globalLight = CreateGlobalLight(systems.transform);

            GameObject depthControllerObject = new("UnderwaterDepthController");
            depthControllerObject.transform.SetParent(systems.transform);
            UnderwaterDepthController depthController = depthControllerObject.AddComponent<UnderwaterDepthController>();
            depthController.Configure(camera, globalLight);

            GameObject background = CreateChild("Background", environment.transform);
            CreateChild("FarSilhouettes", background.transform);
            CreateChild("MidSilhouettes", background.transform);
            CreateChild("LightRays", background.transform);
            CreateChild("AmbientParticles", background.transform);
            CreateChild("LevelGeometry", environment.transform);
            CreateChild("Foreground", environment.transform);

            CreateOceanBackdrop(background.transform, camera);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = camera.gameObject;
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
            Debug.Log("ExploreScene created. Enter Play Mode and use WASD or arrow keys to move the underwater object.");
        }

        [MenuItem("LootTheDeep/Apply Underwater Movement Prototype")]
        public static void ApplyUnderwaterMovementPrototype()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Exit Play Mode before applying the movement prototype.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                Debug.LogError("Open ExploreScene before applying the movement prototype.");
                return;
            }

            GameObject gameplay = GameObject.Find("Gameplay");
            Camera camera = Camera.main;
            if (gameplay == null || camera == null)
            {
                Debug.LogError("ExploreScene is missing its Gameplay root or Main Camera.");
                return;
            }

            Transform existingPlayer = gameplay.transform.Find("Player");
            GameObject player = existingPlayer != null ? existingPlayer.gameObject : CreateChild("Player", gameplay.transform);
            ConfigurePlayer(player);
            ConfigureCamera(camera, player.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;
            Debug.Log("Underwater movement prototype applied. Use WASD or arrow keys in Play Mode.");
        }

        [MenuItem("LootTheDeep/Capture Explore Preview")]
        public static void CaptureExplorePreview()
        {
            Camera camera = Camera.main;
            UnderwaterDepthController depthController = UnityEngine.Object.FindFirstObjectByType<UnderwaterDepthController>();
            CameraBackdropFitter backdropFitter = UnityEngine.Object.FindFirstObjectByType<CameraBackdropFitter>();

            if (camera == null || depthController == null || backdropFitter == null)
            {
                Debug.LogError("Open ExploreScene before capturing the ocean background preview.");
                return;
            }

            const int width = 640;
            const int height = 360;
            string outputDirectory = Path.Combine(Path.GetTempPath(), "LootTheDeepExplorePreview");
            Directory.CreateDirectory(outputDirectory);

            Vector3 originalPosition = camera.transform.position;
            RenderTexture originalTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                CaptureAtDepth(camera, depthController, backdropFitter, 0f, "shallow.png", outputDirectory, width, height);
                CaptureAtDepth(camera, depthController, backdropFitter, -40f, "mid.png", outputDirectory, width, height);
                CaptureAtDepth(camera, depthController, backdropFitter, -100f, "deep.png", outputDirectory, width, height);
            }
            finally
            {
                camera.transform.position = originalPosition;
                camera.targetTexture = originalTarget;
                RenderTexture.active = previousActive;
                backdropFitter.RefreshNow();
                depthController.RefreshNow();
            }

            Debug.Log($"ExploreScene previews saved to {outputDirectory}");
        }

        private static void CaptureAtDepth(
            Camera camera,
            UnderwaterDepthController depthController,
            CameraBackdropFitter backdropFitter,
            float worldY,
            string fileName,
            string outputDirectory,
            int width,
            int height)
        {
            Vector3 position = camera.transform.position;
            position.y = worldY;
            camera.transform.position = position;
            backdropFitter.RefreshNow();
            depthController.RefreshNow();

            RenderTexture renderTexture = new(width, height, 24, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Point
            };
            Texture2D image = new(width, height, TextureFormat.RGBA32, false, false);

            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(outputDirectory, fileName), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(image);
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        private static Camera CreateCamera(Transform parent, Transform followTarget)
        {
            GameObject cameraObject = new("Main Camera");
            cameraObject.transform.SetParent(parent);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.tag = "MainCamera";

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.063f, 0.137f, 1f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.allowMSAA = false;
            camera.allowHDR = true;

            cameraObject.AddComponent<AudioListener>();
            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            CameraFollow2D cameraFollow = cameraObject.AddComponent<CameraFollow2D>();
            cameraFollow.Configure(followTarget);
            return camera;
        }

        private static GameObject CreatePlayer(Transform parent)
        {
            GameObject player = CreateChild("Player", parent);
            ConfigurePlayer(player);
            return player;
        }

        private static void ConfigurePlayer(GameObject player)
        {
            player.transform.position = new Vector3(0f, -8f, 0f);

            Rigidbody2D body = GetOrAddComponent<Rigidbody2D>(player);
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CapsuleCollider2D collider = GetOrAddComponent<CapsuleCollider2D>(player);
            collider.direction = CapsuleDirection2D.Horizontal;
            collider.size = new Vector2(1.3f, 0.7f);

            PlayerInput playerInput = GetOrAddComponent<PlayerInput>(player);
            playerInput.actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            playerInput.defaultActionMap = "Player";
            playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

            Transform visualRoot = player.transform.Find("Visual");
            if (visualRoot == null)
            {
                visualRoot = CreateChild("Visual", player.transform).transform;
            }

            visualRoot.localPosition = Vector3.zero;
            CreatePlayerVisual(visualRoot);

            UnderwaterMover2D mover = GetOrAddComponent<UnderwaterMover2D>(player);
            mover.Configure(visualRoot);
        }

        private static void ConfigureCamera(Camera camera, Transform followTarget)
        {
            camera.orthographic = true;
            camera.orthographicSize = 4.5f;

            ExploreCameraPreviewController previewController = camera.GetComponent<ExploreCameraPreviewController>();
            if (previewController != null)
            {
                UnityEngine.Object.DestroyImmediate(previewController);
            }

            CameraFollow2D cameraFollow = GetOrAddComponent<CameraFollow2D>(camera.gameObject);
            cameraFollow.Configure(followTarget);
        }

        private static void CreatePlayerVisual(Transform parent)
        {
            Sprite sprite = GetPrototypeSprite();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");

            Transform existingBody = parent.Find("Body");
            GameObject body = existingBody != null ? existingBody.gameObject : CreateChild("Body", parent);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(1.3f, 0.7f, 1f);
            SpriteRenderer bodyRenderer = GetOrAddComponent<SpriteRenderer>(body);
            bodyRenderer.sprite = sprite;
            bodyRenderer.sharedMaterial = material;
            bodyRenderer.color = new Color(1f, 0.86f, 0.35f, 1f);
            bodyRenderer.sortingLayerName = "World";

            Transform existingMarker = parent.Find("FacingMarker");
            GameObject facingMarker = existingMarker != null ? existingMarker.gameObject : CreateChild("FacingMarker", parent);
            facingMarker.transform.localPosition = new Vector3(0.52f, 0f, 0f);
            facingMarker.transform.localScale = new Vector3(0.2f, 0.22f, 1f);
            SpriteRenderer markerRenderer = GetOrAddComponent<SpriteRenderer>(facingMarker);
            markerRenderer.sprite = sprite;
            markerRenderer.sharedMaterial = material;
            markerRenderer.color = new Color(0.03f, 0.16f, 0.24f, 1f);
            markerRenderer.sortingLayerName = "World";
            markerRenderer.sortingOrder = 1;
        }

        private static Sprite GetPrototypeSprite()
        {
            const string spritePath = "Assets/Sprites/PrototypeSquare.png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sprite != null)
            {
                return sprite;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(spritePath));
            Texture2D texture = new(16, 16, TextureFormat.RGBA32, false);
            texture.SetPixels32(Enumerable.Repeat(new Color32(255, 255, 255, 255), 256).ToArray());
            texture.Apply();
            File.WriteAllBytes(spritePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.Refresh();

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(spritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static Light2D CreateGlobalLight(Transform parent)
        {
            GameObject lightObject = new("Global Light 2D");
            lightObject.transform.SetParent(parent);
            Light2D light = lightObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = new Color(0.78f, 0.96f, 1f, 1f);
            light.intensity = 0.95f;
            return light;
        }

        private static void CreateOceanBackdrop(Transform parent, Camera camera)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader '{ShaderName}' was not imported.");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "UnderwaterDepthBackground" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "OceanBackdrop";
            backdrop.transform.SetParent(parent);
            UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());

            MeshRenderer renderer = backdrop.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingLayerName = "BG_Water";
            renderer.sortingOrder = -1000;

            CameraBackdropFitter fitter = backdrop.AddComponent<CameraBackdropFitter>();
            fitter.Configure(camera);
        }

        private static GameObject CreateRoot(string name)
        {
            return new GameObject(name);
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            GameObject child = new(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            EditorBuildSettingsScene[] currentScenes = EditorBuildSettings.scenes;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) }
                .Concat(currentScenes.Where(scene => scene.path != scenePath))
                .ToArray();
        }

        private static void EnsureSortingLayers(params string[] layerNames)
        {
            UnityEngine.Object tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            SerializedObject serializedTagManager = new(tagManager);
            SerializedProperty layers = serializedTagManager.FindProperty("m_SortingLayers");

            foreach (string layerName in layerNames)
            {
                bool exists = Enumerable.Range(0, layers.arraySize)
                    .Select(layers.GetArrayElementAtIndex)
                    .Any(layer => layer.FindPropertyRelative("name").stringValue == layerName);

                if (exists)
                {
                    continue;
                }

                int index = layers.arraySize;
                layers.InsertArrayElementAtIndex(index);
                SerializedProperty layerProperty = layers.GetArrayElementAtIndex(index);
                layerProperty.FindPropertyRelative("name").stringValue = layerName;
                layerProperty.FindPropertyRelative("uniqueID").longValue = Animator.StringToHash($"LootTheDeep.{layerName}") & 0x7fffffff;
            }

            serializedTagManager.ApplyModifiedProperties();
        }
    }
}
