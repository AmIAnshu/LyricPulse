using SkiaSharp;

namespace LyricPulse.Rendering.Shaders;

/// <summary>
/// Pure uniform math helpers for Elliptical Bulge comfort guardrails and
/// 13-tap Directional Motion Blur velocity derivation, dead-zone bypass, and tanh saturation clamping.
/// </summary>
public static class MotionBlurMath
{
    public const float ReferenceRefreshHz = 60.0f;
    public const float DeadZoneMinPxAt100Dpi = 1.25f;
    public const float MaxBlurPxAt100Dpi = 22.0f;

    public const float BaselineBulgeStrength = 0.24f;
    /// <summary>Monotonic convex lens ceiling (f'(1) = 1 - 0.30 * 3.10 = +0.07 &gt; 0, zero fold-back).</summary>
    public const float MaxBulgeStrength = 0.30f;
    public const float DefaultBulgePower = 2.10f;
    public const float MinBulgePower = 1.85f;
    public const float MaxBulgePower = 2.35f;

    public static float ClampBulgeStrength(float strength) =>
        float.IsFinite(strength) ? Math.Clamp(MathF.Abs(strength), 0.0f, MaxBulgeStrength) : 0.0f;

    public static float ClampBulgePower(float power) =>
        float.IsFinite(power) ? Math.Clamp(power, MinBulgePower, MaxBulgePower) : DefaultBulgePower;

    /// <summary>
    /// Computes the physical-pixel shutter travel vector for DirectionalMotionBlur13.sksl.
    /// Uses a fixed 60Hz reference divisor so 144Hz monitors retain consistent cinematic blur,
    /// applies a dead-zone bypass below 1.25 * dpiScale, and saturates via hyperbolic tanh &lt;= 22.0 * dpiScale.
    /// </summary>
    public static SKPoint ComputeClampedVelocity(
        SKPoint velocityPxPerSec,
        float shutterFactor,
        float dpiScale,
        out bool shouldBypass)
    {
        var safeDpi = float.IsFinite(dpiScale) && dpiScale > 0f ? dpiScale : 1.0f;
        if (!float.IsFinite(shutterFactor) || shutterFactor <= 0f ||
            !float.IsFinite(velocityPxPerSec.X) || !float.IsFinite(velocityPxPerSec.Y))
        {
            shouldBypass = true;
            return SKPoint.Empty;
        }

        var rawX = (velocityPxPerSec.X / ReferenceRefreshHz) * shutterFactor;
        var rawY = (velocityPxPerSec.Y / ReferenceRefreshHz) * shutterFactor;
        var magnitude = MathF.Sqrt((rawX * rawX) + (rawY * rawY));

        var deadZone = DeadZoneMinPxAt100Dpi * safeDpi;
        if (magnitude < deadZone)
        {
            shouldBypass = true;
            return SKPoint.Empty;
        }

        var maxBlurPx = MaxBlurPxAt100Dpi * safeDpi;
        var clampedMagnitude = maxBlurPx * MathF.Tanh(magnitude / maxBlurPx);
        var scale = clampedMagnitude / magnitude;

        shouldBypass = false;
        return new SKPoint(rawX * scale, rawY * scale);
    }

    public static (int PadXPx, int PadYPx) ComputeDirectionalPaddingPx(SKPoint clampedVelocity)
    {
        var padX = (int)MathF.Ceiling(MathF.Abs(clampedVelocity.X) * 0.5f) + 4;
        var padY = (int)MathF.Ceiling(MathF.Abs(clampedVelocity.Y) * 0.5f) + 4;
        return (padX, padY);
    }
}
