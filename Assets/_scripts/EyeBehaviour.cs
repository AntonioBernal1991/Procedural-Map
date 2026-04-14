using UnityEngine;

/// <summary>
/// EyeBehaviour = (1) Look at the player camera + (2) Distance-based pupil dilation for EYE ADVANCED shaders.
/// Attach to the Eye object (ideally the same GameObject that has the eye Renderer).
/// </summary>
[DisallowMultipleComponent]
public class EyeBehaviour : MonoBehaviour
{
    public enum BlendMode
    {
        /// <summary>Use only the distance-based value (ignores base value).</summary>
        Override = 0,
        /// <summary>Add distance-based value on top of the base value.</summary>
        Add = 1,
        /// <summary>Multiply the base value by the distance-based value.</summary>
        Multiply = 2
    }

    [Header("Look At")]
    [SerializeField] private bool enableLookAt = true;

    [Tooltip("Optional explicit target. If null, uses Camera.main.")]
    [SerializeField] private Transform lookTarget;

    [Tooltip("If true, only rotate around Y (keeps upright). If false, full look rotation.")]
    [SerializeField] private bool yOnly = false;

    [Tooltip("Local forward axis correction (if your eye mesh looks sideways).")]
    [SerializeField] private Vector3 localForwardAxis = Vector3.forward;

    [Tooltip("Use LateUpdate so it follows after camera movement.")]
    [SerializeField] private bool useLateUpdate = true;

    [Header("Pupil Dilation (Distance)")]
    [SerializeField] private bool enableDistanceDilation = true;

    [Tooltip("Usually the player camera. If null, will use Camera.main.")]
    [SerializeField] private Transform dilationTarget;

    [Header("Distance Mapping")]
    [Tooltip("At or below this distance, pupil size will be 'pupilAtNear'.")]
    [SerializeField] [Min(0f)] private float nearDistance = 1.5f;

    [Tooltip("At or above this distance, pupil size will be 'pupilAtFar'.")]
    [SerializeField] [Min(0f)] private float farDistance = 12f;

    [Tooltip("Pupil size when target is near (bigger = more dilated).")]
    [SerializeField] [Range(0f, 1f)] private float pupilAtNear = 1.0f;

    [Tooltip("Pupil size when target is far.")]
    [SerializeField] [Range(0f, 1f)] private float pupilAtFar = 0.2f;

    [Header("Blend")]
    [SerializeField] private BlendMode blendMode = BlendMode.Multiply;

    [Tooltip("When blending, read the base value from the shared material (e.g., from EyeAdv_AutoDilation).")]
    [SerializeField] private bool readBaseFromMaterial = true;

    [Tooltip("If false, uses 'manualBasePupilSize' instead of reading from material.")]
    [SerializeField] [Range(0f, 1f)] private float manualBasePupilSize = 0.5f;

    [Header("Smoothing")]
    [Tooltip("Seconds to smooth changes. 0 = instant.")]
    [SerializeField] [Min(0f)] private float smoothTime = 0.15f;

    [Header("Output")]
    [Tooltip("If true, writes _pupilSize to the shared material directly. Not recommended (affects all instances).")]
    [SerializeField] private bool writeToSharedMaterial = false;

    private static readonly int PupilSizeId = Shader.PropertyToID("_pupilSize");

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private float _current;
    private float _vel;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
    }

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
        if (enableLookAt) TickLookAt();
        if (enableDistanceDilation) TickDistanceDilation();
    }

    private void TickLookAt()
    {
        Transform t = lookTarget;
        if (t == null)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            t = cam.transform;
        }

        Vector3 toTarget = t.position - transform.position;
        if (yOnly) toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.000001f) return;

        Quaternion look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        Quaternion axisFix = Quaternion.FromToRotation(localForwardAxis.normalized, Vector3.forward);
        transform.rotation = look * axisFix;
    }

    private void TickDistanceDilation()
    {
        if (_renderer == null) return;

        Transform t = dilationTarget;
        if (t == null && Camera.main != null) t = Camera.main.transform;
        if (t == null) return;

        if (!HasPupilProperty()) return;

        float d = Vector3.Distance(_renderer.transform.position, t.position);
        float mapped = MapDistanceToPupil(d);

        float baseValue = readBaseFromMaterial ? ReadBaseFromSharedMaterial() : manualBasePupilSize;
        float finalValue = Blend(baseValue, mapped);
        finalValue = Mathf.Clamp01(finalValue);

        if (smoothTime <= 0f)
        {
            _current = finalValue;
            _vel = 0f;
        }
        else
        {
            _current = Mathf.SmoothDamp(_current, finalValue, ref _vel, smoothTime);
        }

        if (writeToSharedMaterial)
        {
            if (_renderer.sharedMaterial != null) _renderer.sharedMaterial.SetFloat(PupilSizeId, _current);
        }
        else
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(PupilSizeId, _current);
            _renderer.SetPropertyBlock(_mpb);
        }
    }

    private float MapDistanceToPupil(float distance)
    {
        float nd = Mathf.Max(0.0001f, nearDistance);
        float fd = Mathf.Max(nd + 0.0001f, farDistance);
        float t = Mathf.InverseLerp(nd, fd, distance);
        return Mathf.Lerp(pupilAtNear, pupilAtFar, t);
    }

    private float ReadBaseFromSharedMaterial()
    {
        if (_renderer == null) return manualBasePupilSize;
        Material m = _renderer.sharedMaterial;
        if (m == null) return manualBasePupilSize;
        if (!m.HasProperty(PupilSizeId)) return manualBasePupilSize;
        return m.GetFloat(PupilSizeId);
    }

    private bool HasPupilProperty()
    {
        if (_renderer == null) return false;
        Material m = _renderer.sharedMaterial;
        if (m == null) return false;
        return m.HasProperty(PupilSizeId);
    }

    private float Blend(float baseValue, float distanceValue)
    {
        switch (blendMode)
        {
            case BlendMode.Override:
                return distanceValue;
            case BlendMode.Add:
                return baseValue + distanceValue;
            case BlendMode.Multiply:
            default:
                return baseValue * distanceValue;
        }
    }
}

