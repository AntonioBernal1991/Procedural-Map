using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

/// <summary>
/// Attach to the Main Camera. When you press a key (default K), the camera rotates to look at a target (eye).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class CameraLookAtOnKey : MonoBehaviour
{
    [SerializeField] private KeyCode key = KeyCode.K;
    [Tooltip("Eye (or any target) the camera will look at when pressing the key.")]
    [SerializeField] private Transform target;

    [Tooltip("If true, only rotates around Y (keeps camera pitch). If false, full look rotation.")]
    [SerializeField] private bool yawOnly = false;

    [Tooltip("Seconds to rotate to target. 0 = instant.")]
    [SerializeField] private float rotateDuration = 0.15f;

    [Header("FOV")]
    [Tooltip("If true, also animates the camera Field Of View when toggling.")]
    [SerializeField] private bool animateFov = true;
    [Tooltip("Target FOV when looking at the target. NOTE: Unity clamps FOV; values <= 0 may not be visible.")]
    [SerializeField] private float targetFovWhenLooking = 0f;
    [Tooltip("Seconds to animate FOV. 0 = instant.")]
    [SerializeField] private float fovDuration = 2f;

    [Header("On FOV Reached (looking)")]
    [Tooltip("Optional legacy single object: activated right after FOV finishes animating to Target FOV (only when toggling to 'look at target').")]
    [FormerlySerializedAs("activateOnFovReached")]
    [SerializeField] private GameObject activateOnFovReachedLegacy;

    [Tooltip("Optional list. If size >= 2: activates [0] when music is NOT playing, and [1] when music IS playing (at the moment FOV is reached). Other entries are deactivated.")]
    [SerializeField] private List<GameObject> activateOnFovReachedList = new List<GameObject>(2);

    [Header("On FOV Reached (looking) - Audio")]
    [Tooltip("If true, stops the background music when 'Activate On FOV Reached' is triggered.")]
    [SerializeField] private bool stopMusicOnFovReached = true;
    [Tooltip("If true, pauses instead of stopping.")]
    [SerializeField] private bool pauseInsteadOfStop = false;
    [Tooltip("Seconds to fade out music (0 = instant).")]
    [SerializeField] private float musicFadeOutSeconds = 1.25f;

    private Quaternion _from;
    private Quaternion _to;
    private Quaternion _savedRotation;
    private float _t;
    private bool _rotating;
    private bool _isLookingAtTarget;

    private Camera _cam;
    private float _savedFov;
    private float _fovFrom;
    private float _fovTo;
    private float _fovT;
    private bool _fovAnimating;
    private bool _pendingActivateOnFovReached;

    private bool _hasCachedMusicStateForActivation;
    private bool _cachedMusicWasPlayingForActivation;

    public bool IsLookingAtTarget => _isLookingAtTarget;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(key))
        {
            ToggleLook();
        }

        if (_rotating)
        {
            if (rotateDuration <= 0f)
            {
                transform.rotation = _to;
                _rotating = false;
                return;
            }

            _t += Time.deltaTime / Mathf.Max(0.0001f, rotateDuration);
            float t = Mathf.Clamp01(_t);
            transform.rotation = Quaternion.Slerp(_from, _to, t);
            if (t >= 1f) _rotating = false;
        }

        if (_fovAnimating && _cam != null)
        {
            if (fovDuration <= 0f)
            {
                _cam.fieldOfView = _fovTo;
                _fovAnimating = false;
                OnFovAnimationFinished();
            }
            else
            {
                _fovT += Time.deltaTime / Mathf.Max(0.0001f, fovDuration);
                float t = Mathf.Clamp01(_fovT);
                _cam.fieldOfView = Mathf.Lerp(_fovFrom, _fovTo, t);
                if (t >= 1f)
                {
                    _fovAnimating = false;
                    OnFovAnimationFinished();
                }
            }
        }
    }

    private void OnFovAnimationFinished()
    {
        // Only fire when we toggled into "look at target" mode and reached the "looking" target FOV.
        if (!_pendingActivateOnFovReached) return;
        _pendingActivateOnFovReached = false;

        // Activate UI based on whether music is currently playing.
        if (activateOnFovReachedList != null && activateOnFovReachedList.Count >= 2)
        {
            bool playing = false;
            if (_hasCachedMusicStateForActivation)
            {
                playing = _cachedMusicWasPlayingForActivation;
                _hasCachedMusicStateForActivation = false; // one-shot cache
            }
            else
            {
                // Fallback: decide at activation time if caller didn't cache (e.g., manual key press).
                BackgroundMusicPlayer.TryIsMusicPlaying(out playing);
            }
            int index = playing ? 1 : 0;

            for (int i = 0; i < activateOnFovReachedList.Count; i++)
            {
                GameObject go = activateOnFovReachedList[i];
                if (go == null) continue;
                go.SetActive(i == index);
            }
        }
        else if (activateOnFovReachedLegacy != null)
        {
            // Backward compatibility for old scenes.
            activateOnFovReachedLegacy.SetActive(true);
        }

        if (stopMusicOnFovReached)
        {
            if (pauseInsteadOfStop) BackgroundMusicPlayer.TryFadeOutAndPause(musicFadeOutSeconds);
            else BackgroundMusicPlayer.TryFadeOutAndStop(musicFadeOutSeconds);
        }
    }

    /// <summary>
    /// Starts looking at the target (if not already looking). Useful for triggers/cutscenes.
    /// </summary>
    public void StartLookAtTarget()
    {
        if (_isLookingAtTarget) return;
        ToggleLookInternal(startLook: true);
    }

    /// <summary>
    /// Call this right when the end trigger is crossed to "lock in" the music-playing state.
    /// The activation (UI/gameobject) still happens when FOV is reached, but uses this cached decision.
    /// </summary>
    public void CacheMusicStateForFovReachedActivation()
    {
        BackgroundMusicPlayer.TryIsMusicPlaying(out _cachedMusicWasPlayingForActivation);
        _hasCachedMusicStateForActivation = true;
    }

    /// <summary>
    /// Returns to the previous look direction (if currently looking at the target).
    /// </summary>
    public void ReturnToPreviousLook()
    {
        if (!_isLookingAtTarget) return;
        ToggleLookInternal(startLook: false);
    }

    private void ToggleLook()
    {
        ToggleLookInternal(startLook: !_isLookingAtTarget);
    }

    private void ToggleLookInternal(bool startLook)
    {
        if (target == null) return;

        _from = transform.rotation;

        if (startLook)
        {
            // Save where we were looking so we can return on the next key press.
            _savedRotation = _from;
            if (_cam != null) _savedFov = _cam.fieldOfView;

            Vector3 dir = target.position - transform.position;
            if (yawOnly) dir.y = 0f;
            if (dir.sqrMagnitude < 0.000001f) return;

            _to = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _isLookingAtTarget = true;

            if (animateFov && _cam != null)
            {
                _fovFrom = _cam.fieldOfView;
                _fovTo = targetFovWhenLooking;
                _fovT = 0f;
                _fovAnimating = true;
                _pendingActivateOnFovReached = true;
            }
            else
            {
                _pendingActivateOnFovReached = true;
                OnFovAnimationFinished();
            }
        }
        else
        {
            // Return to the saved rotation.
            _to = _savedRotation;
            _isLookingAtTarget = false;
            _pendingActivateOnFovReached = false;

            if (animateFov && _cam != null)
            {
                _fovFrom = _cam.fieldOfView;
                _fovTo = _savedFov;
                _fovT = 0f;
                _fovAnimating = true;
            }
        }

        _t = 0f;
        _rotating = true;
    }
}

