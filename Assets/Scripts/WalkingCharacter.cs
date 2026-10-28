using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person walker: move the mouse to look, WASD to walk, Shift to run, Space to jump. The pivot is at the feet.
/// It waits until there is ground below it (streamed chunks only appear after Play starts) and is put back on the
/// ground if it ever falls out of the world. Escape frees the cursor, a click captures it again.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class WalkingCharacter : MonoBehaviour
{
    [Tooltip("The camera, a child at eye height; it pitches, the body yaws.")]
    [SerializeField] private Transform view;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 1.5f;
    [SerializeField, Min(0f)] private float runSpeed = 4f;
    [SerializeField, Min(0f)] private float jumpHeight = 0.5f;
    [SerializeField, Min(0f)] private float gravity = 15f;

    [Header("Look")]
    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    [SerializeField, Min(0f)] private float lookSensitivity = 0.1f;
    [SerializeField, Range(0f, 90f)] private float maxPitch = 85f;

    [Header("Ground")]
    [Tooltip("Height the ground is searched from, straight down, when placing the character.")]
    [SerializeField] private float probeHeight = 50f;
    [Tooltip("Below this height the character counts as fallen out of the world and is placed on the ground again.")]
    [SerializeField] private float fallLimit = -10f;

    private CharacterController controller;
    private float pitch;
    private float verticalSpeed;
    private bool placed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        pitch = view != null ? Mathf.DeltaAngle(0f, view.localEulerAngles.x) : 0f;
        controller.enabled = false;
        placed = false;
        SetCursorCaptured(true);
    }

    private void OnDisable()
    {
        SetCursorCaptured(false);
    }

    private void Update()
    {
        if (!placed || transform.position.y < fallLimit)
        {
            placed = TryPlaceOnGround();
            return;
        }
        UpdateCursor();
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            UpdateLook();
        }
        UpdateMovement();
    }

    private bool TryPlaceOnGround()
    {
        controller.enabled = false;
        Vector3 position = transform.position;
        var origin = new Vector3(position.x, probeHeight, position.z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeHeight - fallLimit))
        {
            return false;
        }
        transform.position = hit.point + Vector3.up * 0.05f;
        verticalSpeed = 0f;
        controller.enabled = true;
        return true;
    }

    private void UpdateCursor()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorCaptured(false);
        }
        else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            SetCursorCaptured(true);
        }
    }

    private void UpdateLook()
    {
        Vector2 delta = Mouse.current != null ? Mouse.current.delta.ReadValue() * lookSensitivity : Vector2.zero;
        transform.Rotate(0f, delta.x, 0f);
        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        if (view != null)
        {
            view.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void UpdateMovement()
    {
        Keyboard keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;
        bool run = false, jump = false;
        if (keyboard != null)
        {
            input = new Vector2(Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed), Axis(keyboard.wKey.isPressed, keyboard.sKey.isPressed));
            run = keyboard.shiftKey.isPressed;
            jump = keyboard.spaceKey.wasPressedThisFrame;
        }

        Vector3 horizontal = Vector3.ClampMagnitude(transform.right * input.x + transform.forward * input.y, 1f) * (run ? runSpeed : walkSpeed);
        if (controller.isGrounded)
        {
            // Keep pressing into the ground so walking down slopes doesn't turn into small falls.
            verticalSpeed = jump ? Mathf.Sqrt(2f * gravity * jumpHeight) : -2f;
        }
        verticalSpeed -= gravity * Time.deltaTime;
        controller.Move((horizontal + Vector3.up * verticalSpeed) * Time.deltaTime);
    }

    private static void SetCursorCaptured(bool captured)
    {
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }

    private static float Axis(bool positive, bool negative)
    {
        return (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
