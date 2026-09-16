using UnityEngine;
using UnityEngine.InputSystem;

namespace LootTheDeep.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CapsuleCollider2D))]
    [RequireComponent(typeof(PlayerInput))]
    public sealed class UnderwaterMover2D : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 4.5f;
        [SerializeField, Min(0f)] private float acceleration = 14f;
        [SerializeField, Min(0f)] private float deceleration = 18f;

        [Header("Action Bounds")]
        [SerializeField] private Vector2 horizontalRange = new(-30f, 30f);
        [SerializeField] private float maximumWorldY = 0f;

        [Header("Presentation")]
        [SerializeField] private Transform visualRoot;

        private Rigidbody2D body;
        private PlayerInput playerInput;
        private InputAction moveAction;
        private Vector2 moveInput;
        private bool inventoryOpen;

        public void SetInventoryOpen(bool value)
        {
            inventoryOpen = value;
            moveInput = Vector2.zero;
            if (body != null) body.linearVelocity = Vector2.zero;
        }

        public void Configure(Transform objectVisual)
        {
            visualRoot = objectVisual;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            playerInput = GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            moveAction = playerInput.actions?.FindAction("Player/Move", false);

            if (moveAction == null)
            {
                Debug.LogError("Player/Move input action is required for underwater movement.", this);
            }
        }

        private void Update()
        {
            if (inventoryOpen) return;
            moveInput = moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
            moveInput = Vector2.ClampMagnitude(moveInput, 1f);

            if (visualRoot != null && Mathf.Abs(moveInput.x) > 0.01f)
            {
                Vector3 scale = visualRoot.localScale;
                scale.x = Mathf.Abs(scale.x) * Mathf.Sign(moveInput.x);
                visualRoot.localScale = scale;
            }
        }

        private void FixedUpdate()
        {
            if (inventoryOpen) { body.linearVelocity = Vector2.zero; return; }
            Vector2 targetVelocity = moveInput * moveSpeed;
            float changeRate = moveInput.sqrMagnitude > 0.001f ? acceleration : deceleration;
            Vector2 velocity = Vector2.MoveTowards(body.linearVelocity, targetVelocity, changeRate * Time.fixedDeltaTime);

            Vector2 projectedPosition = body.position + velocity * Time.fixedDeltaTime;
            Vector2 clampedPosition = new(
                Mathf.Clamp(projectedPosition.x, horizontalRange.x, horizontalRange.y),
                Mathf.Min(projectedPosition.y, maximumWorldY));

            body.linearVelocity = (clampedPosition - body.position) / Time.fixedDeltaTime;
        }
    }
}
