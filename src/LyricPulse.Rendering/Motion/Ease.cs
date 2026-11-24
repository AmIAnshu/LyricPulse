namespace LyricPulse.Rendering.Motion;

/// <summary>
/// Canonical easing curves defined in MOTION_AND_STYLES.md section 4.3.
/// Departing animations use Ease.Exit (35-45% faster than Ease.Enter).
/// </summary>
public static class Ease
{
    /// <summary>cubic-bezier(0.16, 1.00, 0.30, 1.00) — Expo Out for entrances and reveals.</summary>
    public static float Enter(float t) => EvaluateCubicBezier(t, 0.16f, 1.00f, 0.30f, 1.00f);

    /// <summary>cubic-bezier(0.40, 0.00, 1.00, 1.00) — Quad/Cubic In for brisk departures.</summary>
    public static float Exit(float t) => EvaluateCubicBezier(t, 0.40f, 0.00f, 1.00f, 1.00f);

    /// <summary>cubic-bezier(0.22, 1.00, 0.36, 1.00) — Quint Out for active word highlight ramp.</summary>
    public static float Highlight(float t) => EvaluateCubicBezier(t, 0.22f, 1.00f, 0.36f, 1.00f);

    /// <summary>Continuous playback-synced linear progress clamped to [0, 1].</summary>
    public static float LinearProgress(float t) => float.IsFinite(t) ? Math.Clamp(t, 0f, 1f) : 0f;

    /// <summary>cubic-bezier(0.25, 0.10, 0.25, 1.00) — OKLab track-change palette morph.</summary>
    public static float ColorMorph(float t) => EvaluateCubicBezier(t, 0.25f, 0.10f, 0.25f, 1.00f);

    private static float EvaluateCubicBezier(float t, float x1, float y1, float x2, float y2)
    {
        if (!float.IsFinite(t) || t <= 0f)
        {
            return 0f;
        }

        if (t >= 1f)
        {
            return 1f;
        }

        var u = (double)t;
        for (var i = 0; i < 6; i++)
        {
            var x = EvaluateAxis(u, x1, x2) - t;
            var dx = EvaluateDerivative(u, x1, x2);
            if (Math.Abs(dx) < 1e-6)
            {
                break;
            }

            u = Math.Clamp(u - (x / dx), 0.0, 1.0);
        }

        return (float)Math.Clamp(EvaluateAxis(u, y1, y2), 0.0, 1.0);
    }

    private static double EvaluateAxis(double u, double p1, double p2)
    {
        var inv = 1.0 - u;
        return (3.0 * inv * inv * u * p1) + (3.0 * inv * u * u * p2) + (u * u * u);
    }

    private static double EvaluateDerivative(double u, double p1, double p2)
    {
        var inv = 1.0 - u;
        return (3.0 * inv * inv * p1) + (6.0 * inv * u * (p2 - p1)) + (3.0 * u * u * (1.0 - p2));
    }
}
