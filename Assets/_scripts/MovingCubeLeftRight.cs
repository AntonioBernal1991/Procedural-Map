using UnityEngine;

/// <summary>
/// Simple left-right (or any axis) mover for cubes:
/// - Moves constantly along an axis.
/// - When it hits another collider (collision or trigger), it reverses direction.
///
/// Recommended setup:
/// - This GameObject: Collider + Rigidbody (isKinematic = true).
/// - Obstacles: Collider (static is fine).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class MovingCubeLeftRight : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Units per second.")]
    [SerializeField] private float speed = 2f;

    [Tooltip("Movement axis (X = left/right). If Use Local Space is true, this is local to the cube.")]
    [SerializeField] private Vector3 axis = Vector3.right;

    [Tooltip("If true, axis is interpreted in local space (so rotating the cube rotates the movement direction).")]
    [SerializeField] private bool useLocalSpace = false;

    [Tooltip("Start moving in the negative axis direction (left if axis = Vector3.right).")]
    [SerializeField] private bool startNegative = false;

    [Header("Reverse Conditions")]
    [Tooltip("Only reverse when colliding with these layers. Default = Everything.")]
    [SerializeField] private LayerMask reverseOnLayers = ~0;

    [Tooltip("Optional: only reverse when the other object (or its parent) has this tag. Empty = ignore tags.")]
    [SerializeField] private string requiredTag = "";

    [Tooltip("Minimum seconds between direction flips to avoid jitter when staying in contact.")]
    [SerializeField] [Min(0f)] private float minFlipInterval = 0.05f;

    [Tooltip("Optional small push away from obstacle after reversing (helps avoid sticking).")]
    [SerializeField] [Min(0f)] private float separationDistance = 0.01f;

    [Header("Detection")]
    [Tooltip("If true, uses Rigidbody.SweepTest each FixedUpdate to detect an upcoming hit and reverse reliably (recommended).")]
    [SerializeField] private bool useSweepTest = true;
    [Tooltip("Extra distance added to the sweep test (helps when frame rate varies).")]
    [SerializeField] [Min(0f)] private float sweepExtraDistance = 0.02f;

    [Header("Debug")]
    [SerializeField] private bool debugDraw = false;

    private Rigidbody _rb;
    private Collider _col;
    private int _sign = 1;
    private float _lastFlipTime = -999f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _sign = startNegative ? -1 : 1;

        // Safe defaults for movers.
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        if (_rb == null) return;
        if (speed <= 0f) return;

        Vector3 dir = GetWorldAxis().normalized * _sign;
        float dt = Time.fixedDeltaTime;
        Vector3 delta = dir * (speed * dt);

        if (useSweepTest && IsFlipAllowed() && WouldHitBlocking(dir, delta.magnitude + sweepExtraDistance, out RaycastHit hit))
        {
            if (debugDraw)
            {
                Debug.DrawLine(_rb.position, hit.point, Color.red, 0f, false);
            }
            ReverseAndSeparate(null);
        }

        if (debugDraw)
        {
            Debug.DrawLine(_rb.position, _rb.position + dir * 0.5f, Color.cyan, 0f, false);
        }

        _rb.MovePosition(_rb.position + delta);
    }

    private Vector3 GetWorldAxis()
    {
        Vector3 a = axis.sqrMagnitude < 0.0001f ? Vector3.right : axis;
        return useLocalSpace ? transform.TransformDirection(a) : a;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null) return;
        if (!ShouldConsider(collision.gameObject)) return;
        if (!IsFlipAllowed()) return;

        // Reverse only if the contact is blocking our current movement (prevents reversing on floor contact).
        Vector3 dir = GetWorldAxis().normalized * _sign;
        bool blocking = false;
        var contacts = collision.contacts;
        for (int i = 0; i < contacts.Length; i++)
        {
            Vector3 n = contacts[i].normal;
            // If normal faces against our movement, dot is negative.
            if (Vector3.Dot(n, dir) < -0.1f) { blocking = true; break; }
        }

        if (!blocking) return;
        ReverseAndSeparate(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        // If we start already touching, Enter may not fire. Stay keeps it reliable.
        if (collision == null) return;
        if (!ShouldConsider(collision.gameObject)) return;
        if (!IsFlipAllowed()) return;

        Vector3 planarDir = GetWorldAxis().normalized * _sign;
        bool blocking = false;
        var contacts = collision.contacts;
        for (int i = 0; i < contacts.Length; i++)
        {
            if (Vector3.Dot(contacts[i].normal, planarDir) < -0.1f) { blocking = true; break; }
        }
        if (!blocking) return;
        ReverseAndSeparate(collision);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (!ShouldConsider(other.gameObject)) return;
        if (!IsFlipAllowed()) return;
        ReverseAndSeparate(null);
    }

    private void OnTriggerStay(Collider other)
    {
        if (other == null) return;
        if (!ShouldConsider(other.gameObject)) return;
        if (!IsFlipAllowed()) return;
        ReverseAndSeparate(null);
    }

    private bool ShouldConsider(GameObject otherGo)
    {
        if (otherGo == null) return false;

        // Layer filter
        int otherLayer = otherGo.layer;
        if (((1 << otherLayer) & reverseOnLayers.value) == 0) return false;

        // Optional tag filter (also allow tag on parents)
        if (!string.IsNullOrWhiteSpace(requiredTag))
        {
            Transform t = otherGo.transform;
            while (t != null)
            {
                if (t.CompareTag(requiredTag)) return true;
                t = t.parent;
            }
            return false;
        }

        return true;
    }

    private bool WouldHitBlocking(Vector3 dir, float distance, out RaycastHit hit)
    {
        hit = default;
        if (_rb == null) return false;
        if (_col == null) _col = GetComponent<Collider>();
        if (_col == null) return false;

        // Sweep the rigidbody's collider shape forward.
        if (!_rb.SweepTest(dir, out hit, Mathf.Max(0f, distance), QueryTriggerInteraction.Collide)) return false;
        if (hit.collider == null) return false;
        if (!ShouldConsider(hit.collider.gameObject)) return false;

        // Avoid reversing on floor contacts.
        if (Vector3.Dot(hit.normal, dir) > -0.1f) return false;
        return true;
    }

    private bool IsFlipAllowed()
    {
        return Time.time - _lastFlipTime >= minFlipInterval;
    }

    private void ReverseAndSeparate(Collision collision)
    {
        _sign *= -1;
        _lastFlipTime = Time.time;

        if (separationDistance <= 0f) return;

        Vector3 pushDir;
        if (collision != null && collision.contactCount > 0)
        {
            // Push away from average contact normal.
            Vector3 n = Vector3.zero;
            var contacts = collision.contacts;
            for (int i = 0; i < contacts.Length; i++) n += contacts[i].normal;
            pushDir = n.sqrMagnitude < 0.0001f ? -GetWorldAxis().normalized : n.normalized;
        }
        else
        {
            pushDir = -GetWorldAxis().normalized * _sign;
        }

        _rb.position += pushDir * separationDistance;
    }
}

