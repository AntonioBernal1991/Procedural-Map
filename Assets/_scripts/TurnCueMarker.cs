using UnityEngine;

/// <summary>
/// Attach to a cube (typically a module's central cube) to declare it as a "turn cue" marker.
/// You can enable/disable (or add/remove) this component manually per module.
///
/// Optionally auto-adds a small trigger collider so the player camera can detect it without any per-frame scanning.
/// </summary>
[DisallowMultipleComponent]
public class TurnCueMarker : MonoBehaviour
{
    public enum MusicPanMode
    {
        Center = 0,
        Left = 1,
        Right = 2,
        Custom = 3
    }

    [Header("Trigger (optional)")]
    [SerializeField] private bool addTriggerCollider = true;
    [SerializeField] private float triggerRadius = 0.65f;
    [SerializeField] private Vector3 triggerCenter = new Vector3(0f, 0.5f, 0f);

    [Header("Behavior")]
    [Tooltip("If true, the receiver should treat this as one-shot (only trigger once).")]
    [SerializeField] private bool oneShot = false;

    [Header("Music (optional)")]
    [Tooltip("Stereo pan to apply to BackgroundMusicPlayer when the camera enters this cue.")]
    [SerializeField] private MusicPanMode musicPan = MusicPanMode.Center;
    [Tooltip("Only used when Music Pan = Custom. -1 = full left, +1 = full right.")]
    [SerializeField] [Range(-1f, 1f)] private float customPanStereo = 0f;
    [Tooltip("If true, restore the BackgroundMusicPlayer pan when the camera exits this cue.")]
    [SerializeField] private bool resetMusicPanOnExit = true;

    public bool OneShot => oneShot;
    public bool ResetMusicPanOnExit => resetMusicPanOnExit;
    public float MusicPanStereo
    {
        get
        {
            switch (musicPan)
            {
                case MusicPanMode.Left: return -1f;
                case MusicPanMode.Right: return 1f;
                case MusicPanMode.Custom: return Mathf.Clamp(customPanStereo, -1f, 1f);
                default: return 0f;
            }
        }
    }

    private void Reset()
    {
        EnsureTrigger();
    }

    private void OnValidate()
    {
        if (!addTriggerCollider) return;
        EnsureTrigger();
    }

    private void Awake()
    {
        if (addTriggerCollider) EnsureTrigger();
    }

    private void EnsureTrigger()
    {
        SphereCollider sc = GetComponent<SphereCollider>();
        if (sc == null) sc = gameObject.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = Mathf.Max(0.01f, triggerRadius);
        sc.center = triggerCenter;
    }

    private void OnDrawGizmosSelected()
    {
        if (!addTriggerCollider) return;
        Gizmos.color = new Color(1f, 0f, 1f, 0.35f);
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireSphere(triggerCenter, Mathf.Max(0.01f, triggerRadius));
        Gizmos.matrix = old;
    }
}

