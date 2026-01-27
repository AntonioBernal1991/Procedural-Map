using UnityEngine;

/// <summary>
/// "Character-like" tank controls for a ball:
/// - W/S: move forward/back in facing direction
/// - A/D: rotate left/right (yaw)
/// Additionally, enforces rolling spin while grounded so it looks/feels like a real ball (no sliding).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BallController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveAcceleration = 25f;
    [SerializeField] private float maxSpeed = 8f;
    [SerializeField] private bool invertForward = true;
    [SerializeField] private bool invertStrafe = true;

    [Header("Rotation")]
    [SerializeField] private float turnSpeedDegreesPerSecond = 180f;
    [Tooltip("Use Q/E to rotate the heading. If false, the ball won't turn from input (still rolls/strafe).")]
    [SerializeField] private bool enableTurnWithQE = true;
    [Tooltip("If true, lock yaw (Y) rotation so the ball keeps looking the same direction, but can still roll (X/Z).")]
    [SerializeField] private bool lockYawRotation = true;

    [Header("Rolling")]
    [Tooltip("How quickly we correct spin to match forward speed (rad/s^2).")]
    [SerializeField] private float rollAngularAccel = 50f;
    [Tooltip("Stylized effect: rotate opposite to the movement direction (backspin), like a bowling ball sliding.")]
    [SerializeField] private bool stylizedBackspin = true;
    [Tooltip("Backspin intensity multiplier. 1 = same magnitude as perfect rolling but reversed.")]
    [SerializeField] private float backspinMultiplier = 1.0f;
    [Tooltip("Ground check distance.")]
    [SerializeField] private float groundCheckExtra = 0.05f;
    [Tooltip("Optional: if you also use WASD to tilt the maze, hold Shift for maze tilt. Ball ignores input while Shift is held.")]
    [SerializeField] private bool ignoreInputWhileShiftHeld = true;

    private Rigidbody _rb;
    private float _radius = 0.5f;
    private float _headingYaw;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _headingYaw = transform.eulerAngles.y;
        
        if (lockYawRotation)
        {
            _rb.constraints |= RigidbodyConstraints.FreezeRotationY;
            enableTurnWithQE = false; // turning would fight the constraint
        }
        SphereCollider sc = GetComponent<SphereCollider>();
        if (sc != null)
        {
            // approximate world radius
            float maxScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
            _radius = sc.radius * maxScale;
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (ignoreInputWhileShiftHeld && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
        {
            return;
        }

        // Rotation (Q/E) - optional (keeps WASD free for movement).
        if (enableTurnWithQE)
        {
            float turn = 0f;
            if (Input.GetKey(KeyCode.Q)) turn -= 1f;
            if (Input.GetKey(KeyCode.E)) turn += 1f;
            if (!Mathf.Approximately(turn, 0f))
            {
                _headingYaw += turn * turnSpeedDegreesPerSecond * dt;
            }
        }

        // Movement (W/S)
        float forwardAxis = 0f;
        if (Input.GetKey(KeyCode.W)) forwardAxis += 1f;
        if (Input.GetKey(KeyCode.S)) forwardAxis -= 1f;
        if (invertForward) forwardAxis = -forwardAxis;

        // Strafe (A/D)
        float strafeAxis = 0f;
        if (Input.GetKey(KeyCode.A)) strafeAxis -= 1f;
        if (Input.GetKey(KeyCode.D)) strafeAxis += 1f;
        if (invertStrafe) strafeAxis = -strafeAxis;

        Quaternion headingRot = Quaternion.Euler(0f, _headingYaw, 0f);
        Vector3 forward = headingRot * Vector3.forward;
        Vector3 right = headingRot * Vector3.right;

        Vector3 desiredDir = (forward * forwardAxis + right * strafeAxis);
        if (desiredDir.sqrMagnitude > 0.0001f)
        {
            desiredDir.Normalize();
            _rb.AddForce(desiredDir * moveAcceleration, ForceMode.Acceleration);
        }

        // Clamp horizontal speed
        Vector3 vel = _rb.velocity;
        Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
        if (horizontal.magnitude > maxSpeed)
        {
            Vector3 clamped = horizontal.normalized * maxSpeed;
            _rb.velocity = new Vector3(clamped.x, vel.y, clamped.z);
        }

        // Enforce rolling spin when grounded so it behaves/looks like a rolling ball.
        if (_radius > 0.0001f && IsGrounded())
        {
            Vector3 planarVel = Vector3.ProjectOnPlane(_rb.velocity, Vector3.up);
            if (planarVel.sqrMagnitude < 0.0025f) return;

            // Rolling without slipping: v = ω × r  =>  ω = (n × v) / r, where n is the ground normal (~up).
            Vector3 desiredAngVel = Vector3.Cross(Vector3.up, planarVel) / _radius; // rad/s
            if (stylizedBackspin)
            {
                desiredAngVel = -desiredAngVel * Mathf.Max(0f, backspinMultiplier);
            }

            Vector3 currentAngVel = _rb.angularVelocity;
            Vector3 targetAngVel = new Vector3(desiredAngVel.x, currentAngVel.y, desiredAngVel.z);

            float maxDelta = rollAngularAccel * dt;
            _rb.angularVelocity = Vector3.MoveTowards(currentAngVel, targetAngVel, maxDelta);
        }
    }

    private bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.02f;
        float dist = (_radius > 0.0001f ? _radius : 0.5f) + groundCheckExtra;
        return Physics.SphereCast(origin, (_radius > 0.0001f ? _radius : 0.5f) * 0.95f, Vector3.down, out _, dist);
    }
}

