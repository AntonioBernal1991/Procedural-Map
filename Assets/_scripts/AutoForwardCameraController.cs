using UnityEngine;

/// <summary>
/// Camera-as-player controller:
/// - Moves forward automatically on the XZ plane.
/// - Stops when an obstacle is detected in front (sphere cast).
/// - Allows 90-degree turns with A/D. After turning, auto-walk continues when path is clear.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class AutoForwardCameraController : MonoBehaviour
{
    [Header("Auto Forward")]
    [SerializeField] private float moveSpeed = 3.5f;
    [Tooltip("Extra distance in front of the controller to consider 'blocked'.")]
    [SerializeField] private float stopDistance = 0.15f;
    [Tooltip("Which layers count as walls/obstacles.")]
    [SerializeField] private LayerMask obstacleMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Collision Feel")]
    [Tooltip("Only stop when the obstacle is really in front. Uses dot(hitNormal, forward). -1 = head-on, 0 = side wall.")]
    [SerializeField] [Range(-1f, 0f)] private float stopWhenFacingDotIsAtMost = -0.85f;
    [Tooltip("If the obstacle is somewhat in front (but not head-on), slide along it instead of stopping. " +
             "Uses dot(hitNormal, forward). Values closer to 0 mean more permissive sliding.")]
    [SerializeField] [Range(-1f, 0f)] private float slideWhenFacingDotIsAtMost = -0.2f;

    [Header("Turning")]
    [Tooltip("Seconds to complete a 90-degree turn. 0 = instant.")]
    [SerializeField] private float turnDuration = 0.12f;

    [Header("Turning Control")]
    [Tooltip("Master toggle for turning input (A/D + swipe). Useful for cutscenes/end triggers.")]
    [SerializeField] private bool turningEnabled = true;

    [Header("Keys / Inventory")]
    [Tooltip("Runtime flag: set to true when the player picks up a key.")]
    [SerializeField] private bool hasKey = false;
    [Tooltip("If true, shows the LockedCube prompt only when the player does NOT have a key (typical 'need key' UX).")]
    [SerializeField] private bool showLockedPromptOnlyWhenNoKey = true;

    [Header("Mobile Turning (Swipe)")]
    [Tooltip("Enable swipe left/right as an alternative to A/D for turning 90 degrees.")]
    [SerializeField] private bool enableSwipeTurns = true;

    [Tooltip("Minimum swipe distance in pixels to count as a turn gesture.")]
    [SerializeField] private float swipeMinPixels = 80f;

    [Tooltip("Swipe must be mostly horizontal: |dx| must be >= this * |dy|.")]
    [SerializeField] private float swipeHorizontalDominance = 1.5f;

    [Tooltip("Max time (seconds) for a swipe gesture. 0 = no limit.")]
    [SerializeField] private float swipeMaxTime = 0.5f;

    [Tooltip("Allow mouse drag in editor to simulate swipe.")]
    [SerializeField] private bool swipeEnableMouseInEditor = true;

    [Header("Debug")]
    [SerializeField] private bool debugDraw = false;

    [Header("Rendering (camera)")]
    [Tooltip("Disables Camera Occlusion Culling at runtime. Recommended for procedural/dynamic levels with baked occlusion data, to avoid pop-in holes.")]
    [SerializeField] private bool disableOcclusionCulling = true;

    private CharacterController _cc;
    private bool _isTurning;
    private Quaternion _turnFrom;
    private Quaternion _turnTo;
    private float _turnT;

    // Swipe tracking (internal, so no extra component is required)
    private bool _swipeTracking;
    private Vector2 _swipeStartPos;
    private float _swipeStartTime;
    private bool _swipeTurnLeft;
    private bool _swipeTurnRight;

    private LockedCube _lockedPromptTarget;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();

        if (disableOcclusionCulling)
        {
            Camera cam = GetComponent<Camera>();
            if (cam != null) cam.useOcclusionCulling = false;
        }
    }

    public bool HasKey => hasKey;

    public void GiveKey()
    {
        hasKey = true;
    }

    public bool ConsumeKey()
    {
        if (!hasKey) return false;
        hasKey = false;
        return true;
    }

    private void Update()
    {
        HandleTurningInput();
        TickTurn(Time.deltaTime);
        TickMove(Time.deltaTime);
    }

    private void HandleTurningInput()
    {
        if (!turningEnabled)
        {
            // Ensure we don't carry pending swipe/turn state while disabled.
            _swipeTracking = false;
            _swipeTurnLeft = false;
            _swipeTurnRight = false;
            return;
        }
        if (_isTurning) return;

        TickSwipeInput();

        bool turnLeft = Input.GetKeyDown(KeyCode.A) || ConsumeSwipeLeft();
        bool turnRight = Input.GetKeyDown(KeyCode.D) || ConsumeSwipeRight();

        if (turnLeft)
        {
            StartTurn(-90f);
        }
        else if (turnRight)
        {
            StartTurn(90f);
        }
    }

    private bool ConsumeSwipeLeft()
    {
        if (!_swipeTurnLeft) return false;
        _swipeTurnLeft = false;
        return true;
    }

    private bool ConsumeSwipeRight()
    {
        if (!_swipeTurnRight) return false;
        _swipeTurnRight = false;
        return true;
    }

    private void TickSwipeInput()
    {
        if (!enableSwipeTurns) return;

        // Touch input (mobile)
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                SwipeBegin(t.position);
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                SwipeEnd(t.position);
            }
            return;
        }

#if UNITY_EDITOR
        if (!swipeEnableMouseInEditor) return;
        if (Input.GetMouseButtonDown(0))
        {
            SwipeBegin(Input.mousePosition);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            SwipeEnd(Input.mousePosition);
        }
#endif
    }

    private void SwipeBegin(Vector2 pos)
    {
        _swipeTracking = true;
        _swipeStartPos = pos;
        _swipeStartTime = Time.unscaledTime;
    }

    private void SwipeEnd(Vector2 pos)
    {
        if (!_swipeTracking) return;
        _swipeTracking = false;

        if (swipeMaxTime > 0f)
        {
            float dt = Time.unscaledTime - _swipeStartTime;
            if (dt > swipeMaxTime) return;
        }

        Vector2 delta = pos - _swipeStartPos;
        float dx = delta.x;
        float dy = delta.y;

        if (Mathf.Abs(dx) < Mathf.Max(1f, swipeMinPixels)) return;
        if (Mathf.Abs(dx) < swipeHorizontalDominance * Mathf.Abs(dy)) return;

        if (dx < 0f) _swipeTurnLeft = true;
        else _swipeTurnRight = true;
    }

    private void StartTurn(float deltaYawDegrees)
    {
        _isTurning = true;
        _turnT = 0f;
        _turnFrom = transform.rotation;
        _turnTo = Quaternion.Euler(0f, deltaYawDegrees, 0f) * _turnFrom;

        if (turnDuration <= 0f)
        {
            transform.rotation = _turnTo;
            _isTurning = false;
        }
    }

    /// <summary>
    /// Enable/disable turning input at runtime (A/D + swipe). Intended for end triggers / cutscenes.
    /// </summary>
    public void SetTurningEnabled(bool enabled, bool cancelCurrentTurn = true)
    {
        turningEnabled = enabled;
        _swipeTracking = false;
        _swipeTurnLeft = false;
        _swipeTurnRight = false;

        if (!enabled && cancelCurrentTurn)
        {
            _isTurning = false;
        }
    }

    private void TickTurn(float dt)
    {
        if (!_isTurning) return;
        if (turnDuration <= 0f) return;

        _turnT += dt / Mathf.Max(0.0001f, turnDuration);
        float t = Mathf.Clamp01(_turnT);
        transform.rotation = Quaternion.Slerp(_turnFrom, _turnTo, t);

        if (t >= 1f)
        {
            _isTurning = false;
        }
    }

    private void TickMove(float dt)
    {
        if (_isTurning) return;
        if (_cc == null) return;
        if (moveSpeed <= 0f) return;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return;
        forward.Normalize();

        Vector3 moveDir = GetMoveDirection(forward);
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            _cc.Move(moveDir * (moveSpeed * dt));
        }
    }

    private Vector3 GetMoveDirection(Vector3 planarForward)
    {
        // Use a CapsuleCast that matches the CharacterController shape (more reliable than a simple sphere cast),
        // and ignore side-wall touches so we only stop when something is actually in front.
        Vector3 center = transform.TransformPoint(_cc.center);
        float skin = Mathf.Max(0f, _cc.skinWidth);
        float radius = Mathf.Max(0.01f, (_cc.radius - skin) * 0.95f);
        float height = Mathf.Max(_cc.height, radius * 2f);
        float half = height * 0.5f;
        float capOffset = Mathf.Max(0f, half - radius);

        Vector3 up = Vector3.up;
        Vector3 p1 = center + up * capOffset;
        Vector3 p2 = center - up * capOffset;

        // stopDistance is the desired "gap" from the capsule surface to the wall, so cast forward by that gap (+skin).
        float dist = Mathf.Max(0f, stopDistance) + skin + 0.02f;

        if (debugDraw)
        {
            Debug.DrawLine(center, center + planarForward * (dist + radius), Color.yellow);
        }

        bool hit = Physics.CapsuleCast(p1, p2, radius, planarForward, out RaycastHit info, dist, obstacleMask, triggerInteraction);
        if (!hit)
        {
            UpdateLockedPrompt(null, false);
            return planarForward;
        }

        // Ignore ground-ish hits: if the normal is strongly upward, it's probably the floor edge.
        if (Vector3.Dot(info.normal, Vector3.up) > 0.75f)
        {
            UpdateLockedPrompt(null, false);
            return planarForward;
        }

        // For a wall directly in front, dot(normal, forward) ~ -1. For side wall, ~0.
        float facing = Vector3.Dot(info.normal, planarForward);

        // Proximity prompt for locked cubes: show/hide based on cast (works even if we stop before triggers).
        LockedCube locked = info.collider != null ? info.collider.GetComponentInParent<LockedCube>() : null;
        bool shouldShowPrompt = locked != null && !locked.IsUnlocked;
        if (shouldShowPrompt && showLockedPromptOnlyWhenNoKey && hasKey) shouldShowPrompt = false;
        // Only show when it's not a pure side graze (same rule we use for movement logic).
        if (shouldShowPrompt && facing > slideWhenFacingDotIsAtMost) shouldShowPrompt = false;
        UpdateLockedPrompt(locked, shouldShowPrompt);

        // If we hit a locked cube and we have a key, unlock it and keep moving.
        if (locked != null && hasKey)
        {
            if (locked.TryUnlock(this))
            {
                UpdateLockedPrompt(locked, false);
                return planarForward;
            }
        }

        // Completely ignore side-ish contacts.
        if (facing > slideWhenFacingDotIsAtMost) return planarForward;

        // Stop only when it's really head-on.
        if (facing <= stopWhenFacingDotIsAtMost) return Vector3.zero;

        // Otherwise, it's a "grazing" hit: slide along the wall instead of stopping.
        Vector3 slide = Vector3.ProjectOnPlane(planarForward, info.normal);
        slide.y = 0f;
        if (slide.sqrMagnitude < 0.0001f) return Vector3.zero;
        return slide.normalized;
    }

    private void UpdateLockedPrompt(LockedCube locked, bool active)
    {
        if (_lockedPromptTarget != null && _lockedPromptTarget != locked)
        {
            _lockedPromptTarget.SetProximityPromptActive(false);
        }

        _lockedPromptTarget = locked;

        if (_lockedPromptTarget != null)
        {
            _lockedPromptTarget.SetProximityPromptActive(active);
        }
    }
}

