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

    [Header("Debug")]
    [SerializeField] private bool logPickup = false;

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
}

