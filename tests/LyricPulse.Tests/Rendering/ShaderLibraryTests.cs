using AwesomeAssertions;
using LyricPulse.Rendering.Shaders;
using SkiaSharp;
using Xunit;

namespace LyricPulse.Tests.Rendering;

public sealed class ShaderLibraryTests
{
    [Fact]
    public void EmbeddedSkslShaders_CompileWithZeroErrors()
    {
        using var shaders = new ShaderLibrary();

        shaders.IsBulgeReady.Should().BeTrue(shaders.BulgeCompileError ?? string.Empty);
        shaders.IsMotionBlurReady.Should().BeTrue(shaders.MotionBlurCompileError ?? string.Empty);
        shaders.AreAllShadersReady.Should().BeTrue();
        shaders.BulgeCompileError.Should().BeNull();
        shaders.MotionBlurCompileError.Should().BeNull();
    }

    [Fact]
    public void EllipticalBulge_RendersConvexOutwardMagnificationWithNoCenterNaN()
    {
        const int size = 200;
        const int halfRadius = 40; // Source white square spans [60..140] around center (100,100)

        using var shaders = new ShaderLibrary();
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var sourceSurface = SKSurface.Create(info);
        sourceSurface.Canvas.Clear(SKColors.Transparent);
        using (var whitePaint = new SKPaint { Color = SKColors.White, IsAntialias = false })
        {
            sourceSurface.Canvas.DrawRect(
                100 - halfRadius,
                100 - halfRadius,
                halfRadius * 2,
                halfRadius * 2,
                whitePaint);
        }

        using var destSurface = SKSurface.Create(info);
        destSurface.Canvas.Clear(SKColors.Transparent);

        var executed = shaders.ApplyEllipticalBulge(
            destSurface.Canvas,
            sourceSurface,
            new SKRect(0, 0, size, size),
            size,
            size,
            dpiScale: 1.0f,
            strength: 0.36f,
            power: 2.10f,
            isGpuBackend: false,
            reducedMotion: false,
            allowCpuForTesting: true);

        executed.Should().BeTrue();

        using var resultBitmap = SKBitmap.FromImage(destSurface.Snapshot());

        // Exact center pixel (r == 0) must remain solid white (no pow(0, p) NaN black hole!)
        var centerPixel = resultBitmap.GetPixel(100, 100);
        centerPixel.Alpha.Should().Be(255);
        centerPixel.Red.Should().Be(255);

        // Measure horizontal lit span across centerline y=100:
        // True convex bulge magnifies outward so lit width MUST exceed source width (80px), NOT pinch inward!
        var minLitX = size;
        var maxLitX = -1;
        for (var x = 0; x < size; x++)
        {
            if (resultBitmap.GetPixel(x, 100).Alpha > 128)
            {
                minLitX = Math.Min(minLitX, x);
                maxLitX = Math.Max(maxLitX, x);
            }
        }

        var bulgedWidth = maxLitX - minLitX + 1;
        bulgedWidth.Should().BeGreaterThan(halfRadius * 2 + 2, "positive iStrength must magnify outward (convex bulge), never pinch inward");

        // Out-of-bounds corners must stay cleanly transparent (Decal + boundary discard)
        resultBitmap.GetPixel(0, 0).Alpha.Should().Be(0);
        resultBitmap.GetPixel(size - 1, size - 1).Alpha.Should().Be(0);
    }

    [Fact]
    public void DirectionalMotionBlur13_SpreadsPixelsAlongVelocityAxisAndBypassesUnderGuards()
    {
        const int size = 160;
        using var shaders = new ShaderLibrary();
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var sourceSurface = SKSurface.Create(info);
        sourceSurface.Canvas.Clear(SKColors.Transparent);
        using (var whitePaint = new SKPaint { Color = SKColors.White, IsAntialias = false })
        {
            // Sharp 4px vertical bar centered at (80, 80), spanning y in [78..82]
            sourceSurface.Canvas.DrawRect(70, 78, 20, 4, whitePaint);
        }

        using var destSurface = SKSurface.Create(info);
        destSurface.Canvas.Clear(SKColors.Transparent);

        var executed = shaders.ApplyDirectionalMotionBlur(
            destSurface.Canvas,
            sourceSurface,
            new SKRect(0, 0, size, size),
            size,
            size,
            dpiScale: 1.0f,
            velocityPxPerSec: new SKPoint(0f, 1800f),
            shutterFactor: 0.48f,
            isGpuBackend: false,
            reducedMotion: false,
            allowCpuForTesting: true);

        executed.Should().BeTrue();

        using var resultBitmap = SKBitmap.FromImage(destSurface.Snapshot());
        // Vertical blur should smear alpha above and below y=[78..82]
        resultBitmap.GetPixel(80, 74).Alpha.Should().BeGreaterThan(10);
        resultBitmap.GetPixel(80, 86).Alpha.Should().BeGreaterThan(10);
        resultBitmap.GetPixel(80, 80).Alpha.Should().BeGreaterThan(50);

        // Software mode (without CPU test override) and Reduced Motion MUST bypass both shaders
        using var bypassDest = SKSurface.Create(info);
        shaders.ApplyEllipticalBulge(
            bypassDest.Canvas,
            sourceSurface,
            new SKRect(0, 0, size, size),
            size,
            size,
            dpiScale: 1.0f,
            strength: 0.24f,
            isGpuBackend: false,
            reducedMotion: false).Should().BeFalse();

        shaders.ApplyEllipticalBulge(
            bypassDest.Canvas,
            sourceSurface,
            new SKRect(0, 0, size, size),
            size,
            size,
            dpiScale: 1.0f,
            strength: 0.24f,
            isGpuBackend: true,
            reducedMotion: true).Should().BeFalse();

        shaders.ApplyDirectionalMotionBlur(
            bypassDest.Canvas,
            sourceSurface,
            new SKRect(0, 0, size, size),
            size,
            size,
            dpiScale: 1.0f,
            velocityPxPerSec: new SKPoint(0f, 1800f),
            shutterFactor: 0.48f,
            isGpuBackend: true,
            reducedMotion: true).Should().BeFalse();
    }

    [Fact]
    public void MotionBlurMath_EnforcesDeadZoneBypassAndHyperbolicTanhCap()
    {
        // Dead-zone: raw = 50 / 60 * 0.48 = 0.40px < 1.25px -> bypass
        var dead = MotionBlurMath.ComputeClampedVelocity(new SKPoint(0f, 50f), 0.48f, dpiScale: 1.0f, out var shouldBypass);
        shouldBypass.Should().BeTrue();
        dead.Should().Be(SKPoint.Empty);

        // Extreme velocity: hyperbolic tanh must strictly saturate below 22.0 * dpiScale
        var extreme = MotionBlurMath.ComputeClampedVelocity(new SKPoint(0f, 250_000f), 0.48f, dpiScale: 1.5f, out var extremeBypass);
        extremeBypass.Should().BeFalse();
        MathF.Abs(extreme.Y).Should().BeLessThanOrEqualTo(22.0f * 1.5f);
        MathF.Abs(extreme.Y).Should().BeGreaterThan(21.5f * 1.5f);

        // Bulge clamps & directional padding math
        MotionBlurMath.ClampBulgeStrength(0.95f).Should().Be(0.40f);
        MotionBlurMath.ClampBulgeStrength(-0.24f).Should().Be(0.24f);
        MotionBlurMath.ClampBulgeStrength(float.NaN).Should().Be(0.0f);
        MotionBlurMath.ClampBulgePower(1.0f).Should().Be(1.85f);
        MotionBlurMath.ClampBulgePower(5.0f).Should().Be(2.35f);
        MotionBlurMath.ClampBulgePower(float.NaN).Should().Be(MotionBlurMath.DefaultBulgePower);

        var (padX, padY) = MotionBlurMath.ComputeDirectionalPaddingPx(new SKPoint(10f, -20f));
        padX.Should().Be(9);
        padY.Should().Be(14);

        var zeroShutter = MotionBlurMath.ComputeClampedVelocity(new SKPoint(1000f, 1000f), 0f, 1f, out var zeroBypass);
        zeroBypass.Should().BeTrue();
        zeroShutter.Should().Be(SKPoint.Empty);
    }

    [Fact]
    public void InvalidSkslSource_CapturesDiagnosticErrorsGracefullyWithoutThrowing()
    {
        using var broken = ShaderLibrary.CreateFromSources("not valid sksl {{{", "also invalid }}}");
        broken.IsBulgeReady.Should().BeFalse();
        broken.IsMotionBlurReady.Should().BeFalse();
        broken.AreAllShadersReady.Should().BeFalse();
        broken.BulgeCompileError.Should().NotBeNullOrWhiteSpace();
        broken.MotionBlurCompileError.Should().NotBeNullOrWhiteSpace();
    }
}
