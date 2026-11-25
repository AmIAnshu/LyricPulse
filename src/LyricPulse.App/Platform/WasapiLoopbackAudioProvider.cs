using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyricPulse.App.Platform;

/// <summary>
/// Zero-dependency Windows CoreAudio WASAPI Loopback audio capture provider.
/// Captures default playback mix (<c>eRender + AUDCLNT_STREAMFLAGS_LOOPBACK</c>, zero mic access)
/// and feeds an <see cref="AudioSpectrumAnalyzer"/> with graceful fallback when idle or device changes.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows CoreAudio COM interop capture pump; pure FFT logic is unit tested in AudioSpectrumAnalyzer.")]
public sealed partial class WasapiLoopbackAudioProvider : IAudioSpectrumProvider
{
    private const int DataFlowRender = 0;
    private const int RoleConsole = 1;
    private const int ShareModeShared = 0;
    private const int StreamFlagsLoopback = 0x00020000;
    private const int ClsCtxAll = 0x17;
    private const int BufferSilentFlag = 0x2;
    private const ushort WaveFormatPcm = 1;
    private const ushort WaveFormatIeeeFloat = 3;
    private const ushort WaveFormatExtensible = 0xFFFE;
    private static readonly Guid KsDataFormatSubTypeIeeeFloat = new("00000003-0000-0010-8000-00AA00389B71");
    private static readonly Guid KsDataFormatSubTypePcm = new("00000001-0000-0010-8000-00AA00389B71");

    private static Guid AudioClientIid => new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static Guid AudioCaptureClientIid => new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    private readonly AudioSpectrumAnalyzer _analyzer = new();
    private readonly ILogger<WasapiLoopbackAudioProvider> _logger;
    private readonly Lock _lifecycleLock = new();

    private CancellationTokenSource? _cts;
    private Thread? _captureThread;
    private bool _disposed;

    public WasapiLoopbackAudioProvider(ILogger<WasapiLoopbackAudioProvider>? logger = null)
    {
        _logger = logger ?? NullLogger<WasapiLoopbackAudioProvider>.Instance;
    }

    public void StartCapture()
    {
        lock (_lifecycleLock)
        {
            if (_disposed || _cts is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _captureThread = new Thread(() => CaptureLoop(token))
            {
                Name = "LyricPulse.WasapiLoopback",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _captureThread.SetApartmentState(ApartmentState.MTA);
            _captureThread.Start();
        }
    }

    public void StopCapture()
    {
        Thread? thread;
        CancellationTokenSource? cts;
        lock (_lifecycleLock)
        {
            cts = _cts;
            thread = _captureThread;
            _cts = null;
            _captureThread = null;
        }

        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (thread is not null && thread.IsAlive)
        {
            thread.Join(TimeSpan.FromMilliseconds(500));
        }

        cts?.Dispose();
    }

    public AudioFrameMetrics GetCurrentMetrics(double timeSeconds, bool isPlaying, float partyEnergy) =>
        _analyzer.ComputeMetrics(timeSeconds, isPlaying, partyEnergy);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopCapture();
    }

    private void CaptureLoop(CancellationToken ct)
    {
        var backoffMs = 500;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                RunSingleCaptureSession(ct);
                backoffMs = 500;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogLoopbackWarning(_logger, ex);
                if (ct.WaitHandle.WaitOne(backoffMs))
                {
                    break;
                }

                backoffMs = Math.Min(backoffMs * 2, 5000);
            }
        }
    }

    private unsafe void RunSingleCaptureSession(CancellationToken ct)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        nint mixFormatPtr = 0;
        var clientStarted = false;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var hr = enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleConsole, out device);
            if (hr < 0 || device is null)
            {
                Thread.Sleep(750);
                return;
            }

            var audioClientIid = AudioClientIid;
            hr = device.Activate(ref audioClientIid, ClsCtxAll, 0, out var audioClientObj);
            if (hr < 0 || audioClientObj is not IAudioClient ac)
            {
                Thread.Sleep(750);
                return;
            }

            audioClient = ac;
            hr = audioClient.GetMixFormat(out mixFormatPtr);
            if (hr < 0 || mixFormatPtr == 0)
            {
                Thread.Sleep(750);
                return;
            }

            var format = ParseWaveFormat(mixFormatPtr);
            _analyzer.SampleRateHz = format.SampleRate;

            const long FiftyMsIn100Ns = 500_000;
            hr = audioClient.Initialize(
                ShareModeShared,
                StreamFlagsLoopback,
                FiftyMsIn100Ns,
                0,
                mixFormatPtr,
                0);
            if (hr < 0)
            {
                Thread.Sleep(750);
                return;
            }

            var captureIid = AudioCaptureClientIid;
            hr = audioClient.GetService(ref captureIid, out var captureObj);
            if (hr < 0 || captureObj is not IAudioCaptureClient cc)
            {
                return;
            }

            captureClient = cc;
            if (audioClient.Start() < 0)
            {
                return;
            }

            clientStarted = true;
            while (!ct.IsCancellationRequested)
            {
                hr = captureClient.GetNextPacketSize(out var packetFrames);
                if (hr < 0)
                {
                    break; // e.g., AUDCLNT_E_DEVICE_INVALIDATED -> reconnect on next loop
                }

                if (packetFrames == 0)
                {
                    if (ct.WaitHandle.WaitOne(10))
                    {
                        break;
                    }

                    continue;
                }

                while (packetFrames > 0 && !ct.IsCancellationRequested)
                {
                    hr = captureClient.GetBuffer(out var dataPtr, out var framesRead, out var flags, out _, out _);
                    if (hr < 0)
                    {
                        return;
                    }

                    try
                    {
                        if (framesRead > 0 && dataPtr != 0 && (flags & BufferSilentFlag) == 0)
                        {
                            var sampleCount = (int)framesRead * format.Channels;
                            if (format.IsFloat32)
                            {
                                var span = new ReadOnlySpan<float>((void*)dataPtr, sampleCount);
                                _analyzer.PushInterleavedFloatSamples(span, format.Channels);
                            }
                            else if (format.IsPcm16)
                            {
                                var span = new ReadOnlySpan<short>((void*)dataPtr, sampleCount);
                                _analyzer.PushInterleavedInt16Samples(span, format.Channels);
                            }
                        }
                    }
                    finally
                    {
                        _ = captureClient.ReleaseBuffer(framesRead);
                    }

                    if (captureClient.GetNextPacketSize(out packetFrames) < 0)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            if (clientStarted && audioClient is not null)
            {
                try
                {
                    _ = audioClient.Stop();
                }
                catch
                {
                }
            }

            if (mixFormatPtr != 0)
            {
                Marshal.FreeCoTaskMem(mixFormatPtr);
            }

            if (captureClient is not null) Marshal.ReleaseComObject(captureClient);
            if (audioClient is not null) Marshal.ReleaseComObject(audioClient);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private static (int SampleRate, int Channels, bool IsFloat32, bool IsPcm16) ParseWaveFormat(nint formatPtr)
    {
        var wfx = Marshal.PtrToStructure<WaveFormatEx>(formatPtr);
        var isFloat = wfx.wFormatTag == WaveFormatIeeeFloat && wfx.wBitsPerSample == 32;
        var isPcm16 = wfx.wFormatTag == WaveFormatPcm && wfx.wBitsPerSample == 16;

        if (wfx.wFormatTag == WaveFormatExtensible && wfx.cbSize >= 22)
        {
            var ext = Marshal.PtrToStructure<WaveFormatExtensibleStruct>(formatPtr);
            isFloat = ext.SubFormat == KsDataFormatSubTypeIeeeFloat && wfx.wBitsPerSample == 32;
            isPcm16 = ext.SubFormat == KsDataFormatSubTypePcm && wfx.wBitsPerSample == 16;
        }

        return (
            Math.Clamp((int)wfx.nSamplesPerSec, 8_000, 192_000),
            Math.Clamp((int)wfx.nChannels, 1, 16),
            isFloat,
            isPcm16);
    }

    private static readonly Action<ILogger, Exception?> LogLoopbackWarning =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(1, nameof(WasapiLoopbackAudioProvider)),
            "WASAPI loopback capture session interrupted; falling back to smooth tempo pulse until retry.");

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatExtensibleStruct
    {
        public WaveFormatEx Format;
        public ushort Samples;
        public uint dwChannelMask;
        public Guid SubFormat;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out nint ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate([In] ref Guid iid, int dwClsCtx, nint pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig]
        int Initialize(int shareMode, int streamFlags, long hnsBufferDuration, long hnsPeriodicity, nint pFormat, nint audioSessionGuid);

        [PreserveSig]
        int GetBufferSize(out uint pNumBufferFrames);

        [PreserveSig]
        int GetStreamLatency(out long phnsLatency);

        [PreserveSig]
        int GetCurrentPadding(out uint pNumPaddingFrames);

        [PreserveSig]
        int IsFormatSupported(int shareMode, nint pFormat, out nint ppClosestMatch);

        [PreserveSig]
        int GetMixFormat(out nint ppDeviceFormat);

        [PreserveSig]
        int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(nint eventHandle);

        [PreserveSig]
        int GetService([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(out nint ppData, out uint pNumFramesToRead, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);

        [PreserveSig]
        int ReleaseBuffer(uint numFramesRead);

        [PreserveSig]
        int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }
}
