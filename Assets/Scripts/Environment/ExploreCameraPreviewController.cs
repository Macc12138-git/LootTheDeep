using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace LootTheDeep.Environment
{
    public sealed class ExploreCameraPreviewController : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float moveSpeed = 14f;
        [SerializeField] private Vector2 horizontalRange = new(-30f, 30f);
        [SerializeField] private Vector2 verticalRange = new(-100f, 0f);

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            Vector2 direction = Vector2.zero;
            direction.x = ReadAxis(keyboard.aKey, keyboard.leftArrowKey, keyboard.dKey, keyboard.rightArrowKey);
            direction.y = ReadAxis(keyboard.sKey, keyboard.downArrowKey, keyboard.wKey, keyboard.upArrowKey);

            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            Vector3 position = transform.position;
            position += new Vector3(direction.x, direction.y, 0f) * (moveSpeed * Time.unscaledDeltaTime);
            position.x = Mathf.Clamp(position.x, horizontalRange.x, horizontalRange.y);
            position.y = Mathf.Clamp(position.y, verticalRange.x, verticalRange.y);
            transform.position = position;
        }

        private static float ReadAxis(KeyControl negativePrimary, KeyControl negativeAlternate, KeyControl positivePrimary, KeyControl positiveAlternate)
        {
            float negative = negativePrimary.isPressed || negativeAlternate.isPressed ? 1f : 0f;
            float positive = positivePrimary.isPressed || positiveAlternate.isPressed ? 1f : 0f;
            return positive - negative;
        }
    }
}
