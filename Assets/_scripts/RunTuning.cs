using UnityEngine;

/// <summary>
/// Per-run parameters you can tweak in assets and apply at runtime when a run/scene loads.
/// Create one asset per run (Run0, Run1, ...), then assign them in GameManager by index.
/// </summary>
[CreateAssetMenu(menuName = "Game/Run Tuning", fileName = "RunTuning_Run0")]
public class RunTuning : ScriptableObject
{
    [Header("AutoForwardCameraController (optional overrides)")]

    [Tooltip("If enabled, overrides AutoForwardCameraController move speed for this run.")]
    public bool overrideMoveSpeed = true;

    [Tooltip("Forward movement speed (units/sec).")]
    [Min(0f)] public float moveSpeed = 3.5f;

    [Tooltip("If enabled, overrides AutoForwardCameraController stop distance for this run.")]
    public bool overrideStopDistance = false;

    [Tooltip("How close the controller gets before considering the path blocked.")]
    [Min(0f)] public float stopDistance = 0.15f;

    [Tooltip("If enabled, overrides AutoForwardCameraController turn duration for this run.")]
    public bool overrideTurnDuration = false;

    [Tooltip("Seconds to complete a 90-degree turn. 0 = instant.")]
    [Min(0f)] public float turnDuration = 0.12f;

    [Header("Music (BackgroundMusicPlayer) (optional overrides)")]

    [Tooltip("If enabled, overrides the music clip for this run (BackgroundMusicPlayer).")]
    public bool overrideMusicClip = false;

    [Tooltip("Music clip to play for this run. If null, keeps whatever BackgroundMusicPlayer already has.")]
    public AudioClip musicClip;

    [Tooltip("If enabled, overrides the seconds timestamp where music starts for this run (BackgroundMusicPlayer).")]
    public bool overrideMusicStartAtSeconds = false;

    [Tooltip("Start playback from this timestamp (seconds). Useful to start at the 'drop'/kick.")]
    [Min(0f)] public float musicStartAtSeconds = 0f;
}

