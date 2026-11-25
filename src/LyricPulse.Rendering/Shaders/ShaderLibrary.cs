using LyricPulse.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace LyricPulse.Rendering.Shaders;

/// <summary>
/// Compiles and caches embedded SkSL runtime shaders (<c>EllipticalBulge.sksl</c>,
/// <c>DirectionalMotionBlur13.sksl</c>, and <c>AmbientPartyMesh.sksl</c>) once with thread-safe
/// zero-allocation uniform reuse, null-snapshot TDR guards, and automatic software/ReducedMotion bypass.
/// </summary>
public sealed class ShaderLibrary : IDisposable
{
    public const string EllipticalBulgeResourceName = "LyricPulse.Rendering.Shaders.EllipticalBulge.sksl";
    public const string DirectionalMotionBlurResourceName = "LyricPulse.Rendering.Shaders.DirectionalMotionBlur13.sksl";
    public const string AmbientPartyMeshResourceName = "LyricPulse.Rendering.Shaders.AmbientPartyMesh.sksl";

    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly ILogger<ShaderLibrary> _logger;
    private readonly Lock _shaderSync = new();
    private readonly SKPaint _blitPaint = new() { IsAntialias = true };

    private readonly SKRuntimeEffect? _bulgeEffect;
    private readonly SKRuntimeEffectUniforms? _bulgeUniforms;
    private readonly SKRuntimeEffectChildren? _bulgeChildren;

    private readonly SKRuntimeEffect? _motionBlurEffect;
    private readonly SKRuntimeEffectUniforms? _motionBlurUniforms;
    private readonly SKRuntimeEffectChildren? _motionBlurChildren;

    private readonly SKRuntimeEffect? _partyMeshEffect;
    private readonly SKRuntimeEffectUniforms? _partyMeshUniforms;

    private bool _disposed;

    public ShaderLibrary(ILogger<ShaderLibrary>? logger = null)
        : this(
            LoadEmbeddedShaderSource(EllipticalBulgeResourceName),
            LoadEmbeddedShaderSource(DirectionalMotionBlurResourceName),
            LoadEmbeddedShaderSource(AmbientPartyMeshResourceName),
            logger)
    {
    }

    public static ShaderLibrary CreateFromSources(
        string bulgeSksl,
        string motionBlurSksl,
        ILogger<ShaderLibrary>? logger = null) =>
        new(
            bulgeSksl,
            motionBlurSksl,
            LoadEmbeddedShaderSource(AmbientPartyMeshResourceName),
            logger);

    public static ShaderLibrary CreateFromSources(
        string bulgeSksl,
        string motionBlurSksl,
        string partyMeshSksl,
        ILogger<ShaderLibrary>? logger = null) =>
        new(bulgeSksl, motionBlurSksl, partyMeshSksl, logger);

    internal ShaderLibrary(
        string bulgeSksl,
        string motionBlurSksl,
        string partyMeshSksl,
        ILogger<ShaderLibrary>? logger = null)
    {
        _logger = logger ?? NullLogger<ShaderLibrary>.Instance;

        _bulgeEffect = CompileEffect("EllipticalBulge", bulgeSksl, out var bulgeError);
        BulgeCompileError = bulgeError;
        if (_bulgeEffect is not null)
        {
            _bulgeUniforms = new SKRuntimeEffectUniforms(_bulgeEffect);
            _bulgeChildren = new SKRuntimeEffectChildren(_bulgeEffect);
        }

        _motionBlurEffect = CompileEffect("DirectionalMotionBlur13", motionBlurSksl, out var blurError);
        MotionBlurCompileError = blurError;
        if (_motionBlurEffect is not null)
        {
            _motionBlurUniforms = new SKRuntimeEffectUniforms(_motionBlurEffect);
            _motionBlurChildren = new SKRuntimeEffectChildren(_motionBlurEffect);
        }

        _partyMeshEffect = CompileEffect("AmbientPartyMesh", partyMeshSksl, out var partyError);
        PartyMeshCompileError = partyError;
        if (_partyMeshEffect is not null)
        {
            _partyMeshUniforms = new SKRuntimeEffectUniforms(_partyMeshEffect);
        }
    }

    public bool IsBulgeReady => _bulgeEffect is not null;

    public bool IsMotionBlurReady => _motionBlurEffect is not null;

    public bool IsPartyMeshReady => _partyMeshEffect is not null;

    public bool AreAllShadersReady => IsBulgeReady && IsMotionBlurReady && IsPartyMeshReady;

    public string? BulgeCompileError { get; }

    public string? MotionBlurCompileError { get; }

    public string? PartyMeshCompileError { get; }

    public static string LoadEmbeddedShaderSource(string resourceName)
    {
        var assembly = typeof(ShaderLibrary).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SkSL resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public bool ShouldBypassBulge(bool isGpuBackend, bool reducedMotion, float strength, bool allowCpuForTesting = false) =>
        (!isGpuBackend && !allowCpuForTesting) ||
        reducedMotion ||
        !IsBulgeReady ||
        MotionBlurMath.ClampBulgeStrength(strength) <= 0.0005f;

    public bool ShouldBypassMotionBlur(
        bool isGpuBackend,
        bool reducedMotion,
        SKPoint velocityPxPerSec,
        float shutterFactor,
        float dpiScale,
        out SKPoint clampedVelocity,
        bool allowCpuForTesting = false)
    {
        clampedVelocity = MotionBlurMath.ComputeClampedVelocity(velocityPxPerSec, shutterFactor, dpiScale, out var inDeadZone);
        return (!isGpuBackend && !allowCpuForTesting) ||
               reducedMotion ||
               !IsMotionBlurReady ||
               inDeadZone;
    }

    /// <summary>
    /// Composites an offscreen physical-pixel <see cref="SKSurface"/> through the Elliptical Bulge shader
    /// (monotonic convex magnification with transparent edge feather), or performs a clean unwarped blit when bypassed.
    /// </summary>
    public bool ApplyEllipticalBulge(
        SKCanvas targetCanvas,
        SKSurface offscreenSurface,
        SKRect logicalDestRect,
        int physWidth,
        int physHeight,
        float dpiScale,
        float strength,
        float power = MotionBlurMath.DefaultBulgePower,
        bool isGpuBackend = true,
        bool reducedMotion = false,
        bool allowCpuForTesting = false)
    {
        ArgumentNullException.ThrowIfNull(targetCanvas);
        ArgumentNullException.ThrowIfNull(offscreenSurface);

        if (physWidth <= 0 || physHeight <= 0)
        {
            return false;
        }

        lock (_shaderSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            using var snapshot = offscreenSurface.Snapshot(new SKRectI(0, 0, physWidth, physHeight));
            if (snapshot is null)
            {
                return false;
            }

            if (ShouldBypassBulge(isGpuBackend, reducedMotion, strength, allowCpuForTesting) ||
                _bulgeEffect is null || _bulgeUniforms is null || _bulgeChildren is null)
            {
                BlitUnshadedLocked(targetCanvas, snapshot, logicalDestRect);
                return false;
            }

            using var contentShader = snapshot.ToShader(
                SKShaderTileMode.Decal,
                SKShaderTileMode.Decal,
                LinearSampling);

            Span<float> centerSpan = stackalloc float[2];
            centerSpan[0] = physWidth * 0.5f;
            centerSpan[1] = physHeight * 0.5f;

            Span<float> sizeSpan = stackalloc float[2];
            sizeSpan[0] = physWidth;
            sizeSpan[1] = physHeight;

            _bulgeUniforms["iCenter"] = (ReadOnlySpan<float>)centerSpan;
            _bulgeUniforms["iSize"] = (ReadOnlySpan<float>)sizeSpan;
            _bulgeUniforms["iStrength"] = MotionBlurMath.ClampBulgeStrength(strength);
            _bulgeUniforms["iPower"] = MotionBlurMath.ClampBulgePower(power);
            _bulgeChildren["content"] = contentShader;

            try
            {
                using var runtimeShader = _bulgeEffect.ToShader(_bulgeUniforms, _bulgeChildren);
                if (runtimeShader is null)
                {
                    BlitUnshadedLocked(targetCanvas, snapshot, logicalDestRect);
                    return false;
                }

                DrawPhysicalShaderRectLocked(targetCanvas, runtimeShader, logicalDestRect, physWidth, physHeight, dpiScale);
                return true;
            }
            finally
            {
                _bulgeChildren["content"] = null;
            }
        }
    }

    /// <summary>
    /// Composites an offscreen physical-pixel <see cref="SKSurface"/> through the 13-tap Directional Motion Blur
    /// shader, or performs a clean unblurred blit when bypassed (dead-zone, software mode, or Reduced Motion).
    /// </summary>
    public bool ApplyDirectionalMotionBlur(
        SKCanvas targetCanvas,
        SKSurface offscreenSurface,
        SKRect logicalDestRect,
        int physWidth,
        int physHeight,
        float dpiScale,
        SKPoint velocityPxPerSec,
        float shutterFactor,
        bool isGpuBackend = true,
        bool reducedMotion = false,
        bool allowCpuForTesting = false)
    {
        ArgumentNullException.ThrowIfNull(targetCanvas);
        ArgumentNullException.ThrowIfNull(offscreenSurface);

        if (physWidth <= 0 || physHeight <= 0)
        {
            return false;
        }

        lock (_shaderSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            using var snapshot = offscreenSurface.Snapshot(new SKRectI(0, 0, physWidth, physHeight));
            if (snapshot is null)
            {
                return false;
            }

            if (ShouldBypassMotionBlur(isGpuBackend, reducedMotion, velocityPxPerSec, shutterFactor, dpiScale, out var clampedVelocity, allowCpuForTesting) ||
                _motionBlurEffect is null || _motionBlurUniforms is null || _motionBlurChildren is null)
            {
                BlitUnshadedLocked(targetCanvas, snapshot, logicalDestRect);
                return false;
            }

            using var contentShader = snapshot.ToShader(
                SKShaderTileMode.Decal,
                SKShaderTileMode.Decal,
                LinearSampling);

            Span<float> velocitySpan = stackalloc float[2];
            velocitySpan[0] = clampedVelocity.X;
            velocitySpan[1] = clampedVelocity.Y;

            _motionBlurUniforms["iVelocity"] = (ReadOnlySpan<float>)velocitySpan;
            _motionBlurChildren["content"] = contentShader;

            try
            {
                using var runtimeShader = _motionBlurEffect.ToShader(_motionBlurUniforms, _motionBlurChildren);
                if (runtimeShader is null)
                {
                    BlitUnshadedLocked(targetCanvas, snapshot, logicalDestRect);
                    return false;
                }

                DrawPhysicalShaderRectLocked(targetCanvas, runtimeShader, logicalDestRect, physWidth, physHeight, dpiScale);
                return true;
            }
            finally
            {
                _motionBlurChildren["content"] = null;
            }
        }
    }

    /// <summary>
    /// Draws the GPU-accelerated grainy, slow-breathing, beat-reactive Ambient Party Mesh backdrop
    /// (<c>AmbientPartyMesh.sksl</c>). Returns <c>false</c> when bypassed (Reduced Motion, software mode,
    /// or compile failure) so callers can draw the static zero-CPU gradient fallback.
    /// </summary>
    public bool DrawAmbientPartyMesh(
        SKCanvas targetCanvas,
        SKRect logicalDestRect,
        float dpiScale,
        float timeSeconds,
        float energyLevel,
        AlbumAccentPalette palette,
        bool isGpuBackend = true,
        bool reducedMotion = false,
        bool allowCpuForTesting = false,
        float bassEnergy = 0f,
        bool bottomFadeToBlack = false)
    {
        ArgumentNullException.ThrowIfNull(targetCanvas);
        ArgumentNullException.ThrowIfNull(palette);

        if (logicalDestRect.Width <= 0f || logicalDestRect.Height <= 0f)
        {
            return false;
        }

        if ((!isGpuBackend && !allowCpuForTesting) || reducedMotion || !IsPartyMeshReady)
        {
            return false;
        }

        lock (_shaderSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_partyMeshEffect is null || _partyMeshUniforms is null)
            {
                return false;
            }

            var safeDpi = float.IsFinite(dpiScale) && dpiScale > 0f ? dpiScale : 1.0f;
            var physW = Math.Max(1, (int)MathF.Ceiling(logicalDestRect.Width * safeDpi));
            var physH = Math.Max(1, (int)MathF.Ceiling(logicalDestRect.Height * safeDpi));

            Span<float> resSpan = stackalloc float[2];
            resSpan[0] = physW;
            resSpan[1] = physH;

            Span<float> c1 = stackalloc float[3];
            c1[0] = palette.AccentColor1.R / 255f;
            c1[1] = palette.AccentColor1.G / 255f;
            c1[2] = palette.AccentColor1.B / 255f;

            Span<float> c2 = stackalloc float[3];
            c2[0] = palette.AccentColor2.R / 255f;
            c2[1] = palette.AccentColor2.G / 255f;
            c2[2] = palette.AccentColor2.B / 255f;

            Span<float> c3 = stackalloc float[3];
            c3[0] = palette.AccentColor3.R / 255f;
            c3[1] = palette.AccentColor3.G / 255f;
            c3[2] = palette.AccentColor3.B / 255f;

            _partyMeshUniforms["iResolution"] = (ReadOnlySpan<float>)resSpan;
            _partyMeshUniforms["iTime"] = float.IsFinite(timeSeconds) ? timeSeconds : 0f;
            _partyMeshUniforms["iEnergy"] = float.IsFinite(energyLevel) ? Math.Clamp(energyLevel, 0.5f, 1.85f) : 1.0f;
            _partyMeshUniforms["iColor1"] = (ReadOnlySpan<float>)c1;
            _partyMeshUniforms["iColor2"] = (ReadOnlySpan<float>)c2;
            _partyMeshUniforms["iColor3"] = (ReadOnlySpan<float>)c3;
            if (_partyMeshUniforms.Contains("iBass"))
            {
                _partyMeshUniforms["iBass"] = float.IsFinite(bassEnergy) ? Math.Clamp(bassEnergy, 0f, 1f) : 0f;
            }

            if (_partyMeshUniforms.Contains("iBottomFade"))
            {
                _partyMeshUniforms["iBottomFade"] = bottomFadeToBlack ? 1.0f : 0.0f;
            }

            using var runtimeShader = _partyMeshEffect.ToShader(_partyMeshUniforms);
            if (runtimeShader is null)
            {
                return false;
            }

            DrawPhysicalShaderRectLocked(targetCanvas, runtimeShader, logicalDestRect, physW, physH, safeDpi);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_shaderSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _bulgeUniforms?.Dispose();
            _bulgeChildren?.Dispose();
            _bulgeEffect?.Dispose();
            _motionBlurUniforms?.Dispose();
            _motionBlurChildren?.Dispose();
            _motionBlurEffect?.Dispose();
            _partyMeshUniforms?.Dispose();
            _partyMeshEffect?.Dispose();
            _blitPaint.Dispose();
        }
    }

    private SKRuntimeEffect? CompileEffect(string shaderName, string source, out string? errorText)
    {
        var effect = SKRuntimeEffect.CreateShader(source, out var error);
        if (effect is null)
        {
            errorText = string.IsNullOrWhiteSpace(error) ? "Unknown SkSL compilation failure." : error;
            _logger.LogShaderCompileFailed(shaderName, errorText);
            return null;
        }

        errorText = null;
        return effect;
    }

    private void DrawPhysicalShaderRectLocked(
        SKCanvas targetCanvas,
        SKShader runtimeShader,
        SKRect logicalDestRect,
        int physWidth,
        int physHeight,
        float dpiScale)
    {
        var safeDpi = float.IsFinite(dpiScale) && dpiScale > 0f ? dpiScale : 1.0f;
        _blitPaint.Shader = runtimeShader;

        var saved = targetCanvas.Save();
        try
        {
            targetCanvas.Translate(logicalDestRect.Left, logicalDestRect.Top);
            targetCanvas.Scale(1f / safeDpi, 1f / safeDpi);
            targetCanvas.DrawRect(0, 0, physWidth, physHeight, _blitPaint);
        }
        finally
        {
            _blitPaint.Shader = null;
            targetCanvas.RestoreToCount(saved);
        }
    }

    private void BlitUnshadedLocked(SKCanvas targetCanvas, SKImage snapshot, SKRect logicalDestRect)
    {
        _blitPaint.Shader = null;
        targetCanvas.DrawImage(snapshot, logicalDestRect, LinearSampling, _blitPaint);
    }
}
