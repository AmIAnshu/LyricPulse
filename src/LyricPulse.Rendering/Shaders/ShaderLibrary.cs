using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace LyricPulse.Rendering.Shaders;

/// <summary>
/// Compiles and caches embedded SkSL runtime shaders (<c>EllipticalBulge.sksl</c> and
/// <c>DirectionalMotionBlur13.sksl</c>) once with zero-allocation uniform reuse and automatic
/// software/ReducedMotion bypass.
/// </summary>
public sealed class ShaderLibrary : IDisposable
{
    public const string EllipticalBulgeResourceName = "LyricPulse.Rendering.Shaders.EllipticalBulge.sksl";
    public const string DirectionalMotionBlurResourceName = "LyricPulse.Rendering.Shaders.DirectionalMotionBlur13.sksl";

    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private readonly ILogger<ShaderLibrary> _logger;
    private readonly SKPaint _blitPaint = new() { IsAntialias = true };

    private readonly SKRuntimeEffect? _bulgeEffect;
    private readonly SKRuntimeEffectUniforms? _bulgeUniforms;
    private readonly SKRuntimeEffectChildren? _bulgeChildren;

    private readonly SKRuntimeEffect? _motionBlurEffect;
    private readonly SKRuntimeEffectUniforms? _motionBlurUniforms;
    private readonly SKRuntimeEffectChildren? _motionBlurChildren;

    private bool _disposed;

    public ShaderLibrary(ILogger<ShaderLibrary>? logger = null)
        : this(
            LoadEmbeddedShaderSource(EllipticalBulgeResourceName),
            LoadEmbeddedShaderSource(DirectionalMotionBlurResourceName),
            logger)
    {
    }

    public static ShaderLibrary CreateFromSources(
        string bulgeSksl,
        string motionBlurSksl,
        ILogger<ShaderLibrary>? logger = null) =>
        new(bulgeSksl, motionBlurSksl, logger);

    internal ShaderLibrary(string bulgeSksl, string motionBlurSksl, ILogger<ShaderLibrary>? logger = null)
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
    }

    public bool IsBulgeReady => _bulgeEffect is not null;

    public bool IsMotionBlurReady => _motionBlurEffect is not null;

    public bool AreAllShadersReady => IsBulgeReady && IsMotionBlurReady;

    public string? BulgeCompileError { get; }

    public string? MotionBlurCompileError { get; }

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
    /// (true convex magnification), or performs a clean unwarped blit when bypassed.
    /// Returns true if the SkSL shader pass executed, false if bypassed.
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (physWidth <= 0 || physHeight <= 0)
        {
            return false;
        }

        using var snapshot = offscreenSurface.Snapshot(new SKRectI(0, 0, physWidth, physHeight));
        if (ShouldBypassBulge(isGpuBackend, reducedMotion, strength, allowCpuForTesting) ||
            _bulgeEffect is null || _bulgeUniforms is null || _bulgeChildren is null)
        {
            BlitUnshaded(targetCanvas, snapshot, logicalDestRect);
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
                BlitUnshaded(targetCanvas, snapshot, logicalDestRect);
                return false;
            }

            DrawPhysicalShaderRect(targetCanvas, runtimeShader, logicalDestRect, physWidth, physHeight, dpiScale);
            return true;
        }
        finally
        {
            _bulgeChildren["content"] = null;
        }
    }

    /// <summary>
    /// Composites an offscreen physical-pixel <see cref="SKSurface"/> through the 13-tap Directional Motion Blur
    /// shader, or performs a clean unblurred blit when bypassed (dead-zone, software mode, or Reduced Motion).
    /// Returns true if the SkSL shader pass executed, false if bypassed.
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (physWidth <= 0 || physHeight <= 0)
        {
            return false;
        }

        using var snapshot = offscreenSurface.Snapshot(new SKRectI(0, 0, physWidth, physHeight));
        if (ShouldBypassMotionBlur(isGpuBackend, reducedMotion, velocityPxPerSec, shutterFactor, dpiScale, out var clampedVelocity, allowCpuForTesting) ||
            _motionBlurEffect is null || _motionBlurUniforms is null || _motionBlurChildren is null)
        {
            BlitUnshaded(targetCanvas, snapshot, logicalDestRect);
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
                BlitUnshaded(targetCanvas, snapshot, logicalDestRect);
                return false;
            }

            DrawPhysicalShaderRect(targetCanvas, runtimeShader, logicalDestRect, physWidth, physHeight, dpiScale);
            return true;
        }
        finally
        {
            _motionBlurChildren["content"] = null;
        }
    }

    public void Dispose()
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
        _blitPaint.Dispose();
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

    private void DrawPhysicalShaderRect(
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

    private void BlitUnshaded(SKCanvas targetCanvas, SKImage snapshot, SKRect logicalDestRect)
    {
        _blitPaint.Shader = null;
        targetCanvas.DrawImage(snapshot, logicalDestRect, LinearSampling, _blitPaint);
    }
}
