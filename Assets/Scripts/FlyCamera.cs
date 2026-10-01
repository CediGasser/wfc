using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Free-flying camera for looking around a demo scene, controlled like the Scene view:
/// hold the right mouse button to look around, WASD to move, Space/Shift to rise/sink,
/// and scroll (while looking) to change the move speed.
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
    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    [SerializeField, Min(0f)] private float lookSensitivity = 0.2f;
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

    private static bool IsLookButtonHeld()
    {
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
    }

    private static Vector2 ReadLookDelta()
    {
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
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
}
