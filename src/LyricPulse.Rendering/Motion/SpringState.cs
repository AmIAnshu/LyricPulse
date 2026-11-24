namespace LyricPulse.Rendering.Motion;

/// <summary>
/// Immutable damped harmonic oscillator state evaluated via closed-form analytical integration
/// so 60Hz, 120Hz, 144Hz, and 240Hz monitors trace identical curves with zero timestep drift.
/// </summary>
public readonly record struct SpringState(float Value, float Velocity, float Target)
{
    public const float MaxDeltaSeconds = 1.0f / 30.0f;
    public const float RestDisplacementThreshold = 0.001f;
    public const float RestVelocityThreshold = 0.002f;

    public bool IsAtRest =>
        MathF.Abs(Value - Target) < RestDisplacementThreshold &&
        MathF.Abs(Velocity) < RestVelocityThreshold;

    public static SpringState AtRest(float value) => new(value, 0f, value);

    public SpringState WithTarget(float newTarget) => this with { Target = newTarget };

    public SpringState WithImpulse(float deltaVelocity) =>
        this with { Velocity = Velocity + (float.IsFinite(deltaVelocity) ? deltaVelocity : 0f) };

    public SpringState SnapToTarget() => new(Target, 0f, Target);

    /// <summary>
    /// Advances the spring analytically by <paramref name="rawDeltaSeconds"/> (clamped to &lt;= 33.33ms),
    /// returning a new immutable <see cref="SpringState"/>.
    /// </summary>
    public SpringState Step(SpringParams parameters, float rawDeltaSeconds)
    {
        if (!float.IsFinite(rawDeltaSeconds) || rawDeltaSeconds <= 0f)
        {
            return this;
        }

        if (IsAtRest)
        {
            return SnapToTarget();
        }

        var dt = (double)Math.Clamp(rawDeltaSeconds, 0f, MaxDeltaSeconds);
        double k = parameters.SafeStiffness;
        double c = parameters.SafeDamping;
        double m = parameters.SafeMass;

        var omega0 = Math.Sqrt(k / m);
        var zeta = c / (2.0 * Math.Sqrt(k * m));

        var y0 = (double)Value - Target;
        var v0 = (double)Velocity;

        double y;
        double v;

        if (zeta < 1.0 - 1e-4)
        {
            var omegaD = omega0 * Math.Sqrt(1.0 - (zeta * zeta));
            var decay = Math.Exp(-zeta * omega0 * dt);
            var cos = Math.Cos(omegaD * dt);
            var sin = Math.Sin(omegaD * dt);

            var a = y0;
            var b = (v0 + (zeta * omega0 * y0)) / omegaD;

            y = decay * ((a * cos) + (b * sin));
            v = decay * (((b * omegaD) - (zeta * omega0 * a)) * cos - (((a * omegaD) + (zeta * omega0 * b)) * sin));
        }
        else if (zeta <= 1.0 + 1e-4)
        {
            var decay = Math.Exp(-omega0 * dt);
            var a = y0;
            var b = v0 + (omega0 * y0);

            y = decay * (a + (b * dt));
            v = decay * (b - (omega0 * (a + (b * dt))));
        }
        else
        {
            var gamma = omega0 * Math.Sqrt((zeta * zeta) - 1.0);
            var r1 = (-omega0 * zeta) + gamma;
            var r2 = (-omega0 * zeta) - gamma;

            var a = (v0 - (r2 * y0)) / (r1 - r2);
            var b = y0 - a;

            var e1 = Math.Exp(r1 * dt);
            var e2 = Math.Exp(r2 * dt);

            y = (a * e1) + (b * e2);
            v = (a * r1 * e1) + (b * r2 * e2);
        }

        var nextValue = (float)(Target + y);
        var nextVelocity = (float)v;

        if (!float.IsFinite(nextValue) || !float.IsFinite(nextVelocity))
        {
            return SnapToTarget();
        }

        if (MathF.Abs(nextValue - Target) < RestDisplacementThreshold &&
            MathF.Abs(nextVelocity) < RestVelocityThreshold)
        {
            return SnapToTarget();
        }

        return new SpringState(nextValue, nextVelocity, Target);
    }
}
