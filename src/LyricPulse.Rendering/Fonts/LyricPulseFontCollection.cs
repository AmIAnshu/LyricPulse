using Avalonia.Media.Fonts;

namespace LyricPulse.Rendering.Fonts;

/// <summary>
/// Avalonia EmbeddedFontCollection exposing all bundled SIL OFL and MIT icon TTFs
/// under the <c>fonts:LyricPulse#FamilyName</c> URI scheme.
/// </summary>
public sealed class LyricPulseFontCollection : EmbeddedFontCollection
{
    public const string SchemeKey = "fonts:LyricPulse";
    public const string AssetSourceUri = "avares://LyricPulse.Rendering/Assets/Fonts";

    public LyricPulseFontCollection()
        : base(
            key: new Uri(SchemeKey, UriKind.Absolute),
            source: new Uri(AssetSourceUri, UriKind.Absolute))
    {
    }
}
