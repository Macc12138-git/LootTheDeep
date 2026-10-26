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
        [SerializeField] private bool instantMovement;
        [SerializeField, Min(0f)] private float acceleration = 14f;
        [SerializeField, Min(0f)] private float deceleration = 18f;

        [Header("Action Bounds")]
        [SerializeField] private Vector2 horizontalRange = new(-30f, 30f);
        [SerializeField] private float maximumWorldY = 0f;

        [Header("Presentation")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;
        [SerializeField] private bool faceAimDirection;

        private static readonly int IsMovingParameter = Animator.StringToHash("IsMoving");

        private Rigidbody2D body;
        private PlayerInput playerInput;
        private InputAction moveAction;
        private Vector2 moveInput;
        private bool inventoryOpen;
        private bool combatEnded;

        public void SetCombatEnded(bool value)
        {
            combatEnded = value;
            moveInput = Vector2.zero;
            if (body != null) body.linearVelocity = Vector2.zero;
            RefreshAnimation();
        }

        public void SetInventoryOpen(bool value)
        {
            inventoryOpen = value;
            moveInput = Vector2.zero;
            if (body != null) body.linearVelocity = Vector2.zero;
            RefreshAnimation();
        }

        public void Configure(Transform objectVisual)
        {
            visualRoot = objectVisual;
        }

        public void SetAimDirection(Vector2 direction)
        {
            if (faceAimDirection) RefreshFacing(direction.x);
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
            moveInput = inventoryOpen || combatEnded
                ? Vector2.zero
                : moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
            moveInput = Vector2.ClampMagnitude(moveInput, 1f);
            RefreshAnimation();

            if (!faceAimDirection) RefreshFacing(moveInput.x);
        }

        private void RefreshFacing(float horizontalDirection)
        {
            if (visualRoot == null || Mathf.Abs(horizontalDirection) <= 0.01f) return;
            Vector3 scale = visualRoot.localScale;
            scale.x = Mathf.Abs(scale.x) * Mathf.Sign(horizontalDirection);
            visualRoot.localScale = scale;
        }

        private void OnDisable()
        {
            moveInput = Vector2.zero;
            RefreshAnimation();
        }

        private void RefreshAnimation()
        {
            if (animator != null)
                animator.SetBool(IsMovingParameter, moveInput.sqrMagnitude > 0.001f);
        }

        private void FixedUpdate()
        {
            if (inventoryOpen || combatEnded) { body.linearVelocity = Vector2.zero; return; }
            Vector2 targetVelocity = moveInput * moveSpeed;
            float changeRate = moveInput.sqrMagnitude > 0.001f ? acceleration : deceleration;
            Vector2 velocity = instantMovement
                ? targetVelocity
                : Vector2.MoveTowards(body.linearVelocity, targetVelocity, changeRate * Time.fixedDeltaTime);

            Vector2 projectedPosition = body.position + velocity * Time.fixedDeltaTime;
            Vector2 clampedPosition = new(
                Mathf.Clamp(projectedPosition.x, horizontalRange.x, horizontalRange.y),
                Mathf.Min(projectedPosition.y, maximumWorldY));

            body.linearVelocity = (clampedPosition - body.position) / Time.fixedDeltaTime;
        }
    }
}
