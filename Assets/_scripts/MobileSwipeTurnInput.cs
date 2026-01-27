using UnityEngine;

/// <summary>
/// Detects left/right swipe gestures and exposes them as one-shot "turn" inputs.
/// Works on mobile (touch) and can optionally be simulated in editor with mouse drag.
/// </summary>
[DisallowMultipleComponent]
public class MobileSwipeTurnInput : MonoBehaviour
{
    [Header("Swipe Detection")]
    [Tooltip("Minimum swipe distance in pixels to count as a turn gesture.")]
    [SerializeField] private float minSwipePixels = 80f;

    [Tooltip("Swipe must be mostly horizontal: |dx| must be >= this * |dy|.")]
    [SerializeField] private float horizontalDominance = 1.5f;

    [Tooltip("Max time (seconds) for a swipe gesture. 0 = no limit.")]
    [SerializeField] private float maxSwipeTime = 0.5f;

    [Header("Editor")]
    [Tooltip("Allow mouse drag in editor to simulate swipe.")]
    [SerializeField] private bool enableMouseInEditor = true;

    private bool _turnLeft;
    private bool _turnRight;

    private Vector2 _startPos;
    private float _startTime;
    private bool _tracking;

    /// <summary>Returns true once per detected left swipe.</summary>
    public bool ConsumeTurnLeft()
    {
        if (!_turnLeft) return false;
        _turnLeft = false;
        return true;
    }

    /// <summary>Returns true once per detected right swipe.</summary>
    public bool ConsumeTurnRight()
    {
        if (!_turnRight) return false;
        _turnRight = false;
        return true;
    }

    private void Update()
    {
        // Touch input (mobile)
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);

            if (t.phase == TouchPhase.Began)
            {
                Begin(t.position);
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                End(t.position);
            }
            return;
        }

#if UNITY_EDITOR
        if (!enableMouseInEditor) return;

        if (Input.GetMouseButtonDown(0))
        {
            Begin(Input.mousePosition);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            End(Input.mousePosition);
        }
#endif
    }

    private void Begin(Vector2 pos)
    {
        _tracking = true;
        _startPos = pos;
        _startTime = Time.unscaledTime;
    }

    private void End(Vector2 pos)
    {
        if (!_tracking) return;
        _tracking = false;

        if (maxSwipeTime > 0f)
        {
            float dt = Time.unscaledTime - _startTime;
            if (dt > maxSwipeTime) return;
        }

        Vector2 delta = pos - _startPos;
        float dx = delta.x;
        float dy = delta.y;

        if (Mathf.Abs(dx) < Mathf.Max(1f, minSwipePixels)) return;

        // Must be mostly horizontal.
        if (Mathf.Abs(dx) < horizontalDominance * Mathf.Abs(dy)) return;

        if (dx < 0f) _turnLeft = true;
        else _turnRight = true;
    }
}

