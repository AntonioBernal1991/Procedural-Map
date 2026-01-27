using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime distance culling for large, modular maps.
/// Disables far modules (or their renderers) so FPS doesn't tank when the camera can see the entire maze.
/// </summary>
[DisallowMultipleComponent]
public class MapDistanceCuller : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("If assigned, distance is measured from this transform. If null, uses Target Camera or Camera.main.")]
    [SerializeField] private Transform _target;

    [Tooltip("If Target is null, uses this camera. If null, uses Camera.main.")]
    [SerializeField] private Camera _targetCamera;

    [Header("Culling")]
    [Tooltip("Modules become visible when closer than this distance.")]
    [Min(0f)]
    [SerializeField] private float _enableDistance = 120f;

    [Tooltip("Modules are hidden when farther than this distance (should be >= Enable Distance for hysteresis).")]
    [Min(0f)]
    [SerializeField] private float _disableDistance = 140f;

    [Tooltip("If true, only toggles Renderer.enabled (keeps colliders active). If false, toggles entire module GameObject active state.")]
    [SerializeField] private bool _renderersOnly = true;

    [Tooltip("If true, uses squared distance in XZ only (ignores height). Recommended for top-down cameras.")]
    [SerializeField] private bool _ignoreY = true;

    [Tooltip("How often to update culling (seconds). 0 = every frame.")]
    [Min(0f)]
    [SerializeField] private float _updateInterval = 0.1f;

    [Header("Discovery")]
    [Tooltip("Only objects whose names start with this prefix are treated as modules.")]
    [SerializeField] private string _moduleNamePrefix = "Module_";

    [SerializeField] private bool _includeInactiveModules = true;

    private readonly List<ModuleEntry> _modules = new List<ModuleEntry>(256);
    private float _nextUpdateTime;

    private sealed class ModuleEntry
    {
        public GameObject Go;
        public bool Visible;
        public RendererState[] Renderers;
    }

    [Serializable]
    private struct RendererState
    {
        public Renderer Renderer;
        public bool InitiallyEnabled;
    }

    private void OnValidate()
    {
        if (_disableDistance < _enableDistance)
        {
            _disableDistance = _enableDistance;
        }
    }

    private void Awake()
    {
        RebuildCache();
    }

    public void RebuildCache()
    {
        _modules.Clear();

        Transform[] all = GetComponentsInChildren<Transform>(_includeInactiveModules);
        foreach (Transform t in all)
        {
            if (t == null) continue;
            if (t == transform) continue;

            if (!string.IsNullOrEmpty(_moduleNamePrefix) && !t.name.StartsWith(_moduleNamePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var entry = new ModuleEntry
            {
                Go = t.gameObject,
                Visible = true,
                Renderers = null
            };

            if (_renderersOnly)
            {
                Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
                var states = new List<RendererState>(rs.Length);
                foreach (Renderer r in rs)
                {
                    if (r == null) continue;
                    states.Add(new RendererState { Renderer = r, InitiallyEnabled = r.enabled });
                }
                entry.Renderers = states.ToArray();
            }

            _modules.Add(entry);
        }
    }

    private void Update()
    {
        if (_updateInterval > 0f && Time.unscaledTime < _nextUpdateTime) return;
        _nextUpdateTime = Time.unscaledTime + _updateInterval;

        Vector3 origin = GetOrigin();
        float enableSqr = _enableDistance * _enableDistance;
        float disableSqr = _disableDistance * _disableDistance;

        for (int i = 0; i < _modules.Count; i++)
        {
            ModuleEntry m = _modules[i];
            if (m == null || m.Go == null) continue;

            Vector3 p = m.Go.transform.position;
            Vector3 d = p - origin;
            if (_ignoreY) d.y = 0f;
            float distSqr = d.sqrMagnitude;

            bool shouldBeVisible = m.Visible ? distSqr <= disableSqr : distSqr <= enableSqr;
            if (shouldBeVisible == m.Visible) continue;

            m.Visible = shouldBeVisible;
            ApplyVisibility(m, shouldBeVisible);
        }
    }

    private Vector3 GetOrigin()
    {
        if (_target != null) return _target.position;

        Camera cam = _targetCamera != null ? _targetCamera : Camera.main;
        if (cam != null) return cam.transform.position;

        return transform.position;
    }

    private void ApplyVisibility(ModuleEntry m, bool visible)
    {
        if (_renderersOnly)
        {
            if (m.Renderers == null) return;
            for (int i = 0; i < m.Renderers.Length; i++)
            {
                Renderer r = m.Renderers[i].Renderer;
                if (r == null) continue;
                r.enabled = visible && m.Renderers[i].InitiallyEnabled;
            }
        }
        else
        {
            m.Go.SetActive(visible);
        }
    }
}

