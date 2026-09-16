using UnityEngine;

namespace LootTheDeep.Gameplay
{
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow2D : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Min(0f)] private float smoothTime = 0.18f;
        [SerializeField, Min(0f)] private float lookAheadDistance = 1f;
        [SerializeField, Min(0f)] private float lookAheadResponsiveness = 4f;
        [SerializeField] private Vector2 horizontalRange = new(-30f, 30f);
        [SerializeField] private float maximumWorldY = 0f;

        private Camera targetCamera;
        private Rigidbody2D targetBody;
        private Vector3 smoothVelocity;
        private Vector2 currentLookAhead;

        public void Configure(Transform followTarget)
        {
            target = followTarget;
            CacheTargetBody();
            SnapToTarget();
        }

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            CacheTargetBody();
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector2 desiredLookAhead = Vector2.zero;
            if (targetBody != null && targetBody.linearVelocity.sqrMagnitude > 0.01f)
            {
                desiredLookAhead = targetBody.linearVelocity.normalized * lookAheadDistance;
            }

            currentLookAhead = Vector2.Lerp(
                currentLookAhead,
                desiredLookAhead,
                1f - Mathf.Exp(-lookAheadResponsiveness * Time.deltaTime));

            Vector3 desiredPosition = ClampToActionBounds((Vector2)target.position + currentLookAhead);
            desiredPosition.z = transform.position.z;
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref smoothVelocity, smoothTime);
        }

        private void CacheTargetBody()
        {
            targetBody = target != null ? target.GetComponent<Rigidbody2D>() : null;
        }

        private void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            Vector3 position = ClampToActionBounds(target.position);
            position.z = transform.position.z;
            transform.position = position;
            smoothVelocity = Vector3.zero;
        }

        private Vector3 ClampToActionBounds(Vector2 desiredPosition)
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            float halfHeight = targetCamera.orthographicSize;
            float halfWidth = halfHeight * targetCamera.aspect;

            return new Vector3(
                ClampAxis(desiredPosition.x, horizontalRange.x + halfWidth, horizontalRange.y - halfWidth),
                Mathf.Min(desiredPosition.y, maximumWorldY - halfHeight),
                transform.position.z);
        }

        private static float ClampAxis(float value, float minimum, float maximum)
        {
            return minimum <= maximum ? Mathf.Clamp(value, minimum, maximum) : (minimum + maximum) * 0.5f;
        }
    }
}
