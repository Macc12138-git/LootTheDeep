using UnityEngine;

namespace LootTheDeep.Environment
{
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public sealed class CameraBackdropFitter : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField, Min(1f)] private float coveragePadding = 1.04f;
        [SerializeField] private float distanceFromCamera = 50f;

        public void Configure(Camera cameraToFollow, float padding = 1.04f)
        {
            targetCamera = cameraToFollow;
            coveragePadding = padding;
            FitToCamera();
        }

        public void RefreshNow()
        {
            FitToCamera();
        }

        private void LateUpdate()
        {
            FitToCamera();
        }

        private void FitToCamera()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null || !targetCamera.orthographic)
            {
                return;
            }

            float height = targetCamera.orthographicSize * 2f * coveragePadding;
            float width = height * targetCamera.aspect;

            Vector3 cameraPosition = targetCamera.transform.position;
            transform.position = new Vector3(cameraPosition.x, cameraPosition.y, cameraPosition.z + distanceFromCamera);
            transform.localScale = new Vector3(width, height, 1f);
        }
    }
}
