using UnityEngine;

/// <summary>
/// Makes an eye (or any object) rotate to look at the player camera.
/// Attach this to the Eye object (or the eye bone/pivot).
/// </summary>
[DisallowMultipleComponent]
public class EyeLookAtPlayer : MonoBehaviour
{
    [Tooltip("Optional explicit target. If null, uses Camera.main.")]
    [SerializeField] private Transform target;

    [Tooltip("If true, only rotate around Y (keeps upright). If false, full look rotation.")]
    [SerializeField] private bool yOnly = false;

    [Tooltip("Local forward axis correction (if your eye mesh looks sideways).")]
    [SerializeField] private Vector3 localForwardAxis = Vector3.forward;

    [Tooltip("Use LateUpdate so it follows after camera movement.")]
    [SerializeField] private bool useLateUpdate = true;

    private void Update()
    {
        if (!useLateUpdate) Tick();
    }

    private void LateUpdate()
    {
        if (useLateUpdate) Tick();
    }

    private void Tick()
    {
        Transform t = target;
        if (t == null)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            t = cam.transform;
        }

        Vector3 toTarget = t.position - transform.position;
        if (yOnly) toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.000001f) return;

        // LookRotation assumes object's forward is Vector3.forward.
        // If the mesh uses a different local axis, rotate from that axis to the desired forward.
        Quaternion look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        Quaternion axisFix = Quaternion.FromToRotation(localForwardAxis.normalized, Vector3.forward);
        transform.rotation = look * axisFix;
    }
}

