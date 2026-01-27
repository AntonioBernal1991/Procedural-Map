using UnityEngine;

/// <summary>
/// Manual marker: attach this to ONLY the cubes you want to stay highlighted.
/// Uses MaterialPropertyBlock (no material instancing).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class ManualCubeMarker : MonoBehaviour
{
    [SerializeField] private Color _markerColor = new Color(1f, 0f, 1f, 1f); // lilac/magenta

    [Tooltip("Apply marker color automatically when enabled.")]
    [SerializeField] private bool _applyOnEnable = true;

    [Tooltip("Shader color property. URP/Lit usually uses _BaseColor, legacy uses _Color.")]
    [SerializeField] private string _baseColorProperty = "_BaseColor";

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;

    private string _propUsed;
    private bool _hadPrev;
    private Color _prev;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        if (_applyOnEnable) Apply();
    }

    private void OnDisable()
    {
        Clear();
    }

    [ContextMenu("Apply Marker")]
    public void Apply()
    {
        if (_renderer == null) _renderer = GetComponent<Renderer>();
        if (_renderer == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        _renderer.GetPropertyBlock(_mpb);

        _hadPrev = false;
        _propUsed = null;

        // Prefer MPB if it already has a color set.
        if (!string.IsNullOrWhiteSpace(_baseColorProperty) && _mpb.HasColor(_baseColorProperty))
        {
            _prev = _mpb.GetColor(_baseColorProperty);
            _hadPrev = true;
            _propUsed = _baseColorProperty;
        }
        else if (_mpb.HasColor("_Color"))
        {
            _prev = _mpb.GetColor("_Color");
            _hadPrev = true;
            _propUsed = "_Color";
        }

        // If MPB didn't contain it yet, read from material (best effort).
        if (_propUsed == null)
        {
            var mat = _renderer.sharedMaterial;
            if (mat != null)
            {
                if (!string.IsNullOrWhiteSpace(_baseColorProperty) && mat.HasProperty(_baseColorProperty))
                {
                    _prev = mat.GetColor(_baseColorProperty);
                    _hadPrev = true;
                    _propUsed = _baseColorProperty;
                }
                else if (mat.HasProperty("_Color"))
                {
                    _prev = mat.GetColor("_Color");
                    _hadPrev = true;
                    _propUsed = "_Color";
                }
            }
        }

        // If we still don't know, default to _BaseColor.
        if (string.IsNullOrEmpty(_propUsed))
        {
            _propUsed = string.IsNullOrWhiteSpace(_baseColorProperty) ? "_BaseColor" : _baseColorProperty;
        }

        _mpb.SetColor(_propUsed, _markerColor);
        _renderer.SetPropertyBlock(_mpb);
    }

    [ContextMenu("Clear Marker")]
    public void Clear()
    {
        if (_renderer == null) _renderer = GetComponent<Renderer>();
        if (_renderer == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        _renderer.GetPropertyBlock(_mpb);

        if (_hadPrev && !string.IsNullOrEmpty(_propUsed))
        {
            _mpb.SetColor(_propUsed, _prev);
        }
        else
        {
            _mpb.Clear();
        }

        _renderer.SetPropertyBlock(_mpb);
        _hadPrev = false;
        _propUsed = null;
    }
}

