using UnityEngine;

/// <summary>
/// Test helper: attach to a module root (e.g. "Module_0") to detect the "central cube"
/// and tint it using a MaterialPropertyBlock (no material instancing).
///
/// How it works:
/// 1) Computes a Bounds for the module from child Renderers (optionally ignoring BasePlane / Combined meshes).
/// 2) Finds the child cube (Renderer) whose position is closest to bounds.center.
/// 3) Applies a color override to that Renderer.
/// </summary>
[DisallowMultipleComponent]
public class ModuleCenterHighlighter : MonoBehaviour
{
    public enum TargetScope
    {
        /// <summary>Assumes this GameObject is a single module root.</summary>
        ThisIsAModule = 0,
        /// <summary>Assumes this GameObject is the MAP root and contains Module_* children.</summary>
        ThisIsTheMapRoot = 1
    }

    [Header("Target")]
    [SerializeField] private TargetScope _targetScope = TargetScope.ThisIsAModule;

    [Tooltip("Only used when TargetScope = ThisIsTheMapRoot. Modules are identified by name prefix (default: 'Module_').")]
    [SerializeField] private string _moduleNamePrefix = "Module_";

    [Header("Selection")]
    [Tooltip("Ignore Renderers named 'BasePlane'.")]
    [SerializeField] private bool _ignoreBasePlane = true;

    [Tooltip("Ignore any combined mesh objects whose name contains '_Combined'.")]
    [SerializeField] private bool _ignoreCombinedMeshes = true;

    [Tooltip("If true, includes inactive children when searching (useful in-editor).")]
    [SerializeField] private bool _includeInactive = true;

    [Tooltip("If true, only highlight modules that have GeneratedModulePathInfo.IsTurnModule = true.")]
    [SerializeField] private bool _onlyHighlightTurnModules = true;

    [Header("Highlight")]
    [SerializeField] private Color _highlightColor = Color.magenta;

    [Tooltip("Shader property name for base color. Tries _BaseColor then _Color.")]
    [SerializeField] private string _baseColorProperty = "_BaseColor";

    [Header("Debug")]
    [SerializeField] private bool _logResult = false;
    [Tooltip("If false, suppresses logs while in Play Mode (prevents console spam).")]
    [SerializeField] private bool _logInPlayMode = false;
    [SerializeField] private bool _drawGizmos = true;

    private struct HighlightState
    {
        public Renderer Renderer;
        public bool HadPrevColor;
        public Color PrevColor;
        public string ColorPropUsed;
    }

    private readonly System.Collections.Generic.List<HighlightState> _highlighted = new System.Collections.Generic.List<HighlightState>(128);
    private Bounds _lastBounds;
    private bool _hasLastBounds;
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        FindAndHighlight();
    }

    private void OnDisable()
    {
        ClearHighlight();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // In editor, live-update when changing settings in Inspector.
        if (!isActiveAndEnabled) return;
        FindAndHighlight();
    }
#endif

    [ContextMenu("Find & Highlight Center Cube")]
    public void FindAndHighlight()
    {
        ClearHighlight();

        if (_targetScope == TargetScope.ThisIsTheMapRoot)
        {
            int modules = HighlightAllModulesUnderMapRoot();
            if (ShouldLog()) Debug.Log($"[{nameof(ModuleCenterHighlighter)}] Highlighted {modules} module center cube(s) under '{name}'.", this);
            return;
        }

        // Single-module mode (original behavior)
        if (!TryComputeModuleBounds(transform, out _lastBounds))
        {
            if (ShouldLog()) Debug.LogWarning($"[{nameof(ModuleCenterHighlighter)}] No child renderers found under '{name}'.", this);
            _hasLastBounds = false;
            return;
        }

        _hasLastBounds = true;

        if (!TryFindClosestRendererToPoint(transform, _lastBounds.center, out Renderer centerRenderer))
        {
            if (ShouldLog()) Debug.LogWarning($"[{nameof(ModuleCenterHighlighter)}] Could not find a center cube renderer under '{name}'.", this);
            return;
        }

        ApplyHighlight(centerRenderer);

        if (ShouldLog())
        {
            Debug.Log($"[{nameof(ModuleCenterHighlighter)}] Module '{name}' center renderer: '{centerRenderer.name}' at {centerRenderer.transform.position}", this);
        }
    }

    private int HighlightAllModulesUnderMapRoot()
    {
        Transform[] allChildren = GetComponentsInChildren<Transform>(_includeInactive);
        int highlightedModules = 0;

        string prefix = string.IsNullOrWhiteSpace(_moduleNamePrefix) ? "Module_" : _moduleNamePrefix;
        for (int i = 0; i < allChildren.Length; i++)
        {
            Transform t = allChildren[i];
            if (t == null) continue;
            if (t == transform) continue;
            if (!t.name.StartsWith(prefix)) continue;

            if (_onlyHighlightTurnModules)
            {
                GeneratedModulePathInfo pathInfo = t.GetComponent<GeneratedModulePathInfo>();
                if (pathInfo == null || !pathInfo.IsTurnModule) continue;
            }

            if (!TryComputeModuleBounds(t, out Bounds bounds)) continue;
            if (!TryFindClosestRendererToPoint(t, bounds.center, out Renderer centerRenderer)) continue;

            ApplyHighlight(centerRenderer);
            highlightedModules++;

            // Keep a last-bounds snapshot for gizmos (shows the last module processed)
            _lastBounds = bounds;
            _hasLastBounds = true;

            if (ShouldLog())
            {
                Debug.Log($"[{nameof(ModuleCenterHighlighter)}] Module '{t.name}' center renderer: '{centerRenderer.name}' at {centerRenderer.transform.position}", t);
            }
        }

        return highlightedModules;
    }

    private bool ShouldLog()
    {
        if (!_logResult) return false;
        if (!Application.isPlaying) return true;
        return _logInPlayMode;
    }

    private bool TryComputeModuleBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null) return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(_includeInactive);
        bool hasAny = false;

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (!ShouldConsiderRenderer(r)) continue;

            if (!hasAny)
            {
                bounds = r.bounds;
                hasAny = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return hasAny;
    }

    private bool TryFindClosestRendererToPoint(Transform root, Vector3 point, out Renderer closest)
    {
        closest = null;
        float bestSqr = float.PositiveInfinity;

        if (root == null) return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(_includeInactive);
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (!ShouldConsiderRenderer(r)) continue;

            // Use transform.position as "cube center" for this test.
            Vector3 p = r.transform.position;
            float d = (p - point).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                closest = r;
            }
        }

        return closest != null;
    }

    private bool ShouldConsiderRenderer(Renderer r)
    {
        if (_ignoreBasePlane && r.gameObject.name == "BasePlane") return false;
        if (_ignoreCombinedMeshes && r.gameObject.name.Contains("_Combined")) return false;
        return true;
    }

    private void ApplyHighlight(Renderer r)
    {
        if (r == null) return;

        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        r.GetPropertyBlock(_mpb);

        // Try preserve previous color (best-effort).
        bool hadPrevColor = false;
        string colorPropUsed = null;
        Color prevColor = default;

        if (!string.IsNullOrWhiteSpace(_baseColorProperty) && _mpb.HasColor(_baseColorProperty))
        {
            prevColor = _mpb.GetColor(_baseColorProperty);
            hadPrevColor = true;
            colorPropUsed = _baseColorProperty;
        }
        else if (_mpb.HasColor("_Color"))
        {
            prevColor = _mpb.GetColor("_Color");
            hadPrevColor = true;
            colorPropUsed = "_Color";
        }

        // If MPB doesn't have it yet, fall back to sharedMaterial property checks.
        if (colorPropUsed == null)
        {
            var mat = r.sharedMaterial;
            if (mat != null)
            {
                if (mat.HasProperty(_baseColorProperty))
                {
                    prevColor = mat.GetColor(_baseColorProperty);
                    hadPrevColor = true;
                    colorPropUsed = _baseColorProperty;
                }
                else if (mat.HasProperty("_Color"))
                {
                    prevColor = mat.GetColor("_Color");
                    hadPrevColor = true;
                    colorPropUsed = "_Color";
                }
            }
        }

        // Apply color override (choose a property).
        string prop = colorPropUsed;
        if (string.IsNullOrEmpty(prop))
        {
            // Default to base property; many URP/Lit use _BaseColor.
            prop = string.IsNullOrWhiteSpace(_baseColorProperty) ? "_BaseColor" : _baseColorProperty;
            colorPropUsed = prop;
        }

        _mpb.SetColor(prop, _highlightColor);
        r.SetPropertyBlock(_mpb);

        _highlighted.Add(new HighlightState
        {
            Renderer = r,
            HadPrevColor = hadPrevColor,
            PrevColor = prevColor,
            ColorPropUsed = colorPropUsed
        });
    }

    [ContextMenu("Clear Highlight")]
    public void ClearHighlight()
    {
        if (_highlighted.Count == 0) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        for (int i = 0; i < _highlighted.Count; i++)
        {
            HighlightState s = _highlighted[i];
            if (s.Renderer == null) continue;

            s.Renderer.GetPropertyBlock(_mpb);
            if (s.HadPrevColor && !string.IsNullOrEmpty(s.ColorPropUsed))
            {
                _mpb.SetColor(s.ColorPropUsed, s.PrevColor);
            }
            else
            {
                // If we don't know what it was, just clear the block to revert to material.
                _mpb.Clear();
            }
            s.Renderer.SetPropertyBlock(_mpb);
        }

        _highlighted.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos) return;
        if (!_hasLastBounds) return;

        Gizmos.color = new Color(1f, 0.2f, 1f, 0.35f);
        Gizmos.DrawWireCube(_lastBounds.center, _lastBounds.size);
        Gizmos.DrawSphere(_lastBounds.center, Mathf.Max(0.05f, _lastBounds.extents.magnitude * 0.03f));
    }
}

