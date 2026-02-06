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

    [Tooltip("If true, disables the cube GameObject on unlock (recommended).")]
    [SerializeField] private bool deactivateOnUnlock = true;

    [Tooltip("Optional: called when unlocked.")]
    [SerializeField] private UnityEvent onUnlocked;

    [Header("While Inside (optional)")]
    [Tooltip("Optional GameObject to activate while the player is inside this cube's TRIGGER collider (e.g., a UI prompt).")]
    [SerializeField] private GameObject activateWhileInside;
    [Tooltip("If true, only shows the object while this cube is still locked.")]
    [SerializeField] private bool showOnlyWhileLocked = true;
    [Tooltip("If true, only triggers when entering object belongs to Camera.main.")]
    [SerializeField] private bool requireMainCamera = true;

    [Header("Debug")]
    [SerializeField] private bool logUnlock = false;

    private bool _unlocked;

    public bool IsUnlocked => _unlocked;

    /// <summary>
    /// Used by AutoForwardCameraController proximity detection (CapsuleCast) to show a prompt
    /// even if the player stops before entering a trigger volume.
    /// </summary>
    public void SetProximityPromptActive(bool active)
    {
        if (activateWhileInside == null) return;
        if (showOnlyWhileLocked && _unlocked)
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
        if (_unlocked) return true;
        if (player == null) return false;
        if (!player.HasKey) return false;

        if (consumeKeyOnUnlock) player.ConsumeKey();

        _unlocked = true;
        if (logUnlock) Debug.Log($"[LockedCube] Unlocked '{name}'.", this);

        onUnlocked?.Invoke();

        // Ensure any "while inside" prompt is turned off after unlocking.
        SetWhileInsideActive(false);

        if (deactivateOnUnlock)
        {
            gameObject.SetActive(false);
        }
        else
        {
            // Fallback: hide visuals and disable colliders.
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        return true;
    }

    private void Reset()
    {
        // If the user adds a trigger collider to this object for "while inside" behavior,
        // make sure it's set correctly.
        // (We don't auto-add a collider because this cube may already have a blocking collider.)
    }

    private void OnTriggerEnter(Collider other)
    {
        if (activateWhileInside == null) return;
        if (showOnlyWhileLocked && _unlocked) return;
        if (other == null) return;

        AutoForwardCameraController player = other.GetComponentInParent<AutoForwardCameraController>();
        if (player == null) return;

        if (requireMainCamera)
        {
            Camera cam = other.GetComponentInParent<Camera>();
            if (cam == null || Camera.main == null || cam != Camera.main) return;
        }

        SetWhileInsideActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
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
}

