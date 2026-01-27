using UnityEngine;

/// <summary>
/// Applies safe, high-impact performance settings at runtime for mobile builds.
/// Helps avoid accidental "PC-like" quality settings on Android/iOS (AA, probes, etc.).
/// </summary>
public static class MobilePerformanceBootstrap
{
    // Change these defaults if you want different behavior.
    private const int TargetFpsMobile = 60;
    // 1080x1920 portrait is ~2.07M pixels; scaling to 0.75 is ~810x1440 (~1.17M pixels), a big GPU win (with more blur).
    private const float RenderScaleMobile = 0.75f; // 1.0 = native resolution
    private const bool UseRenderScale = true;      // enable resolution scaling for mobile by default

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        if (!Application.isMobilePlatform) return;

        // Avoid platform/quality vSync caps. (If you want vSync on mobile, remove this.)
        QualitySettings.vSyncCount = 0;

        // Request a stable refresh target. Some devices may still clamp due to OS power mode.
        Application.targetFrameRate = TargetFpsMobile;

        // Big mobile wins:
        QualitySettings.antiAliasing = 0;
        QualitySettings.pixelLightCount = 0;
        QualitySettings.realtimeReflectionProbes = false;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;

        // You already disable shadows on the map renderers, but keep global shadow distance low too.
        QualitySettings.shadowDistance = 0f;

        // Optional: lower internal render resolution.
        if (UseRenderScale)
        {
            float s = Mathf.Clamp(RenderScaleMobile, 0.5f, 1.0f);
            // Built-in pipeline mobile scaling hook:
            QualitySettings.resolutionScalingFixedDPIFactor = s;

            // Dynamic resolution path:
            // Unity 2022 doesn't expose a simple "isSupported" flag, so we just try this call.
            // On unsupported setups it may do nothing or throw; either way, fixed DPI scaling above still helps.
            try
            {
                ScalableBufferManager.ResizeBuffers(s, s);
            }
            catch
            {
                // ignore
            }
        }
    }
}

