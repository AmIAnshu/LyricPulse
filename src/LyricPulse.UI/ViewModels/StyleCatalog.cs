using System.Collections.Immutable;
using LyricPulse.Core.Models;

namespace LyricPulse.UI.ViewModels;

public sealed record StyleMetadata(
    OverlayStyleId StyleId,
    string DisplayName,
    string CategoryLabel,
    string CapabilityChip,
    string Description,
    bool HasVisualIcons,
    double PreviewPhaseSeconds);

public sealed record ColorPresetOption(
    string Name,
    RgbColor Accent1,
    RgbColor Accent2,
    RgbColor Accent3);

/// <summary>
/// Canonical 10-style catalog and curated studio palettes matching
/// <c>design-system/lyricpulse/pages/gallery.md</c> and <c>settings.md</c>.
/// </summary>
public static class StyleCatalog
{
    public static ImmutableArray<StyleMetadata> All { get; } =
    [
        new(OverlayStyleId.Fisheye, "Fisheye", "Kinetic type", "Lens Shader",
            "Tight 3-word stack bulging outward through a spherical SkSL lens distortion.", false, 0.2),
        new(OverlayStyleId.FisheyeVisual, "Fisheye Visual", "Kinetic type", "Visual Icons",
            "Spherical Fisheye typography paired with offline semantic icons beside matching words.", true, 3.0),
        new(OverlayStyleId.Codestyle, "Codestyle", "Monospace", "Monospace",
            "Lyrics typed out live as syntax-highlighted code beneath a blinking red cursor.", false, 14.4),
        new(OverlayStyleId.CodestyleVisual, "Codestyle Visual", "Monospace", "Visual Icons",
            "Live syntax-highlighted code lyrics enhanced with inline semantic word icons.", true, 17.3),
        new(OverlayStyleId.VisualStack, "Visual Stack", "Visuals", "Visual Icons",
            "Centered kinetic word column pairing every matching lyric word with an expressive icon.", true, 5.9),
        new(OverlayStyleId.Constellation, "Constellation", "Geometry", "Vector Path",
            "Each lyric line traces across space as a connected glowing constellation, star by star.", false, 8.8),
        new(OverlayStyleId.SpidersWeb, "Spider's Web", "Geometry", "Geometric",
            "Words spin outward into an intricate geometric web with a luminous strand per word.", false, 11.7),
        new(OverlayStyleId.NodeLine, "Node Line", "Geometry", "Vector Path",
            "A flowing line draws itself across the screen as every word illuminates a glowing node.", false, 20.1),
        new(OverlayStyleId.KineticWall, "Kinetic Wall", "3D Stage", "3D Motion",
            "Words land sequentially onto a typographic wall drifting smoothly in 3D perspective.", false, 28.6),
        new(OverlayStyleId.Messages, "Messages", "Phone Screen", "Chat Thread",
            "Song lines arrive as a sleek phone message thread with timed chat bubbles per line.", false, 22.9)
    ];

    public static ImmutableArray<ColorPresetOption> StudioPresets { get; } =
    [
        new("Pure Studio", new RgbColor(0xF8, 0xFA, 0xFC), new RgbColor(0x81, 0x8C, 0xF8), new RgbColor(0x38, 0xBD, 0xF8)),
        new("Vinyl Warm", new RgbColor(0xFE, 0xF3, 0xC7), new RgbColor(0xF5, 0x9E, 0x0B), new RgbColor(0xFB, 0x71, 0x85)),
        new("Mint Pulse", new RgbColor(0xEC, 0xFD, 0xF5), new RgbColor(0x34, 0xD3, 0x99), new RgbColor(0x2D, 0xD4, 0xBF)),
        new("Monochrome", new RgbColor(0xFF, 0xFF, 0xFF), new RgbColor(0xCB, 0xD5, 0xE1), new RgbColor(0x64, 0x74, 0x8B))
    ];

    public static StyleMetadata GetMetadata(OverlayStyleId styleId) =>
        All.FirstOrDefault(s => s.StyleId == styleId) ?? All[0];
}
