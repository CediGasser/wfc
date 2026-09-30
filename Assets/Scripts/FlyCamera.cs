using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Free-flying camera for looking around a demo scene, controlled like the Scene view:
/// hold the right mouse button to look around, WASD to move, Space/Shift to rise/sink,
/// and scroll (while looking) to change the move speed.
/// Works with both the legacy Input Manager and the Input System package.
/// </summary>
public class FlyCamera : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 10f;
    [SerializeField, Min(0.01f)] private float minMoveSpeed = 0.5f;
    [SerializeField, Min(0.01f)] private float maxMoveSpeed = 200f;
    [Tooltip("Factor the move speed is multiplied/divided by per scroll-wheel notch.")]
    [SerializeField, Min(1f)] private float scrollSpeedFactor = 1.2f;

    [Header("Look")]
    [Tooltip("Degrees of rotation per unit of mouse movement.")]
    [SerializeField, Min(0f)] private float lookSensitivity = 2f;
    [SerializeField, Range(0f, 90f)] private float maxPitch = 89f;

    private float yaw;
    private float pitch;
    private bool isLooking;

    private void OnEnable()
    {
        // Continue from however the camera was placed in the scene.
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = Mathf.DeltaAngle(0f, euler.x);
    }

    private void OnDisable()
    {
        SetLooking(false);
    }

    private void Update()
    {
        UpdateLook();
        UpdateMovement();
    }

    private void UpdateLook()
    {
        bool wantsToLook = IsLookButtonHeld();
        if (wantsToLook != isLooking)
        {
            SetLooking(wantsToLook);
            // Skip the frame the cursor gets locked, its delta can jump when the cursor is recentred.
            return;
        }
        if (!isLooking)
        {
            return;
        }

        Vector2 delta = ReadLookDelta() * lookSensitivity;
        yaw += delta.x;
        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        float scroll = ReadScrollNotches();
        if (scroll != 0f)
        {
            moveSpeed = Mathf.Clamp(moveSpeed * Mathf.Pow(scrollSpeedFactor, scroll), minMoveSpeed, maxMoveSpeed);
        }
    }

    private void UpdateMovement()
    {
        Vector3 input = ReadMoveInput();
        // Horizontal input follows the view direction, vertical input is always world up/down.
        Vector3 direction = transform.right * input.x + transform.forward * input.z + Vector3.up * input.y;
        // Unscaled so the camera still moves while a demo is paused or slowed down via Time.timeScale.
        transform.position += Vector3.ClampMagnitude(direction, 1f) * (moveSpeed * Time.unscaledDeltaTime);
    }

    private void SetLooking(bool looking)
    {
        isLooking = looking;
        Cursor.lockState = looking ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !looking;
    }

    private static float Axis(bool positive, bool negative)
    {
        return (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }

#if ENABLE_INPUT_SYSTEM
    // Matches the default 0.1 sensitivity of the legacy "Mouse X/Y" axes, so both backends feel the same.
    private const float MouseDeltaScale = 0.1f;

    private static bool IsLookButtonHeld()
    {
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
    }

    private static Vector2 ReadLookDelta()
    {
        return Mouse.current != null ? Mouse.current.delta.ReadValue() * MouseDeltaScale : Vector2.zero;
    }

    private static float ReadScrollNotches()
    {
        // Scroll units differ per platform and package version, so cap it at one notch per frame.
        return Mouse.current != null ? Mathf.Clamp(Mouse.current.scroll.ReadValue().y, -1f, 1f) : 0f;
    }

    private static Vector3 ReadMoveInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector3.zero;
        }
        return new Vector3(
            Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed),
            Axis(keyboard.spaceKey.isPressed, keyboard.shiftKey.isPressed),
            Axis(keyboard.wKey.isPressed, keyboard.sKey.isPressed));
    }
#else
    private static bool IsLookButtonHeld()
    {
        return Input.GetMouseButton(1);
    }

    private static Vector2 ReadLookDelta()
    {
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
    }

    private static float ReadScrollNotches()
    {
        return Mathf.Clamp(Input.mouseScrollDelta.y, -1f, 1f);
    }

    private static Vector3 ReadMoveInput()
    {
        return new Vector3(
            Axis(Input.GetKey(KeyCode.D), Input.GetKey(KeyCode.A)),
            Axis(Input.GetKey(KeyCode.Space), Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)),
            Axis(Input.GetKey(KeyCode.W), Input.GetKey(KeyCode.S)));
    }
#endif
}
