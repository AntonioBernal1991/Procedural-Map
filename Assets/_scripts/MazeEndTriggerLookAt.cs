using System.Collections;
using UnityEngine;

/// <summary>
/// Put this on the end-of-maze trigger volume.
/// When the ball enters, it triggers the camera look-at (eye) sequence automatically.
/// </summary>
[DisallowMultipleComponent]
public class MazeEndTriggerLookAt : MonoBehaviour
{
    [Header("References")]
    [Tooltip("If null, will try to find Camera.main and get CameraLookAtOnKey from it.")]
    [SerializeField] private CameraLookAtOnKey cameraLookAt;

    [Header("Trigger Filter")]
    [Tooltip("Optional: only trigger when the entering object is this Transform (or one of its children). " +
             "Useful if the player is the camera or another specific object.")]
    [SerializeField] private Transform requiredRoot;
    [Tooltip("If true and Required Root is empty, only triggers when the entering object belongs to Camera.main.")]
    [SerializeField] private bool requireMainCamera = true;
    [Tooltip("Optional tag filter. Leave empty to ignore tags.")]
    [SerializeField] private string requiredTag = "";

    [Header("Behavior")]
    [Tooltip("Delay before triggering the look-at sequence.")]
    [SerializeField] private float delaySeconds = 0f;
    [Tooltip("If true, this trigger will only fire once.")]
    [SerializeField] private bool triggerOnce = true;
    [Tooltip("If true, disables the camera turning controls (A/D + swipe) when this trigger is hit.")]
    [SerializeField] private bool disableCameraTurningOnTrigger = true;

    private bool _hasTriggered;

    private void Reset()
    {
        // Ensure collider is a trigger if present.
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_hasTriggered && triggerOnce) return;
        if (other == null) return;

        if (requiredRoot != null)
        {
            if (!IsSelfOrChild(other.transform, requiredRoot)) return;
        }
        else if (requireMainCamera)
        {
            Camera c = other.GetComponentInParent<Camera>();
            if (c == null || Camera.main == null || c != Camera.main) return;
        }

        if (!string.IsNullOrWhiteSpace(requiredTag) && !other.CompareTag(requiredTag))
        {
            // Also allow tag on parent (common if collider is a child)
            Transform t = other.transform;
            bool ok = false;
            while (t != null)
            {
                if (t.CompareTag(requiredTag)) { ok = true; break; }
                t = t.parent;
            }
            if (!ok) return;
        }

        _hasTriggered = true;

        if (disableCameraTurningOnTrigger)
        {
            // Disable turning immediately when reaching the end trigger (so input can't fight the look-at).
            Camera cam = Camera.main;
            if (cam != null)
            {
                AutoForwardCameraController c = cam.GetComponent<AutoForwardCameraController>();
                if (c != null) c.SetTurningEnabled(false);
            }
        }

        StartCoroutine(TriggerRoutine());
    }

    private IEnumerator TriggerRoutine()
    {
        if (delaySeconds > 0f)
        {
            yield return new WaitForSeconds(delaySeconds);
        }

        if (cameraLookAt == null)
        {
            Camera cam = Camera.main;
            if (cam != null) cameraLookAt = cam.GetComponent<CameraLookAtOnKey>();
        }

        if (cameraLookAt != null)
        {
            // Decide which UI to show based on music state AT THE MOMENT we cross the trigger,
            // but still activate it only when FOV animation finishes.
            cameraLookAt.CacheMusicStateForFovReachedActivation();
            cameraLookAt.StartLookAtTarget();
        }
        else
        {
            Debug.LogWarning("MazeEndTriggerLookAt: No CameraLookAtOnKey found. Assign it in the Inspector.", this);
        }
    }

    private static bool IsSelfOrChild(Transform candidate, Transform required)
    {
        if (candidate == null || required == null) return false;
        Transform t = candidate;
        while (t != null)
        {
            if (t == required) return true;
            t = t.parent;
        }
        return false;
    }
}

