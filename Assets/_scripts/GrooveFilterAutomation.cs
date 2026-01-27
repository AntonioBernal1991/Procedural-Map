using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Smooth "groove improvement" cue: automates a filter/EQ-like parameter for a few seconds,
/// then returns to the base value. Designed to feel subtle (no stops, stutters, or silences).
///
/// Hook this from TurnCueReceiver.onEnterTurnCue (UnityEvent).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class GrooveFilterAutomation : MonoBehaviour
{
    public enum AutomationMode
    {
        /// <summary>Automate an exposed AudioMixer float parameter (recommended).</summary>
        AudioMixerFloat = 0,
        /// <summary>Automate an AudioLowPassFilter cutoff frequency on this GameObject.</summary>
        AudioLowPassCutoff = 1
    }

    [Header("Mode")]
    [SerializeField] private AutomationMode mode = AutomationMode.AudioLowPassCutoff;

    [Header("Timing (seconds)")]
    [SerializeField] [Min(0f)] private float attackSeconds = 0.35f;
    [SerializeField] [Min(0f)] private float holdSeconds = 1.75f;
    [SerializeField] [Min(0f)] private float releaseSeconds = 0.9f;

    [Tooltip("Curve used for attack/release. X = normalized time (0..1), Y = normalized value (0..1).")]
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("AudioMixer (Mode = AudioMixerFloat)")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private string exposedFloatName = "Music_Brightness";
    [Tooltip("Base value for the exposed float (example: cutoff Hz if you set it up that way, or dB, etc.).")]
    [SerializeField] private float baseMixerValue = 0f;
    [Tooltip("Cue value for the exposed float (opened/brighter).")]
    [SerializeField] private float cueMixerValue = 1f;

    [Header("LowPass (Mode = AudioLowPassCutoff)")]
    [Tooltip("If missing, one will be added automatically in Awake.")]
    [SerializeField] private AudioLowPassFilter lowPass;
    [Tooltip("Baseline cutoff (Hz). Lower = darker. Typical subtle baseline: 6000-12000.")]
    [SerializeField] [Range(10f, 22000f)] private float baseCutoffHz = 9000f;
    [Tooltip("Cue cutoff (Hz). Higher = brighter. Usually 18000-22000.")]
    [SerializeField] [Range(10f, 22000f)] private float cueCutoffHz = 22000f;

    private enum Phase { Idle, Attack, Hold, Release }
    private Phase _phase = Phase.Idle;
    private float _lastNormalized;
    private float _lastAppliedCutoffHz;
    private float _lastAppliedMixerValue;
    private bool _holdWhileInsideTrigger;

    private Coroutine _routine;

    // Extremely low cutoff values (e.g., 10Hz) will make the music sound like it "dies".
    // Keep a practical minimum to avoid accidental misconfiguration while tuning.
    private const float PracticalMinCutoffHz = 200f;

    public AutomationMode Mode => mode;
    public float AttackSeconds => attackSeconds;
    public float HoldSeconds => holdSeconds;
    public float ReleaseSeconds => releaseSeconds;
    public float BaseCutoffHz => baseCutoffHz;
    public float CueCutoffHz => cueCutoffHz;
    public string ExposedFloatName => exposedFloatName;
    public float BaseMixerValue => baseMixerValue;
    public float CueMixerValue => cueMixerValue;
    public float LastNormalized => _lastNormalized;
    public float LastAppliedCutoffHz => _lastAppliedCutoffHz;
    public float LastAppliedMixerValue => _lastAppliedMixerValue;

    public string GetDebugSummary()
    {
        if (mode == AutomationMode.AudioLowPassCutoff)
        {
            return $"mode=LowPass phase={_phase} base={baseCutoffHz:0}Hz cue={cueCutoffHz:0}Hz now={_lastAppliedCutoffHz:0}Hz n={_lastNormalized:0.00} A/H/R={attackSeconds:0.00}/{holdSeconds:0.00}/{releaseSeconds:0.00}";
        }

        return $"mode=Mixer phase={_phase} '{exposedFloatName}' base={baseMixerValue:0.###} cue={cueMixerValue:0.###} now={_lastAppliedMixerValue:0.###} n={_lastNormalized:0.00} A/H/R={attackSeconds:0.00}/{holdSeconds:0.00}/{releaseSeconds:0.00}";
    }

    private void Awake()
    {
        if (mode == AutomationMode.AudioLowPassCutoff)
        {
            baseCutoffHz = Mathf.Clamp(baseCutoffHz, PracticalMinCutoffHz, 22000f);
            cueCutoffHz = Mathf.Clamp(cueCutoffHz, PracticalMinCutoffHz, 22000f);

            if (lowPass == null) lowPass = GetComponent<AudioLowPassFilter>();
            if (lowPass == null) lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.enabled = true;
            lowPass.cutoffFrequency = Mathf.Clamp(baseCutoffHz, 10f, 22000f);
            _lastAppliedCutoffHz = lowPass.cutoffFrequency;
        }
        else
        {
            // Try to auto-find an AudioMixer from the AudioSource output group (optional convenience).
            if (mixer == null)
            {
                AudioSource src = GetComponent<AudioSource>();
                if (src != null && src.outputAudioMixerGroup != null && src.outputAudioMixerGroup.audioMixer != null)
                {
                    mixer = src.outputAudioMixerGroup.audioMixer;
                }
            }

            ApplyMixerValue(baseMixerValue);
            _lastAppliedMixerValue = baseMixerValue;
        }
    }

    /// <summary>
    /// Call this (e.g., from TurnCueReceiver) to perform the subtle "open filter" cue.
    /// If called repeatedly, it restarts cleanly (no stacking spikes).
    /// </summary>
    [ContextMenu("Trigger Cue")]
    public void TriggerCue()
    {
        if (!isActiveAndEnabled) return;
        _holdWhileInsideTrigger = false;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(CueRoutine());
    }

    /// <summary>
    /// ENTER behavior for "only while inside trigger":
    /// ramps up to cue and then HOLDS until you call ExitCueRelease().
    /// </summary>
    public void EnterCueHold()
    {
        if (!isActiveAndEnabled) return;
        _holdWhileInsideTrigger = true;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(AttackThenHoldRoutine());
    }

    /// <summary>
    /// EXIT behavior for "only while inside trigger":
    /// releases back to base from the CURRENT value.
    /// </summary>
    public void ExitCueRelease()
    {
        if (!isActiveAndEnabled) return;
        _holdWhileInsideTrigger = false;

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ReleaseFromCurrentRoutine());
    }

    [ContextMenu("Reset To Base")]
    public void ResetToBase()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        _holdWhileInsideTrigger = false;
        ApplyNormalized(0f);
    }

    private IEnumerator CueRoutine()
    {
        _phase = Phase.Attack;
        // Attack (base -> cue)
        if (attackSeconds <= 0f)
        {
            ApplyNormalized(1f);
        }
        else
        {
            float t = 0f;
            while (t < attackSeconds)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / Mathf.Max(0.0001f, attackSeconds));
                ApplyNormalized(EvaluateEase(a));
                yield return null;
            }
            ApplyNormalized(1f);
        }

        // Hold
        _phase = Phase.Hold;
        if (holdSeconds > 0f)
        {
            float t = 0f;
            while (t < holdSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        // Release (cue -> base)
        _phase = Phase.Release;
        if (releaseSeconds <= 0f)
        {
            ApplyNormalized(0f);
        }
        else
        {
            float t = 0f;
            while (t < releaseSeconds)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / Mathf.Max(0.0001f, releaseSeconds));
                ApplyNormalized(1f - EvaluateEase(a));
                yield return null;
            }
            ApplyNormalized(0f);
        }

        _phase = Phase.Idle;
        _routine = null;
    }

    private IEnumerator AttackThenHoldRoutine()
    {
        // Attack to cue, then hold indefinitely while _holdWhileInsideTrigger is true.
        _phase = Phase.Attack;
        if (attackSeconds <= 0f)
        {
            ApplyNormalized(1f);
        }
        else
        {
            float t = 0f;
            while (t < attackSeconds)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / Mathf.Max(0.0001f, attackSeconds));
                ApplyNormalized(EvaluateEase(a));
                yield return null;
            }
            ApplyNormalized(1f);
        }

        _phase = Phase.Hold;
        while (_holdWhileInsideTrigger)
        {
            // Stay at cue while inside trigger.
            ApplyNormalized(1f);
            yield return null;
        }

        // If we were told to stop holding, release back to base.
        yield return ReleaseFromCurrentRoutine();
    }

    private IEnumerator ReleaseFromCurrentRoutine()
    {
        _phase = Phase.Release;

        float startN = Mathf.Clamp01(_lastNormalized);
        if (releaseSeconds <= 0f)
        {
            ApplyNormalized(0f);
        }
        else
        {
            float t = 0f;
            while (t < releaseSeconds)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / Mathf.Max(0.0001f, releaseSeconds));
                float eased = EvaluateEase(a);
                ApplyNormalized(Mathf.Lerp(startN, 0f, eased));
                yield return null;
            }
            ApplyNormalized(0f);
        }

        _phase = Phase.Idle;
        _routine = null;
    }

    private float EvaluateEase(float x)
    {
        if (ease == null) return x;
        if (ease.length == 0) return x;
        return Mathf.Clamp01(ease.Evaluate(Mathf.Clamp01(x)));
    }

    private void ApplyNormalized(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        _lastNormalized = normalized;

        if (mode == AutomationMode.AudioLowPassCutoff)
        {
            if (lowPass == null) return;
            float baseHz = Mathf.Clamp(baseCutoffHz, PracticalMinCutoffHz, 22000f);
            float cueHz = Mathf.Clamp(cueCutoffHz, PracticalMinCutoffHz, 22000f);
            float hz = Mathf.Lerp(baseHz, cueHz, normalized);
            lowPass.cutoffFrequency = Mathf.Clamp(hz, 10f, 22000f);
            _lastAppliedCutoffHz = lowPass.cutoffFrequency;
            return;
        }

        float v = Mathf.Lerp(baseMixerValue, cueMixerValue, normalized);
        ApplyMixerValue(v);
        _lastAppliedMixerValue = v;
    }

    private void ApplyMixerValue(float value)
    {
        if (mixer == null) return;
        if (string.IsNullOrWhiteSpace(exposedFloatName)) return;
        mixer.SetFloat(exposedFloatName, value);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Keep values sane.
        baseCutoffHz = Mathf.Clamp(baseCutoffHz, PracticalMinCutoffHz, 22000f);
        cueCutoffHz = Mathf.Clamp(cueCutoffHz, PracticalMinCutoffHz, 22000f);

        // Preview in edit mode without needing to press Play.
        if (!Application.isPlaying)
        {
            if (mode == AutomationMode.AudioLowPassCutoff)
            {
                if (lowPass == null) lowPass = GetComponent<AudioLowPassFilter>();
                if (lowPass == null) lowPass = gameObject.AddComponent<AudioLowPassFilter>();
                if (lowPass != null)
                {
                    lowPass.enabled = true;
                    lowPass.cutoffFrequency = baseCutoffHz;
                }
            }
        }
    }
#endif
}

