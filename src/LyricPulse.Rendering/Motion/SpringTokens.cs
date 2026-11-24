namespace LyricPulse.Rendering.Motion;

/// <summary>
/// The 7 canonical shared spring physics tokens locked by MOTION_AND_STYLES.md section 4.2.
/// </summary>
public static class SpringTokens
{
    /// <summary>Active word scale pop (1.00 -&gt; 1.08), focal lift, Fisheye active center lock (zeta ~0.683).</summary>
    public static readonly SpringParams WordSnap = new(Stiffness: 420f, Damping: 28.0f, Mass: 1.0f);

    /// <summary>Word entrance slide/rise, Kinetic Wall landing impact, Node label bloom (zeta ~0.717).</summary>
    public static readonly SpringParams WordEnter = new(Stiffness: 280f, Damping: 24.0f, Mass: 1.0f);

    /// <summary>Vertical stack shift (Fisheye, VisualStack), line-to-line recentering (zeta ~0.897).</summary>
    public static readonly SpringParams LineShift = new(Stiffness: 210f, Damping: 26.0f, Mass: 1.0f);

    /// <summary>Semantic word-icon pop-in, Constellation star flare, Node milestone ring (zeta ~0.529).</summary>
    public static readonly SpringParams IconPop = new(Stiffness: 480f, Damping: 22.0f, Mass: 0.9f);

    /// <summary>Critically damped silky camera pan/orbit (KineticWall, Constellation) (zeta = 1.000).</summary>
    public static readonly SpringParams CameraDrift = new(Stiffness: 95f, Damping: 19.5f, Mass: 1.0f);

    /// <summary>Messages chat thread upward push and new message bubble entrance (zeta ~0.767).</summary>
    public static readonly SpringParams BubblePush = new(Stiffness: 310f, Damping: 27.0f, Mass: 1.0f);

    /// <summary>Elliptical Bulge shader iStrength impulse breathing on active word onset (zeta ~0.705).</summary>
    public static readonly SpringParams LensBreath = new(Stiffness: 340f, Damping: 26.0f, Mass: 1.0f);
}
