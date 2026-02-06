using UnityEngine;

/// <summary>
/// Distance-based pupil dilation for EYE ADVANCED shaders (drives material property "_pupilSize").
/// Attach to the eye Renderer object (same GameObject that has the Renderer).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)] // run after most scripts (incl. EyeAdv_AutoDilation) so we can override/blend
public class EyeAdv_DistanceDilation : MonoBehaviour
{
    public enum BlendMode
    {
        /// <summary>Use only the distance-based value (ignores other scripts).</summary>
        Override = 0,
        /// <summary>Add distance-based value on top of the base value.</summary>
        Add = 1,
        /// <summary>Multiply the base value by the distance-based value.</summary>
        Multiply = 2
    }

    [Header("Target")]
    [Tooltip("Usually the player camera. If null, will use Camera.main.")]
    [SerializeField] private Transform target;

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
    [Tooltip("If true, writes _pupilSize to the shared material directly (like EyeAdv_AutoDilation). " +
             "Not recommended for per-instance control, but useful for compatibility debugging.")]
    [SerializeField] private bool writeToSharedMaterial = false;

    [Header("Debug")]
    [Tooltip("If true, logs the distance to the target periodically.")]
    [SerializeField] private bool debugLogDistance = false;
    [Tooltip("Seconds between logs (ignored if Debug Log Distance is false).")]
    [SerializeField] [Min(0.05f)] private float debugLogIntervalSeconds = 0.5f;

    private static readonly int PupilSizeId = Shader.PropertyToID("_pupilSize");
    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private float _current;
    private float _vel;
    private float _nextLogTime;

    [ContextMenu("Enable Debug Distance Log")]
    private void EnableDebugDistanceLog()
    {
        debugLogDistance = true;
        _nextLogTime = 0f;
    }

    [ContextMenu("Disable Debug Distance Log")]
    private void DisableDebugDistanceLog()
    {
        debugLogDistance = false;
    }

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
    }

    private void LateUpdate()
    {
        if (_renderer == null) return;
        Transform t = target;
        if (t == null && Camera.main != null) t = Camera.main.transform;
        if (t == null) return;

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

        bool hasProp = HasPupilProperty();
        if (!hasProp) return;

        if (writeToSharedMaterial)
        {
            // Compatibility mode: behaves like EyeAdv_AutoDilation (edits shared material).
            if (_renderer.sharedMaterial != null) _renderer.sharedMaterial.SetFloat(PupilSizeId, _current);
        }
        else
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(PupilSizeId, _current);
            _renderer.SetPropertyBlock(_mpb);
        }

        // Distance debug logging removed (keeps console clean).
    }

    private float MapDistanceToPupil(float distance)
    {
        float nd = Mathf.Max(0.0001f, nearDistance);
        float fd = Mathf.Max(nd + 0.0001f, farDistance);

        float t = Mathf.InverseLerp(nd, fd, distance);
        // near => pupilAtNear, far => pupilAtFar
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

