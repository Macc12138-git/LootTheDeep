using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LootTheDeep.Environment
{
    [ExecuteAlways]
    public sealed class UnderwaterDepthController : MonoBehaviour
    {
        private static readonly int OceanDepthId = Shader.PropertyToID("_OceanDepth01");

        [Header("References")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Light2D globalLight;

        [Header("World Depth")]
        [SerializeField] private float shallowWorldY = 0f;
        [SerializeField] private float deepWorldY = -100f;

        [Header("Lighting")]
        [SerializeField] private Color shallowLightColor = new(0.78f, 0.96f, 1f, 1f);
        [SerializeField] private Color deepLightColor = new(0.22f, 0.42f, 0.62f, 1f);
        [SerializeField, Range(0f, 2f)] private float shallowLightIntensity = 0.95f;
        [SerializeField, Range(0f, 2f)] private float deepLightIntensity = 0.55f;

        public float CurrentDepth01 { get; private set; }

        public void Configure(Camera cameraToTrack, Light2D sceneGlobalLight)
        {
            targetCamera = cameraToTrack;
            globalLight = sceneGlobalLight;
            ApplyDepthState();
        }

        public void RefreshNow()
        {
            ApplyDepthState();
        }

        private void OnEnable()
        {
            ApplyDepthState();
        }

        private void LateUpdate()
        {
            ApplyDepthState();
        }

        private void ApplyDepthState()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null)
            {
                return;
            }

            CurrentDepth01 = Mathf.InverseLerp(shallowWorldY, deepWorldY, targetCamera.transform.position.y);
            Shader.SetGlobalFloat(OceanDepthId, CurrentDepth01);

            if (globalLight != null)
            {
                float smoothDepth = Mathf.SmoothStep(0f, 1f, CurrentDepth01);
                globalLight.color = Color.Lerp(shallowLightColor, deepLightColor, smoothDepth);
                globalLight.intensity = Mathf.Lerp(shallowLightIntensity, deepLightIntensity, smoothDepth);
            }
        }
    }
}
