using System.Collections.Immutable;
using LyricPulse.Core.Models;

namespace LyricPulse.App.Platform;

/// <summary>
/// Immutable physical display geometry descriptor (strictly WorkingArea to avoid taskbar overlap).
/// </summary>
public sealed record MonitorDescriptor(
    string DisplayKey,
    string FriendlyName,
    int WorkingLeftPx,
    int WorkingTopPx,
    int WorkingWidthPx,
    int WorkingHeightPx,
    double EffectiveDpiScale,
    bool IsPrimary);

/// <summary>
/// Safe-zone rectangle inside an overlay window's logical DIP coordinate space.
/// </summary>
public sealed record SafeZoneRect(
    double LeftDip,
    double TopDip,
    double WidthDip,
    double HeightDip,
    double AnchorCenterXDip,
    double AnchorCenterYDip);

/// <summary>
/// Computed WindowState.Normal placement (physical top-left PixelPoint + DPI-scaled logical DIP dimensions)
/// for a single enabled display.
/// </summary>
public sealed record OverlayWindowPlacement(
    string DisplayKey,
    string FriendlyName,
    int PositionXPx,
    int PositionYPx,
    double WidthDip,
    double HeightDip,
    double DpiScale,
    OverlayBoundsConfig BoundsConfig,
    SafeZoneRect SafeZoneDip);

/// <summary>
/// Pure, deterministic multi-monitor placement and 5-zone safe anchor calculator.
/// </summary>
public static class OverlayBoundsCalculator
{
    public const double MinDpiScale = 0.5;
    public const double MaxDpiScale = 5.0;

    public static ImmutableArray<OverlayWindowPlacement> ComputePlacements(
        IEnumerable<MonitorDescriptor> monitors,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(settings);

        var disabledSet = BuildDisabledSet(settings.DisabledDisplays);
        var builder = ImmutableArray.CreateBuilder<OverlayWindowPlacement>();

        foreach (var monitor in monitors)
        {
            if (string.IsNullOrWhiteSpace(monitor.DisplayKey) ||
                monitor.WorkingWidthPx <= 0 ||
                monitor.WorkingHeightPx <= 0)
            {
                continue;
            }

            if (IsDisplayDisabled(monitor.DisplayKey, disabledSet))
            {
                continue;
            }

            builder.Add(ComputeSinglePlacement(monitor, settings));
        }

        return builder.ToImmutable();
    }

    public static OverlayWindowPlacement ComputeSinglePlacement(
        MonitorDescriptor monitor,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(settings);

        var scale = double.IsFinite(monitor.EffectiveDpiScale)
            ? Math.Clamp(monitor.EffectiveDpiScale, MinDpiScale, MaxDpiScale)
            : 1.0;

        var widthDip = Math.Max(1.0, monitor.WorkingWidthPx / scale);
        var heightDip = Math.Max(1.0, monitor.WorkingHeightPx / scale);

        var boundsConfig = ResolveBoundsConfig(monitor.DisplayKey, settings);
        var safeZone = ComputeSafeZoneDip(widthDip, heightDip, scale, boundsConfig);

        return new OverlayWindowPlacement(
            DisplayKey: monitor.DisplayKey,
            FriendlyName: string.IsNullOrWhiteSpace(monitor.FriendlyName) ? monitor.DisplayKey : monitor.FriendlyName,
            PositionXPx: monitor.WorkingLeftPx,
            PositionYPx: monitor.WorkingTopPx,
            WidthDip: widthDip,
            HeightDip: heightDip,
            DpiScale: scale,
            BoundsConfig: boundsConfig,
            SafeZoneDip: safeZone);
    }

    public static SafeZoneRect ComputeSafeZoneDip(
        double overlayWidthDip,
        double overlayHeightDip,
        double dpiScale,
        OverlayBoundsConfig boundsConfig)
    {
        ArgumentNullException.ThrowIfNull(boundsConfig);

        var safeW = Math.Max(1.0, overlayWidthDip);
        var safeH = Math.Max(1.0, overlayHeightDip);
        var safeScale = double.IsFinite(dpiScale) ? Math.Clamp(dpiScale, MinDpiScale, MaxDpiScale) : 1.0;

        var marginPx = Math.Clamp(boundsConfig.SafeMarginPx, AppSettings.MinSafeMarginPx, AppSettings.MaxSafeMarginPx);
        var marginDip = Math.Min(marginPx / safeScale, Math.Min(safeW, safeH) * 0.25);

        var usableW = Math.Max(1.0, safeW - (2.0 * marginDip));
        var usableH = Math.Max(1.0, safeH - (2.0 * marginDip));

        var widthRatio = Math.Clamp(boundsConfig.WidthRatio, 0.20, 1.0);
        var heightRatio = Math.Clamp(boundsConfig.HeightRatio, 0.15, 1.0);

        var zoneW = Math.Min(usableW, safeW * widthRatio);
        var zoneH = Math.Min(usableH, safeH * heightRatio);

        var anchorX = Math.Clamp(boundsConfig.AnchorX, 0.0, 1.0);
        var anchorY = Math.Clamp(boundsConfig.AnchorY, 0.0, 1.0);

        var desiredCenterX = safeW * anchorX;
        var desiredCenterY = safeH * anchorY;

        var minLeft = marginDip;
        var maxLeft = Math.Max(minLeft, safeW - marginDip - zoneW);
        var minTop = marginDip;
        var maxTop = Math.Max(minTop, safeH - marginDip - zoneH);

        var left = Math.Clamp(desiredCenterX - (zoneW * 0.5), minLeft, maxLeft);
        var top = Math.Clamp(desiredCenterY - (zoneH * 0.5), minTop, maxTop);

        return new SafeZoneRect(
            LeftDip: left,
            TopDip: top,
            WidthDip: zoneW,
            HeightDip: zoneH,
            AnchorCenterXDip: left + (zoneW * 0.5),
            AnchorCenterYDip: top + (zoneH * 0.5));
    }

    public static bool IsDisplayDisabled(string displayKey, IReadOnlySet<string> disabledSet)
    {
        ArgumentNullException.ThrowIfNull(disabledSet);
        return !string.IsNullOrWhiteSpace(displayKey) && disabledSet.Contains(NormalizeDisplayKey(displayKey));
    }

    public static string NormalizeDisplayKey(string displayKey) =>
        string.IsNullOrWhiteSpace(displayKey) ? string.Empty : displayKey.Trim().ToUpperInvariant();

    private static HashSet<string> BuildDisabledSet(ImmutableArray<string> disabledDisplays)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (disabledDisplays.IsDefaultOrEmpty)
        {
            return set;
        }

        foreach (var item in disabledDisplays)
        {
            var normalized = NormalizeDisplayKey(item);
            if (!string.IsNullOrEmpty(normalized))
            {
                set.Add(normalized);
            }
        }

        return set;
    }

    private static OverlayBoundsConfig ResolveBoundsConfig(string displayKey, AppSettings settings)
    {
        if (settings.PerDisplayBounds is not null && !settings.PerDisplayBounds.IsEmpty)
        {
            if (settings.PerDisplayBounds.TryGetValue(displayKey, out var exact))
            {
                return exact;
            }

            var normalized = NormalizeDisplayKey(displayKey);
            foreach (var (key, value) in settings.PerDisplayBounds)
            {
                if (string.Equals(NormalizeDisplayKey(key), normalized, StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }

        return OverlayBoundsConfig.FromAnchorZone(settings.AnchorZone, settings.SafeMarginPx);
    }
}
