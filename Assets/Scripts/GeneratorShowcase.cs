using UnityEngine;
using UnityEngine.InputSystem;
using Wfc;
using Wfc.Authoring;

/// <summary>
/// Endless presentation loop for a <see cref="WfcGenerator"/>: on Play, points the camera at the grid and starts an
/// animated generation. Once it succeeds, the result spins around its centre for a while, then everything starts over.
/// A generation that gives up is restarted right away. R restarts at any time.
/// </summary>
public class GeneratorShowcase : MonoBehaviour
{
    private enum Phase
    {
        Generating,
        Spinning,
    }

    [SerializeField] private WfcGenerator generator;
    [Tooltip("Camera to point at the grid on start; defaults to the main camera.")]
    [SerializeField] private Camera targetCamera;

    [Header("Camera")]
    [SerializeField, Range(-89f, 89f)] private float viewPitch = 35f;
    [SerializeField] private float viewYaw = 45f;
    [Tooltip("Scales the camera distance; 1 fits the grid's bounding sphere, which is generous for flat grids.")]
    [SerializeField, Min(0.1f)] private float framingPadding = 1f;

    [Header("Showcase")]
    [Tooltip("Degrees per second around the grid's vertical axis once the generation is done.")]
    [SerializeField] private float spinSpeed = 20f;
    [Tooltip("Seconds the spin takes to reach full speed.")]
    [SerializeField, Min(0f)] private float spinUpTime = 1.5f;
    [Tooltip("Seconds the finished result is shown before the next generation starts.")]
    [SerializeField, Min(0f)] private float showDuration = 10f;

    private Phase phase;
    private float phaseTime;
    private Vector3 initialPosition;
    private Quaternion initialRotation;

    private void Reset()
    {
        generator = FindAnyObjectByType<WfcGenerator>();
    }

    private void Start()
    {
        if (generator == null)
        {
            Debug.LogError($"{name}: assign a generator.", this);
            enabled = false;
            return;
        }
        initialPosition = generator.transform.position;
        initialRotation = generator.transform.rotation;
        FrameCamera();
        Restart();
    }

    private void OnDisable()
    {
        if (generator != null && phase == Phase.Spinning)
        {
            RestorePose();
        }
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            Restart();
            return;
        }

        phaseTime += Time.deltaTime;
        switch (phase)
        {
            case Phase.Generating:
                UpdateGenerating();
                break;
            case Phase.Spinning:
                UpdateSpinning();
                break;
        }
    }

    private void UpdateGenerating()
    {
        if (generator.IsAnimating)
        {
            return;
        }
        if (generator.LastError != null)
        {
            Debug.LogError($"{name}: {generator.LastError}", this);
            enabled = false;
            return;
        }
        if (generator.Solver != null && generator.Solver.Status == SolverStatus.Done)
        {
            phase = Phase.Spinning;
            phaseTime = 0f;
        }
        else
        {
            // Out of attempts: try again with fresh seeds.
            Restart();
        }
    }

    private void UpdateSpinning()
    {
        if (phaseTime >= showDuration)
        {
            Restart();
            return;
        }
        float ramp = spinUpTime > 0f ? Mathf.SmoothStep(0f, 1f, phaseTime / spinUpTime) : 1f;
        generator.transform.RotateAround(GridCenter(), generator.transform.up, spinSpeed * ramp * Time.deltaTime);
    }

    private void Restart()
    {
        RestorePose();
        phase = Phase.Generating;
        phaseTime = 0f;
        generator.Generate(animate: true);
    }

    private void RestorePose()
    {
        generator.transform.SetPositionAndRotation(initialPosition, initialRotation);
    }

    private Vector3 GridCenter()
    {
        Vector3 size = generator.Size;
        Vector3 localCenter = (Vector3.Max(size, Vector3.one) - Vector3.one) * 0.5f;
        return generator.transform.TransformPoint(localCenter);
    }

    private void FrameCamera()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
        {
            return;
        }

        // Fit the grid's bounding sphere, so it stays in view however it's rotated while spinning.
        Vector3 extent = Vector3.Scale(Vector3.Max(generator.Size, Vector3.one), generator.transform.lossyScale);
        float radius = extent.magnitude * 0.5f * framingPadding;
        float verticalHalfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float horizontalHalfFov = Mathf.Atan(Mathf.Tan(verticalHalfFov) * cam.aspect);
        float distance = radius / Mathf.Sin(Mathf.Min(verticalHalfFov, horizontalHalfFov));

        Quaternion rotation = Quaternion.Euler(viewPitch, viewYaw, 0f);
        cam.transform.SetPositionAndRotation(GridCenter() - rotation * Vector3.forward * distance, rotation);

        // The fly camera reads its look angles when enabled; re-enable it so it continues from the new view.
        if (cam.TryGetComponent(out FlyCamera fly) && fly.enabled)
        {
            fly.enabled = false;
            fly.enabled = true;
        }
    }
}
