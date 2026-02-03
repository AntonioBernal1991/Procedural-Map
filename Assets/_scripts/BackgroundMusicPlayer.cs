using UnityEngine;

/// <summary>
/// Simple background music player (looped) with optional persistence across scenes.
/// Add this to any GameObject (e.g., Main Camera or an empty "Audio" object).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class BackgroundMusicPlayer : MonoBehaviour
{
    private static BackgroundMusicPlayer _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Handles Enter Play Mode Options (Domain Reload disabled).
        _instance = null;
    }

    [Header("Music")]
    [SerializeField] private AudioClip musicClip;
    [SerializeField] [Range(0f, 1f)] private float volume = 0.5f;
    [SerializeField] private bool loop = true;
    [Tooltip("Start playback from this timestamp (seconds). Useful to start at the 'drop'/kick.")]
    [SerializeField] [Min(0f)] private float startAtSeconds = 0f;

    [Header("Lifecycle")]
    [Tooltip("Keep playing when loading other scenes. Prevents duplicates by name.")]
    [SerializeField] private bool dontDestroyOnLoad = true;

    private AudioSource _source;
    private Coroutine _fadeRoutine;
    private Coroutine _panRoutine;
    private float _defaultPanStereo;

    /// <summary>
    /// Stops the music on the active BackgroundMusicPlayer instance (if any).
    /// Returns true if an instance existed and was told to stop.
    /// </summary>
    public static bool TryStopMusic()
    {
        if (_instance == null) return false;
        _instance.StopMusic();
        return true;
    }

    /// <summary>
    /// Fades out the music volume to 0 over <paramref name="durationSeconds"/> and then stops playback.
    /// Returns true if an instance existed and was told to fade out.
    /// </summary>
    public static bool TryFadeOutAndStop(float durationSeconds)
    {
        if (_instance == null) return false;
        _instance.FadeOutAndStop(durationSeconds);
        return true;
    }

    /// <summary>
    /// Pauses the music on the active BackgroundMusicPlayer instance (if any).
    /// Returns true if an instance existed and was told to pause.
    /// </summary>
    public static bool TryPauseMusic()
    {
        if (_instance == null) return false;
        _instance.PauseMusic();
        return true;
    }

    /// <summary>
    /// Fades out the music volume to 0 over <paramref name="durationSeconds"/> and then pauses playback.
    /// Returns true if an instance existed and was told to fade out.
    /// </summary>
    public static bool TryFadeOutAndPause(float durationSeconds)
    {
        if (_instance == null) return false;
        _instance.FadeOutAndPause(durationSeconds);
        return true;
    }

    /// <summary>
    /// Sets the stereo pan of the active BackgroundMusicPlayer instance (if any).
    /// -1 = full left, +1 = full right.
    /// Returns true if an instance existed and was updated.
    /// </summary>
    public static bool TrySetPanStereo(float panStereo)
    {
        if (_instance == null) return false;
        _instance.SetPanStereo(panStereo);
        return true;
    }

    /// <summary>
    /// Smoothly pans the music to <paramref name="panStereo"/> over <paramref name="durationSeconds"/>.
    /// -1 = full left, +1 = full right.
    /// Returns true if an instance existed and was updated.
    /// </summary>
    public static bool TrySetPanStereo(float panStereo, float durationSeconds)
    {
        if (_instance == null) return false;
        _instance.SetPanStereo(panStereo, durationSeconds);
        return true;
    }

    /// <summary>
    /// Restores the stereo pan to the value this component had on Awake.
    /// Returns true if an instance existed and was updated.
    /// </summary>
    public static bool TryResetPanStereo()
    {
        if (_instance == null) return false;
        _instance.ResetPanStereo();
        return true;
    }

    /// <summary>
    /// Gets the current stereo pan of the active BackgroundMusicPlayer instance (if any).
    /// Returns true if an instance existed and a value was returned.
    /// </summary>
    public static bool TryGetPanStereo(out float panStereo)
    {
        if (_instance == null)
        {
            panStereo = 0f;
            return false;
        }

        panStereo = _instance.GetPanStereo();
        return true;
    }

    /// <summary>
    /// Returns whether the active BackgroundMusicPlayer AudioSource is currently playing.
    /// Returns true if an instance existed and a value was returned.
    /// </summary>
    public static bool TryIsMusicPlaying(out bool isPlaying)
    {
        if (_instance == null)
        {
            isPlaying = false;
            return false;
        }

        isPlaying = _instance.IsMusicPlaying();
        return true;
    }

    /// <summary>
    /// Stops playback for this component's AudioSource.
    /// </summary>
    public void StopMusic()
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
        if (_source != null) _source.Stop();
    }

    /// <summary>
    /// Pauses playback for this component's AudioSource.
    /// </summary>
    public void PauseMusic()
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
        if (_source != null) _source.Pause();
    }

    /// <summary>
    /// Fade out to silence and stop.
    /// </summary>
    public void FadeOutAndStop(float durationSeconds)
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_source == null) return;
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeOutRoutine(durationSeconds, stopAtEnd: true));
    }

    /// <summary>
    /// Fade out to silence and pause.
    /// </summary>
    public void FadeOutAndPause(float durationSeconds)
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_source == null) return;
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeOutRoutine(durationSeconds, stopAtEnd: false));
    }

    /// <summary>
    /// Sets pan on this component's AudioSource.
    /// -1 = full left, +1 = full right.
    /// </summary>
    public void SetPanStereo(float panStereo)
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_source == null) return;
        if (_panRoutine != null) { StopCoroutine(_panRoutine); _panRoutine = null; }
        _source.panStereo = Mathf.Clamp(panStereo, -1f, 1f);
    }

    /// <summary>
    /// Smoothly pans this component's AudioSource to <paramref name="panStereo"/> over <paramref name="durationSeconds"/>.
    /// </summary>
    public void SetPanStereo(float panStereo, float durationSeconds)
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_source == null) return;

        float d = Mathf.Max(0f, durationSeconds);
        float target = Mathf.Clamp(panStereo, -1f, 1f);
        if (d <= 0f)
        {
            SetPanStereo(target);
            return;
        }

        if (_panRoutine != null) StopCoroutine(_panRoutine);
        _panRoutine = StartCoroutine(PanRoutine(target, d));
    }

    /// <summary>
    /// Restores pan to the value captured on Awake.
    /// </summary>
    public void ResetPanStereo()
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_source == null) return;
        if (_panRoutine != null) { StopCoroutine(_panRoutine); _panRoutine = null; }
        _source.panStereo = Mathf.Clamp(_defaultPanStereo, -1f, 1f);
    }

    private System.Collections.IEnumerator PanRoutine(float targetPan, float durationSeconds)
    {
        if (_source == null) yield break;

        float start = Mathf.Clamp(_source.panStereo, -1f, 1f);
        float d = Mathf.Max(0.0001f, durationSeconds);
        float t = 0f;
        while (t < d && _source != null)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / d);
            _source.panStereo = Mathf.Lerp(start, targetPan, a);
            yield return null;
        }

        if (_source != null) _source.panStereo = targetPan;
        _panRoutine = null;
    }

    public float GetPanStereo()
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_source == null) return 0f;
        return Mathf.Clamp(_source.panStereo, -1f, 1f);
    }

    public bool IsMusicPlaying()
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        return _source != null && _source.isPlaying;
    }

    private System.Collections.IEnumerator FadeOutRoutine(float durationSeconds, bool stopAtEnd)
    {
        if (_source == null) yield break;

        float startVol = _source.volume;
        float d = Mathf.Max(0f, durationSeconds);
        if (d <= 0f)
        {
            _source.volume = 0f;
            if (stopAtEnd) _source.Stop();
            else _source.Pause();
            _fadeRoutine = null;
            yield break;
        }

        float t = 0f;
        while (t < d && _source != null)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / d);
            _source.volume = Mathf.Lerp(startVol, 0f, a);
            yield return null;
        }

        if (_source != null)
        {
            _source.volume = 0f;
            if (stopAtEnd) _source.Stop();
            else _source.Pause();
        }

        _fadeRoutine = null;
    }

    private void Awake()
    {
        if (dontDestroyOnLoad)
        {
            // Prevent duplicates safely.
            if (_instance != null && _instance != this)
            {
                // If the existing instance has no clip but this one does, copy it over.
                if (_instance.musicClip == null && musicClip != null)
                {
                    _instance.musicClip = musicClip;
                    if (_instance._source != null)
                    {
                        _instance._source.clip = musicClip;
                        if (!_instance._source.isPlaying) _instance._source.Play();
                    }
                }
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        _source = GetComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = loop;
        _source.volume = volume;
        _defaultPanStereo = _source.panStereo;

        // Don't wipe the AudioSource clip if the user assigned it there.
        if (musicClip == null && _source.clip != null)
        {
            musicClip = _source.clip;
        }
        else if (musicClip != null)
        {
            _source.clip = musicClip;
        }

        if (_source.clip != null)
        {
            if (startAtSeconds > 0f && _source.clip.length > 0f)
            {
                _source.time = Mathf.Clamp(startAtSeconds, 0f, Mathf.Max(0f, _source.clip.length - 0.01f));
            }
            _source.Play();
        }
    }
    
    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void OnValidate()
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_source == null) return;

        _source.loop = loop;
        _source.volume = volume;
        // Only overwrite the AudioSource if a clip is assigned in this component.
        if (musicClip != null)
        {
            _source.clip = musicClip;
        }
    }
}

