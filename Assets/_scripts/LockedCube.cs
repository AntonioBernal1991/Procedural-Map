using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on a blocking cube. If the player has a key, the cube disappears and the player can continue.
///
/// Works with AutoForwardCameraController because it also unlocks when the camera's forward CapsuleCast hits it.
/// </summary>
[DisallowMultipleComponent]
public class LockedCube : MonoBehaviour
{
    [Header("Unlock")]
    [Tooltip("If true, consumes the key (hasKey becomes false) when unlocking.")]
    [SerializeField] private bool consumeKeyOnUnlock = true;

    [Header("Unlock Fade")]
    [Tooltip("If true, fades the cube alpha to 0, then disables it so the player can pass.")]
    [SerializeField] private bool fadeOutOnUnlock = true;
    [Tooltip("Seconds to fade alpha to 0.")]
    [SerializeField] [Range(0f, 3f)] private float fadeSeconds = 1f;
    [Tooltip("Color property to fade. Standard shader uses _Color. URP uses _BaseColor.")]
    [SerializeField] private string colorProperty = "_Color";

    [Tooltip("If true, disables the cube GameObject after fade completes (recommended).")]
    [SerializeField] private bool deactivateOnUnlock = true;

    [Tooltip("Optional: called when unlocked.")]
    [SerializeField] private UnityEvent onUnlocked;

    [Header("While Inside (optional)")]
    [Tooltip("Optional GameObject to activate while the player is inside this cube's TRIGGER collider (e.g., a UI prompt).")]
    [SerializeField] private GameObject activateWhileInside;
    [Tooltip("If Activate While Inside is null, tries to find a global UI object by name (supports inactive objects).")]
    [SerializeField] private bool autoFindPromptByName = true;
    [Tooltip("Name of the global UI GameObject to find (e.g., Look4Key).")]
    [SerializeField] private string promptObjectName = "Look4Key";
    [Tooltip("Minimum seconds between auto-find attempts (prevents expensive searches every frame).")]
    [SerializeField] [Min(0f)] private float promptFindRetrySeconds = 0.5f;
    [Tooltip("If true, only shows the object while this cube is still locked.")]
    [SerializeField] private bool showOnlyWhileLocked = true;
    [Tooltip("If true, only triggers when entering object belongs to Camera.main.")]
    [SerializeField] private bool requireMainCamera = true;

    [Header("Debug")]
    [SerializeField] private bool logUnlock = false;

    private bool _unlocked;
    private bool _unlocking;
    private Coroutine _fadeRoutine;
    private MaterialPropertyBlock _mpb;
    private float _lastPromptFindAttempt = -999f;

    private struct FadeTarget
    {
        public Renderer Renderer;
        public string Prop;
        public Color StartColor;
        public bool Valid;
    }

    private FadeTarget[] _fadeTargets;

    public bool IsUnlocked => _unlocked;
    public bool IsUnlocking => _unlocking;

    /// <summary>
    /// Used by AutoForwardCameraController proximity detection (CapsuleCast) to show a prompt
    /// even if the player stops before entering a trigger volume.
    /// </summary>
    public void SetProximityPromptActive(bool active)
    {
        ResolvePromptIfNeeded();
        if (activateWhileInside == null) return;
        // Never show prompt once unlocking started (fade in progress) or after unlocked.
        if (_unlocking || (showOnlyWhileLocked && _unlocked))
        {
            SetWhileInsideActive(false);
            return;
        }
        SetWhileInsideActive(active);
    }

    /// <summary>
    /// Called by the player controller when it detects this cube ahead.
    /// Returns true if it unlocked (or was already unlocked).
    /// </summary>
    public bool TryUnlock(AutoForwardCameraController player)
    {
        if (_unlocked || _unlocking) return true;
        if (player == null) return false;
        if (!player.HasKey) return false;

        if (consumeKeyOnUnlock) player.ConsumeKey();

        _unlocking = true;
        if (logUnlock) Debug.Log($"[LockedCube] Unlocked '{name}'.", this);

        onUnlocked?.Invoke();

        // Ensure any "while inside" prompt is turned off after unlocking.
        SetWhileInsideActive(false);

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(UnlockRoutine());

        return true;
    }

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        CacheFadeTargets();
        ResolvePromptIfNeeded(force: true);
    }

    private void CacheFadeTargets()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        _fadeTargets = new FadeTarget[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            var ft = new FadeTarget { Renderer = r, Prop = colorProperty, StartColor = default, Valid = false };

            if (r == null)
            {
                _fadeTargets[i] = ft;
                continue;
            }

            Material m = r.sharedMaterial;
            if (m == null)
            {
                _fadeTargets[i] = ft;
                continue;
            }

            string prop = !string.IsNullOrWhiteSpace(colorProperty) && m.HasProperty(colorProperty) ? colorProperty : null;
            if (prop == null && m.HasProperty("_Color")) prop = "_Color";
            if (prop == null && m.HasProperty("_BaseColor")) prop = "_BaseColor";
            if (prop == null)
            {
                _fadeTargets[i] = ft;
                continue;
            }

            ft.Prop = prop;
            ft.StartColor = m.GetColor(prop);
            ft.Valid = true;
            _fadeTargets[i] = ft;
        }
    }

    private System.Collections.IEnumerator UnlockRoutine()
    {
        // Refresh targets in case something changed at runtime.
        if (_fadeTargets == null || _fadeTargets.Length == 0) CacheFadeTargets();

        if (fadeOutOnUnlock)
        {
            float d = Mathf.Max(0f, fadeSeconds);
            if (d <= 0f)
            {
                ApplyAlpha(0f);
            }
            else
            {
                float t = 0f;
                while (t < d)
                {
                    t += Time.deltaTime;
                    float a = Mathf.Clamp01(t / d);
                    ApplyAlpha(1f - a);
                    yield return null;
                }
                ApplyAlpha(0f);
            }
        }

        // Mark fully unlocked now (after fade finished).
        _unlocked = true;
        _unlocking = false;
        _fadeRoutine = null;

        // Disable colliders so the player can pass.
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

        if (deactivateOnUnlock)
        {
            gameObject.SetActive(false);
        }
        else
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }
    }

    private void ApplyAlpha(float normalizedAlpha)
    {
        float a = Mathf.Clamp01(normalizedAlpha);
        if (_fadeTargets == null) return;

        for (int i = 0; i < _fadeTargets.Length; i++)
        {
            FadeTarget ft = _fadeTargets[i];
            if (!ft.Valid) continue;
            if (ft.Renderer == null) continue;

            Color c = ft.StartColor;
            c.a = ft.StartColor.a * a;

            ft.Renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(ft.Prop, c);
            ft.Renderer.SetPropertyBlock(_mpb);
        }
    }

    private void Reset()
    {
        // If the user adds a trigger collider to this object for "while inside" behavior,
        // make sure it's set correctly.
        // (We don't auto-add a collider because this cube may already have a blocking collider.)
    }

    private void OnTriggerEnter(Collider other)
    {
        ResolvePromptIfNeeded();
        if (activateWhileInside == null) return;
        if (_unlocking) { SetWhileInsideActive(false); return; }
        if (showOnlyWhileLocked && _unlocked) return;
        if (other == null) return;

        AutoForwardCameraController player = other.GetComponentInParent<AutoForwardCameraController>();
        if (player == null) return;
        // If player already has the key, don't show the "need key" prompt while inside.
        if (player.HasKey)
        {
            SetWhileInsideActive(false);
            return;
        }

        if (requireMainCamera)
        {
            Camera cam = other.GetComponentInParent<Camera>();
            if (cam == null || Camera.main == null || cam != Camera.main) return;
        }

        SetWhileInsideActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        ResolvePromptIfNeeded();
        if (activateWhileInside == null) return;
        if (other == null) return;

        AutoForwardCameraController player = other.GetComponentInParent<AutoForwardCameraController>();
        if (player == null) return;

        if (requireMainCamera)
        {
            Camera cam = other.GetComponentInParent<Camera>();
            if (cam == null || Camera.main == null || cam != Camera.main) return;
        }

        SetWhileInsideActive(false);
    }

    private void SetWhileInsideActive(bool active)
    {
        if (activateWhileInside == null) return;
        if (activateWhileInside.activeSelf == active) return;
        activateWhileInside.SetActive(active);
    }

    private void ResolvePromptIfNeeded(bool force = false)
    {
        if (activateWhileInside != null) return;
        if (!autoFindPromptByName) return;
        if (string.IsNullOrWhiteSpace(promptObjectName)) return;

        float now = Time.unscaledTime;
        float retry = Mathf.Max(0f, promptFindRetrySeconds);
        if (!force && (now - _lastPromptFindAttempt) < retry) return;
        _lastPromptFindAttempt = now;

        activateWhileInside = FindGameObjectByNameIncludingInactive(promptObjectName);
    }

    private static GameObject FindGameObjectByNameIncludingInactive(string exactName)
    {
        if (string.IsNullOrWhiteSpace(exactName)) return null;

        // Finds inactive objects too. Filter out assets / non-scene objects.
        GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            GameObject go = all[i];
            if (go == null) continue;
            if (go.name != exactName) continue;
            if (!go.scene.IsValid() || !go.scene.isLoaded) continue;
            if (go.hideFlags != HideFlags.None) continue;
            return go;
        }

        return null;
    }
}

