using UnityEngine;

/// <summary>
/// Put this on a "Key" object with a Trigger collider.
/// When the player camera (AutoForwardCameraController) enters, the key disappears and sets hasKey=true.
/// </summary>
[DisallowMultipleComponent]
public class KeyPickup : MonoBehaviour
{
    [Header("Pickup")]
    [Tooltip("If true, disables the whole key GameObject on pickup. If false, only disables its Renderers/Colliders.")]
    [SerializeField] private bool deactivateGameObjectOnPickup = true;

    [Tooltip("Optional: require the entering object to belong to Camera.main.")]
    [SerializeField] private bool requireMainCamera = true;

    [Tooltip("Optional tag filter. Leave empty to ignore tags.")]
    [SerializeField] private string requiredTag = "";

    [Header("On Pickup (optional)")]
    [Tooltip("Optional GameObject to activate when the key is picked up (e.g., a UI icon).")]
    [SerializeField] private GameObject activateOnPickup;
    [Tooltip("If Activate On Pickup is null, tries to find a global UI object by name (supports inactive objects).")]
    [SerializeField] private bool autoFindUiByName = true;
    [Tooltip("Name of the global UI GameObject to activate on pickup (e.g., Key).")]
    [SerializeField] private string uiObjectName = "Key";
    [Tooltip("Minimum seconds between auto-find attempts (prevents expensive searches every trigger call).")]
    [SerializeField] [Min(0f)] private float uiFindRetrySeconds = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool logPickup = false;

    private float _lastUiFindAttempt = -999f;

    private void Awake()
    {
        ResolveActivateOnPickupIfNeeded(force: true);
    }

    private void Reset()
    {
        // Ensure we have a trigger collider.
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        AutoForwardCameraController player = other.GetComponentInParent<AutoForwardCameraController>();
        if (player == null) return;

        if (requireMainCamera)
        {
            Camera cam = other.GetComponentInParent<Camera>();
            if (cam == null || Camera.main == null || cam != Camera.main) return;
        }

        if (!string.IsNullOrWhiteSpace(requiredTag) && !HasTagOnSelfOrParents(other.transform, requiredTag))
        {
            return;
        }

        player.GiveKey();
        if (logPickup) Debug.Log($"[KeyPickup] Picked up key '{name}'.", this);

        ResolveActivateOnPickupIfNeeded();
        if (activateOnPickup != null)
        {
            activateOnPickup.SetActive(true);
        }

        if (deactivateGameObjectOnPickup)
        {
            gameObject.SetActive(false);
        }
        else
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }
    }

    private static bool HasTagOnSelfOrParents(Transform t, string tag)
    {
        if (t == null) return false;
        while (t != null)
        {
            if (t.CompareTag(tag)) return true;
            t = t.parent;
        }
        return false;
    }

    private void ResolveActivateOnPickupIfNeeded(bool force = false)
    {
        if (activateOnPickup != null) return;
        if (!autoFindUiByName) return;
        if (string.IsNullOrWhiteSpace(uiObjectName)) return;

        float now = Time.unscaledTime;
        float retry = Mathf.Max(0f, uiFindRetrySeconds);
        if (!force && (now - _lastUiFindAttempt) < retry) return;
        _lastUiFindAttempt = now;

        activateOnPickup = FindUiGameObjectByNameIncludingInactive(uiObjectName);
    }

    private GameObject FindUiGameObjectByNameIncludingInactive(string exactName)
    {
        if (string.IsNullOrWhiteSpace(exactName)) return null;

        GameObject best = null;
        GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            GameObject go = all[i];
            if (go == null) continue;
            if (go.name != exactName) continue;
            if (!go.scene.IsValid() || !go.scene.isLoaded) continue;
            if (go.hideFlags != HideFlags.None) continue;

            // Avoid binding to the 3D key pickup itself (common if it's also named "Key").
            if (go == gameObject) continue;
            Transform t = go.transform;
            if (t != null && (t == transform || t.IsChildOf(transform))) continue;

            // Prefer UI objects.
            if (go.GetComponent<RectTransform>() != null) return go;
            if (best == null) best = go;
        }

        return best;
    }
}

