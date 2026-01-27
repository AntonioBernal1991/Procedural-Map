using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on the player camera (or its root).
/// When the camera enters a TurnCueMarker trigger, it fires UnityEvents you can hook to music changes.
/// </summary>
[DisallowMultipleComponent]
public class TurnCueReceiver : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private UnityEvent onEnterTurnCue;
    [SerializeField] private UnityEvent onExitTurnCue;

    [Header("Music Pan (optional)")]
    [Tooltip("If true, entering a TurnCueMarker will set the BackgroundMusicPlayer stereo pan based on the marker settings.")]
    [SerializeField] private bool applyMusicPanFromMarker = true;
    [Tooltip("Seconds to interpolate between pan values (Left <-> Right). 0 = instant.")]
    [SerializeField] [Min(0f)] private float panTransitionSeconds = 0.15f;

    [Header("Haptics (optional)")]
    [Tooltip("If true, trigger haptics when entering/exiting turn cues.")]
    [SerializeField] private bool enableHaptics = false;
    [Tooltip("If true, logs when haptics fire (useful for debugging on device).")]
    [SerializeField] private bool logHaptics = false;
    [SerializeField] private bool vibrateOnEnter = true;
    [SerializeField] private bool vibrateOnExit = false;
    [Tooltip("Optional gamepad rumble duration (requires new Input System enabled).")]
    [SerializeField] [Min(0f)] private float rumbleSeconds = 0.12f;
    [Tooltip("Low-frequency motor (0..1). Requires new Input System enabled.")]
    [SerializeField] [Range(0f, 1f)] private float rumbleLow = 0.3f;
    [Tooltip("High-frequency motor (0..1). Requires new Input System enabled.")]
    [SerializeField] [Range(0f, 1f)] private float rumbleHigh = 0.6f;

    [Tooltip("Optional: also fade out music when entering a cue.")]
    [SerializeField] private bool fadeOutMusicOnEnter = false;

    [SerializeField] private float musicFadeOutSeconds = 0.5f;

    private readonly HashSet<TurnCueMarker> _consumedOneShots = new HashSet<TurnCueMarker>();
    private readonly HashSet<TurnCueMarker> _activePanMarkers = new HashSet<TurnCueMarker>();
    private bool _hasPanBaseline;
    private float _panBaseline;

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        TurnCueMarker marker = other.GetComponent<TurnCueMarker>();
        if (marker == null) return;

        if (marker.OneShot && _consumedOneShots.Contains(marker)) return;

        if (marker.OneShot) _consumedOneShots.Add(marker);

        if (applyMusicPanFromMarker)
        {
            // Enter activates: set pan while we're inside at least one cue.
            // Cache the previous pan the first time we enter any cue, so exit can restore it.
            if (_activePanMarkers.Add(marker))
            {
                if (!_hasPanBaseline)
                {
                    if (BackgroundMusicPlayer.TryGetPanStereo(out float current))
                    {
                        _panBaseline = current;
                        _hasPanBaseline = true;
                    }
                }
                BackgroundMusicPlayer.TrySetPanStereo(marker.MusicPanStereo, panTransitionSeconds);
            }
        }

        if (enableHaptics && vibrateOnEnter)
        {
            if (logHaptics) Debug.Log($"VIBRACION (Enter) marker='{marker.name}' pan={marker.MusicPanStereo:0.00}", this);
            Haptics.Vibrate(rumbleSeconds);
            Haptics.Rumble(this, rumbleSeconds, rumbleLow, rumbleHigh);
        }

        onEnterTurnCue?.Invoke();

        if (fadeOutMusicOnEnter)
        {
            BackgroundMusicPlayer.TryFadeOutAndPause(musicFadeOutSeconds);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null) return;

        TurnCueMarker marker = other.GetComponent<TurnCueMarker>();
        if (marker == null) return;

        if (applyMusicPanFromMarker && marker.ResetMusicPanOnExit)
        {
            // Exit deactivates: restore previous pan when leaving the last active cue.
            if (_activePanMarkers.Remove(marker))
            {
                if (_activePanMarkers.Count == 0)
                {
                    if (_hasPanBaseline)
                    {
                        BackgroundMusicPlayer.TrySetPanStereo(_panBaseline, panTransitionSeconds);
                    }
                    else
                    {
                        BackgroundMusicPlayer.TryResetPanStereo();
                    }
                    _hasPanBaseline = false;
                }
            }
        }

        if (enableHaptics && vibrateOnExit)
        {
            if (logHaptics) Debug.Log($"VIBRACION (Exit) marker='{marker.name}'", this);
            Haptics.Vibrate(rumbleSeconds);
            Haptics.Rumble(this, rumbleSeconds, rumbleLow, rumbleHigh);
        }

        onExitTurnCue?.Invoke();
    }
}

