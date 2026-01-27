using System.Collections;
using UnityEngine;

/// <summary>
/// Minimal haptics helper:
/// - Mobile: Handheld.Vibrate()
/// - Gamepad rumble: only if the new Input System is enabled (ENABLE_INPUT_SYSTEM).
/// </summary>
public static class Haptics
{
    /// <summary>
    /// Vibrate on mobile devices.
    /// On Android, tries to vibrate for the requested duration (if possible).
    /// On iOS, falls back to the system default vibration.
    /// </summary>
    public static void Vibrate(float durationSeconds = 0.12f)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (TryAndroidVibrate(durationSeconds)) return;
#endif
        // Fallback: standard Unity vibration (no duration control).
        if (Application.isMobilePlatform)
        {
            Handheld.Vibrate();
        }
    }

    public static void VibrateOnce() => Vibrate(0.12f);

    public static void Rumble(MonoBehaviour host, float durationSeconds, float lowFrequency, float highFrequency)
    {
#if ENABLE_INPUT_SYSTEM
        if (host == null) return;
        host.StartCoroutine(RumbleRoutine(durationSeconds, lowFrequency, highFrequency));
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static bool TryAndroidVibrate(float durationSeconds)
    {
        try
        {
            int ms = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0f, durationSeconds) * 1000f), 1, 5000);

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity == null) return false;

                using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    if (vibrator == null) return false;
                    bool hasVibrator = true;
                    try { hasVibrator = vibrator.Call<bool>("hasVibrator"); } catch { /* older devices */ }
                    if (!hasVibrator) return false;

                    int sdkInt = 0;
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    {
                        sdkInt = version.GetStatic<int>("SDK_INT");
                    }

                    if (sdkInt >= 26)
                    {
                        using (var vibrationEffect = new AndroidJavaClass("android.os.VibrationEffect"))
                        {
                            int defaultAmp = vibrationEffect.GetStatic<int>("DEFAULT_AMPLITUDE");
                            using (var effect = vibrationEffect.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, defaultAmp))
                            {
                                vibrator.Call("vibrate", effect);
                            }
                        }
                    }
                    else
                    {
                        vibrator.Call("vibrate", (long)ms);
                    }

                    return true;
                }
            }
        }
        catch
        {
            return false;
        }
    }
#endif

#if ENABLE_INPUT_SYSTEM
    private static IEnumerator RumbleRoutine(float durationSeconds, float lowFrequency, float highFrequency)
    {
        // Only compiled if the new Input System is present & enabled.
        float d = Mathf.Max(0f, durationSeconds);
        float low = Mathf.Clamp01(lowFrequency);
        float high = Mathf.Clamp01(highFrequency);

        var gamepad = UnityEngine.InputSystem.Gamepad.current;
        if (gamepad == null) yield break;

        gamepad.SetMotorSpeeds(low, high);

        if (d > 0f)
        {
            yield return new WaitForSeconds(d);
        }

        // Stop rumble.
        gamepad.SetMotorSpeeds(0f, 0f);
    }
#endif
}

