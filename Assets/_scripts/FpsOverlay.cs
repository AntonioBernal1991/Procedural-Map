using UnityEngine;

/// <summary>
/// Lightweight FPS + frametime overlay for builds (and editor if desired).
/// Add this component to any active GameObject (e.g., Main Camera) in your scene.
/// </summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public class FpsOverlay : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private bool _show = true;
    [SerializeField] private KeyCode _toggleKey = KeyCode.F1;
    [SerializeField] private int _fontSize = 18;
    [SerializeField] private Vector2 _margin = new Vector2(12f, 12f);

    [Header("Update")]
    [Tooltip("How often to refresh the text (seconds). Lower = more responsive, higher = less overhead.")]
    [Min(0.05f)]
    [SerializeField] private float _refreshInterval = 0.25f;

    [Tooltip("Smoothing factor for deltaTime averaging. Higher = smoother but slower to react.")]
    [Range(0.0f, 0.99f)]
    [SerializeField] private float _smoothing = 0.9f;

    [Header("Optional (for builds)")]
    [Tooltip("If enabled, forces QualitySettings.vSyncCount = 0 at runtime.")]
    [SerializeField] private bool _forceDisableVSync = false;

    [Tooltip("If > 0, forces Application.targetFrameRate to this value at runtime. Use -1 to leave unchanged.")]
    [SerializeField] private int _forceTargetFrameRate = -1;

    [Header("Lifetime")]
    [SerializeField] private bool _dontDestroyOnLoad = true;

    private float _smoothedDt;
    private float _nextRefreshTime;
    private string _cachedText = "";
    private GUIStyle _style;

    private void Awake()
    {
        _smoothedDt = Time.unscaledDeltaTime;

        ApplyRuntimeOverrides();

        if (_dontDestroyOnLoad)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnValidate()
    {
        if (_refreshInterval < 0.05f) _refreshInterval = 0.05f;
        if (_smoothing < 0f) _smoothing = 0f;
        if (_smoothing > 0.99f) _smoothing = 0.99f;
    }

    private void ApplyRuntimeOverrides()
    {
        if (_forceDisableVSync)
        {
            QualitySettings.vSyncCount = 0;
        }

        if (_forceTargetFrameRate > 0)
        {
            Application.targetFrameRate = _forceTargetFrameRate;
        }
    }

    private void Update()
    {
        if (_toggleKey != KeyCode.None && Input.GetKeyDown(_toggleKey))
        {
            _show = !_show;
        }

        float dt = Time.unscaledDeltaTime;
        _smoothedDt = (_smoothedDt * _smoothing) + (dt * (1f - _smoothing));

        if (Time.unscaledTime < _nextRefreshTime) return;
        _nextRefreshTime = Time.unscaledTime + _refreshInterval;

        float ms = _smoothedDt * 1000f;
        float fps = _smoothedDt > 0f ? (1f / _smoothedDt) : 0f;

        int q = QualitySettings.GetQualityLevel();
        string qName = (QualitySettings.names != null && q >= 0 && q < QualitySettings.names.Length)
            ? QualitySettings.names[q]
            : q.ToString();

        float dpiScale = QualitySettings.resolutionScalingFixedDPIFactor;

        // Keep it short and useful for perf debugging in builds.
        _cachedText =
            $"FPS: {fps:0}\n" +
            $"Frame: {ms:0.0} ms\n" +
            $"Quality: {qName}\n" +
            $"RenderScale: {dpiScale:0.00}\n" +
            $"vSync: {QualitySettings.vSyncCount}\n" +
            $"targetFPS: {Application.targetFrameRate}";
    }

    private void OnGUI()
    {
        if (!_show) return;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                alignment = TextAnchor.UpperLeft,
                richText = false
            };
            _style.normal.textColor = Color.white;
        }

        Rect r = new Rect(_margin.x, _margin.y, 260f, 120f);

        // Simple shadow for readability.
        Color old = _style.normal.textColor;
        _style.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), _cachedText, _style);
        _style.normal.textColor = old;
        GUI.Label(r, _cachedText, _style);
    }
}

