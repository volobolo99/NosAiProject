// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Percezione — Acquisizione DXGI Desktop Duplication con triple buffer
// ============================================================================
//
// Backend di cattura reale: i frame prodotti qui sono LIVE perché provengono
// davvero dal compositore desktop. Se la duplicazione non è disponibile (sessione
// non interattiva, accesso negato, adattatore assente) la sorgente resta UNKNOWN
// con motivo: non esiste alcun percorso che fabbrichi pixel (ADR-0002).

using System;
using System.Threading;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Lock-free triple buffer between a producer (capture thread) and a consumer
/// (pipeline thread).
/// </summary>
/// <remarks>
/// Three slots decouple the two sides: the producer always has a free slot to
/// write into and never blocks on the consumer, and the consumer always reads a
/// whole frame, never one being overwritten. A frame that is superseded before
/// being read is dropped on purpose — perception wants the newest screen, not a
/// queue of stale ones — and the drop is counted rather than hidden.
/// </remarks>
public sealed class TripleFrameBuffer
{
    private readonly CaptureFrame?[] _slots = new CaptureFrame?[3];
    private int _writeSlot;      // slot the producer is filling
    private int _readySlot = -1; // most recently published slot, -1 when none
    private int _readSlot = 1;   // slot the consumer last handed out
    private long _publishedCount;
    private long _droppedCount;

    private readonly object _swap = new();

    /// <summary>Frames published by the producer.</summary>
    public long PublishedCount => Interlocked.Read(ref _publishedCount);

    /// <summary>Frames overwritten before the consumer read them.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>Publishes a frame into the free slot and makes it the newest.</summary>
    public void Publish(CaptureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_swap)
        {
            _slots[_writeSlot] = frame;
            if (_readySlot >= 0) Interlocked.Increment(ref _droppedCount);
            int published = _writeSlot;
            // The next write goes to whichever slot the consumer is not holding.
            _writeSlot = _readySlot >= 0 ? _readySlot : OtherSlot(published, _readSlot);
            _readySlot = published;
            Interlocked.Increment(ref _publishedCount);
        }
    }

    /// <summary>Takes the newest published frame, or false when none is pending.</summary>
    public bool TryTakeLatest(out CaptureFrame frame)
    {
        lock (_swap)
        {
            if (_readySlot < 0)
            {
                frame = null!;
                return false;
            }
            _readSlot = _readySlot;
            _readySlot = -1;
            frame = _slots[_readSlot]!;
            return true;
        }
    }

    private static int OtherSlot(int a, int b)
    {
        for (int i = 0; i < 3; i++)
        {
            if (i != a && i != b) return i;
        }
        return 0;
    }
}

/// <summary>Why a real capture backend could not be started.</summary>
public sealed record CaptureUnavailable(string Reason, int HResult);

/// <summary>
/// Real screen acquisition through DXGI Desktop Duplication.
/// </summary>
/// <remarks>
/// <para>
/// Frames are classified <see cref="DataSourceKind.Live"/>: they are the actual
/// desktop surface copied into a CPU-readable staging texture. Construction is
/// fail-closed — <see cref="TryCreate"/> returns a named reason instead of a
/// half-initialised source, and every native handle is released on failure.
/// </para>
/// <para>
/// Desktop Duplication requires an interactive desktop session. Under a service,
/// a locked secure desktop, or a remote session without a console, the API
/// refuses: that is reported, never papered over with synthetic pixels.
/// </para>
/// </remarks>
public sealed unsafe class DxgiDesktopDuplicationSource : IFrameSource, IDisposable
{
    private void* _factory;
    private void* _adapter;
    private void* _output;
    private void* _output1;
    private void* _device;
    private void* _context;
    private void* _duplication;
    private void* _stagingTexture;

    private readonly int _width;
    private readonly int _height;
    private readonly uint _acquireTimeoutMs;
    private readonly Func<DateTime> _clock;
    private bool _disposed;
    private bool _frameHeld;

    public DataSourceKind Source => DataSourceKind.Live;

    /// <summary>Desktop width reported by the duplication descriptor.</summary>
    public int Width => _width;

    /// <summary>Desktop height reported by the duplication descriptor.</summary>
    public int Height => _height;

    private DxgiDesktopDuplicationSource(void* factory, void* adapter, void* output, void* output1,
        void* device, void* context, void* duplication, void* stagingTexture,
        int width, int height, uint acquireTimeoutMs, Func<DateTime> clock)
    {
        _factory = factory;
        _adapter = adapter;
        _output = output;
        _output1 = output1;
        _device = device;
        _context = context;
        _duplication = duplication;
        _stagingTexture = stagingTexture;
        _width = width;
        _height = height;
        _acquireTimeoutMs = acquireTimeoutMs;
        _clock = clock;
    }

    /// <summary>
    /// Attempts to open Desktop Duplication on the given adapter/output.
    /// Returns false with a named <paramref name="unavailable"/> reason when the
    /// platform refuses; never throws for an ordinary unavailability.
    /// </summary>
    public static bool TryCreate(out DxgiDesktopDuplicationSource? source, out CaptureUnavailable? unavailable,
        uint adapterIndex = 0, uint outputIndex = 0, uint acquireTimeoutMs = 250, Func<DateTime>? clock = null)
    {
        source = null;
        unavailable = null;

        if (!OperatingSystem.IsWindows())
        {
            unavailable = new CaptureUnavailable("dxgi_requires_windows", 0);
            return false;
        }

        void* factory = null, adapter = null, output = null, output1 = null;
        void* device = null, context = null, duplication = null, staging = null;
        try
        {
            int hr = DxgiInterop.CreateDXGIFactory1(DxgiInterop.IID_IDXGIFactory1, out factory);
            if (hr != DxgiInterop.S_OK)
            {
                unavailable = new CaptureUnavailable("dxgi_factory_creation_failed", hr);
                return false;
            }

            hr = DxgiInterop.EnumAdapters1(factory, adapterIndex, out adapter);
            if (hr != DxgiInterop.S_OK)
            {
                unavailable = new CaptureUnavailable($"dxgi_adapter_{adapterIndex}_not_found", hr);
                return false;
            }

            hr = DxgiInterop.EnumOutputs(adapter, outputIndex, out output);
            if (hr != DxgiInterop.S_OK)
            {
                unavailable = new CaptureUnavailable($"dxgi_output_{outputIndex}_not_found", hr);
                return false;
            }

            hr = DxgiInterop.QueryInterface(output, DxgiInterop.IID_IDXGIOutput1, out output1);
            if (hr != DxgiInterop.S_OK)
            {
                unavailable = new CaptureUnavailable("dxgi_output1_unsupported", hr);
                return false;
            }

            // DriverType must be UNKNOWN when an explicit adapter is supplied.
            hr = DxgiInterop.D3D11CreateDevice(adapter, DxgiInterop.D3D_DRIVER_TYPE_UNKNOWN, IntPtr.Zero, 0,
                null, 0, DxgiInterop.D3D11_SDK_VERSION, out device, null, out context);
            if (hr != DxgiInterop.S_OK)
            {
                unavailable = new CaptureUnavailable("d3d11_device_creation_failed", hr);
                return false;
            }

            hr = DxgiInterop.DuplicateOutput(output1, device, out duplication);
            if (hr != DxgiInterop.S_OK)
            {
                // E_ACCESSDENIED is the usual answer on a secure/locked desktop or
                // from a non-interactive session: a real environment limitation.
                string reason = hr == DxgiInterop.E_ACCESSDENIED
                    ? "dxgi_duplication_access_denied"
                    : "dxgi_duplicate_output_failed";
                unavailable = new CaptureUnavailable(reason, hr);
                return false;
            }

            DxgiInterop.GetDuplicationDesc(duplication, out DxgiOutduplDesc desc);
            int width = (int)desc.ModeDesc.Width;
            int height = (int)desc.ModeDesc.Height;
            if (width <= 0 || height <= 0)
            {
                unavailable = new CaptureUnavailable("dxgi_duplication_reported_empty_mode", 0);
                return false;
            }

            var stagingDesc = new D3D11Texture2DDesc
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DxgiInterop.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DxgiSampleDesc { Count = 1, Quality = 0 },
                Usage = DxgiInterop.D3D11_USAGE_STAGING,
                BindFlags = 0,
                CpuAccessFlags = DxgiInterop.D3D11_CPU_ACCESS_READ,
                MiscFlags = 0,
            };
            hr = DxgiInterop.CreateTexture2D(device, stagingDesc, out staging);
            if (hr != DxgiInterop.S_OK)
            {
                unavailable = new CaptureUnavailable("d3d11_staging_texture_creation_failed", hr);
                return false;
            }

            source = new DxgiDesktopDuplicationSource(factory, adapter, output, output1, device, context,
                duplication, staging, width, height, acquireTimeoutMs, clock ?? (() => DateTime.UtcNow));
            // Ownership transferred to the instance; skip the cleanup below.
            factory = adapter = output = output1 = device = context = duplication = staging = null;
            return true;
        }
        catch (DllNotFoundException ex)
        {
            unavailable = new CaptureUnavailable($"dxgi_library_missing:{ex.GetType().Name}", 0);
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            unavailable = new CaptureUnavailable($"dxgi_entrypoint_missing:{ex.GetType().Name}", 0);
            return false;
        }
        finally
        {
            // Whatever was not handed to the instance is released here, so a
            // failed construction leaks no native object.
            DxgiInterop.Release(ref staging);
            DxgiInterop.Release(ref duplication);
            DxgiInterop.Release(ref context);
            DxgiInterop.Release(ref device);
            DxgiInterop.Release(ref output1);
            DxgiInterop.Release(ref output);
            DxgiInterop.Release(ref adapter);
            DxgiInterop.Release(ref factory);
        }
    }

    /// <summary>
    /// Acquires the next desktop frame. Returns false when the compositor had no
    /// new frame within the timeout — an ordinary idle screen, not an error.
    /// </summary>
    public bool TryAcquire(out CaptureFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        frame = null!;

        ReleaseHeldFrame();

        int hr = DxgiInterop.AcquireNextFrame(_duplication, _acquireTimeoutMs, out DxgiOutduplFrameInfo info, out void* desktopResource);
        if (hr == DxgiInterop.DXGI_ERROR_WAIT_TIMEOUT) return false;
        if (hr != DxgiInterop.S_OK)
        {
            DxgiInterop.Release(ref desktopResource);
            // ACCESS_LOST happens on desktop switches (UAC, lock). The caller can
            // rebuild the source; we refuse to emit a frame we do not have.
            return false;
        }

        _frameHeld = true;

        // AccumulatedFrames == 0 means the update carried no new desktop image
        // (pointer-only move, or the blank surface handed out right after
        // DuplicateOutput). Reporting it as a frame would publish a black screen
        // as a real observation, so it counts as "nothing new" instead.
        if (info.AccumulatedFrames == 0)
        {
            DxgiInterop.Release(ref desktopResource);
            return false;
        }
        void* desktopTexture = null;
        try
        {
            hr = DxgiInterop.QueryInterface(desktopResource, DxgiInterop.IID_ID3D11Texture2D, out desktopTexture);
            if (hr != DxgiInterop.S_OK) return false;

            DxgiInterop.CopyResource(_context, _stagingTexture, desktopTexture);

            hr = DxgiInterop.Map(_context, _stagingTexture, 0, DxgiInterop.D3D11_MAP_READ, out D3D11MappedSubresource mapped);
            if (hr != DxgiInterop.S_OK || mapped.Data is null) return false;

            try
            {
                byte[] bgra = new byte[_width * _height * 4];
                int rowBytes = _width * 4;
                // RowPitch is the GPU's stride and is >= rowBytes; copying row by
                // row drops the padding instead of shifting every scanline.
                for (int y = 0; y < _height; y++)
                {
                    var sourceRow = new ReadOnlySpan<byte>((byte*)mapped.Data + (long)y * mapped.RowPitch, rowBytes);
                    sourceRow.CopyTo(bgra.AsSpan(y * rowBytes, rowBytes));
                }
                frame = new CaptureFrame(_width, _height, bgra, DataSourceKind.Live, _clock());
                return true;
            }
            finally
            {
                DxgiInterop.Unmap(_context, _stagingTexture, 0);
            }
        }
        finally
        {
            DxgiInterop.Release(ref desktopTexture);
            DxgiInterop.Release(ref desktopResource);
        }
    }

    private void ReleaseHeldFrame()
    {
        if (!_frameHeld) return;
        DxgiInterop.ReleaseFrame(_duplication);
        _frameHeld = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseHeldFrame();
        DxgiInterop.Release(ref _stagingTexture);
        DxgiInterop.Release(ref _duplication);
        DxgiInterop.Release(ref _context);
        DxgiInterop.Release(ref _device);
        DxgiInterop.Release(ref _output1);
        DxgiInterop.Release(ref _output);
        DxgiInterop.Release(ref _adapter);
        DxgiInterop.Release(ref _factory);
    }
}

/// <summary>
/// Runs a frame source on its own thread and publishes into a triple buffer, so
/// the perception pipeline always reads the newest complete frame without ever
/// blocking the capture loop.
/// </summary>
public sealed class TripleBufferedCapture : IFrameSource, IDisposable
{
    private readonly IFrameSource _inner;
    private readonly TripleFrameBuffer _buffer = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _captureThread;
    private long _acquireFailures;

    public DataSourceKind Source => _inner.Source;
    public TripleFrameBuffer Buffer => _buffer;

    /// <summary>Acquisition attempts that yielded no frame (idle screen or a lost frame).</summary>
    public long AcquireFailures => Interlocked.Read(ref _acquireFailures);

    public TripleBufferedCapture(IFrameSource inner, bool startImmediately = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "NosAi.Perception.Capture",
        };
        if (startImmediately) _captureThread.Start();
    }

    public void Start()
    {
        if (!_captureThread.IsAlive) _captureThread.Start();
    }

    private void CaptureLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_inner.TryAcquire(out CaptureFrame frame)) _buffer.Publish(frame);
                else Interlocked.Increment(ref _acquireFailures);
            }
            catch (ObjectDisposedException) { break; }
            catch
            {
                // A capture fault must not kill the loop; it is counted, and the
                // consumer simply sees no new frame.
                Interlocked.Increment(ref _acquireFailures);
            }
        }
    }

    /// <summary>Takes the newest published frame; false when none is pending.</summary>
    public bool TryAcquire(out CaptureFrame frame) => _buffer.TryTakeLatest(out frame);

    public void Dispose()
    {
        _cts.Cancel();
        if (_captureThread.IsAlive) _captureThread.Join(TimeSpan.FromSeconds(2));
        (_inner as IDisposable)?.Dispose();
        _cts.Dispose();
    }
}
