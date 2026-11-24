using AwesomeAssertions;
using LyricPulse.Rendering.Motion;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class SpringPhysicsTests
{
    [Fact]
    public void CanonicalSpringTokens_MatchMotionSpecDampingRatios()
    {
        SpringTokens.WordSnap.DampingRatio.Should().BeApproximately(0.683f, 0.005f);
        SpringTokens.WordEnter.DampingRatio.Should().BeApproximately(0.717f, 0.005f);
        SpringTokens.LineShift.DampingRatio.Should().BeApproximately(0.897f, 0.005f);
        SpringTokens.IconPop.DampingRatio.Should().BeApproximately(0.529f, 0.005f);
        SpringTokens.CameraDrift.DampingRatio.Should().BeApproximately(1.000f, 0.005f);
        SpringTokens.BubblePush.DampingRatio.Should().BeApproximately(0.767f, 0.005f);
        SpringTokens.LensBreath.DampingRatio.Should().BeApproximately(0.705f, 0.005f);
    }

    [Fact]
    public void SpringState_AnalyticalIntegrationIsTimestepInvariantAcross60Hz120Hz240Hz()
    {
        var start = new SpringState(Value: 0f, Velocity: 0f, Target: 1.0f);

        var s60 = StepMany(start, SpringTokens.WordSnap, steps: 6, dt: 1f / 60f);
        var s120 = StepMany(start, SpringTokens.WordSnap, steps: 12, dt: 1f / 120f);
        var s240 = StepMany(start, SpringTokens.WordSnap, steps: 24, dt: 1f / 240f);

        s60.Value.Should().BeApproximately(s120.Value, 1e-4f);
        s120.Value.Should().BeApproximately(s240.Value, 1e-4f);
        s60.Velocity.Should().BeApproximately(s240.Velocity, 1e-3f);
    }

    [Fact]
    public void SpringState_ConvergesAndSnapsToRestAcrossUnderdampedCriticalAndOverdampedRegimes()
    {
        var regimes = new[]
        {
            SpringTokens.IconPop,                                    // underdamped
            SpringTokens.CameraDrift,                                // critically damped
            new SpringParams(Stiffness: 120f, Damping: 35f, Mass: 1f) // overdamped
        };

        foreach (var regime in regimes)
        {
            var state = new SpringState(Value: 0f, Velocity: 0f, Target: 1.0f);
            for (var i = 0; i < 180; i++)
            {
                state = state.Step(regime, 1f / 60f);
            }

            state.IsAtRest.Should().BeTrue();
            state.Value.Should().Be(1.0f);
            state.Velocity.Should().Be(0.0f);
        }
    }

    [Fact]
    public void SpringState_ClampsLargeDtTo33MsAndHandlesInvalidInputsSafely()
    {
        var start = new SpringState(Value: 0f, Velocity: 0f, Target: 1.0f);

        var hugeDt = start.Step(SpringTokens.WordEnter, 5.0f);
        var clampedDt = start.Step(SpringTokens.WordEnter, SpringState.MaxDeltaSeconds);

        hugeDt.Should().Be(clampedDt, "dt must clamp to <= 33.33ms to prevent sleep/wake overshoot");
        start.Step(SpringTokens.WordEnter, 0f).Should().Be(start);
        start.Step(SpringTokens.WordEnter, -0.5f).Should().Be(start);
        start.Step(SpringTokens.WordEnter, float.NaN).Should().Be(start);
    }

    [Fact]
    public void TempoCompressionAndReducedMotion_AdjustStiffnessAndDampingAppropriately()
    {
        var rapSpring = SpringTokens.WordEnter.WithTempoCompression(0.12f);
        var balladSpring = SpringTokens.WordEnter.WithTempoCompression(0.80f);

        rapSpring.Stiffness.Should().BeGreaterThan(SpringTokens.WordEnter.Stiffness);
        rapSpring.DampingRatio.Should().BeGreaterThan(SpringTokens.WordEnter.DampingRatio);
        balladSpring.Stiffness.Should().BeLessThan(SpringTokens.WordEnter.Stiffness);

        var reduced = SpringTokens.IconPop.WithReducedMotion();
        reduced.DampingRatio.Should().BeGreaterThanOrEqualTo(0.999f);

        // Verify zero overshoot under Reduced Motion
        var state = new SpringState(Value: 0f, Velocity: 0f, Target: 1.0f);
        for (var i = 0; i < 120; i++)
        {
            state = state.Step(reduced, 1f / 60f);
            state.Value.Should().BeLessThanOrEqualTo(1.0001f);
        }
    }

    [Fact]
    public void EasingCurves_RespectBoundaryValuesAndMonotonicity()
    {
        Ease.Enter(0f).Should().Be(0f);
        Ease.Enter(1f).Should().Be(1f);
        Ease.Enter(0.5f).Should().BeGreaterThan(0.75f, "Expo Out front-loads entrance progress");

        Ease.Exit(0f).Should().Be(0f);
        Ease.Exit(1f).Should().Be(1f);
        Ease.Exit(0.5f).Should().BeLessThan(0.5f, "Quad/Cubic In accelerates departure");

        Ease.Highlight(0.5f).Should().BeGreaterThan(0.7f);
        Ease.LinearProgress(0.35f).Should().BeApproximately(0.35f, 1e-6f);
        Ease.LinearProgress(-1f).Should().Be(0f);
        Ease.LinearProgress(2f).Should().Be(1f);
        Ease.ColorMorph(0.5f).Should().BeInRange(0.5f, 0.9f);
    }

    private static SpringState StepMany(SpringState state, SpringParams parameters, int steps, float dt)
    {
        var current = state;
        for (var i = 0; i < steps; i++)
        {
            current = current.Step(parameters, dt);
        }

        return current;
    }
}
