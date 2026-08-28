using System;
using Vortice;   // for Result (hr.Failure / hr.Code)
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace ScreenSnipAlpha
{
    /// <summary>
    /// Meet/OBS-style capture: one GraphicsCaptureSession stays alive (yellow
    /// border stays), and CapsLock+V just clones the latest frame.
    /// </summary>
    public sealed class LiveWgcSession : IDisposable
    {
        private readonly object _gpuLock = new();
        private readonly object _bmpLock = new();
        private readonly ManualResetEventSlim _ready = new(false);

        private GraphicsCaptureItem? _item;
        private Direct3D11CaptureFramePool? _pool;
        private GraphicsCaptureSession? _session;
        private IDirect3DDevice? _winrtDevice;
        private ID3D11Device? _d3d;
        private ID3D11DeviceContext? _ctx;
        private ID3D11Texture2D? _staging;
        private SizeInt32 _lastSize;
        private Bitmap? _latest;
        private int _frames;
        private bool _disposed;
        private string _closedReason = "";

        public string Label { get; }
        public string? LastError { get; private set; }
        public bool IsClosed => _disposed || !string.IsNullOrEmpty(_closedReason);

        public LiveWgcSession(GraphicsCaptureItem item)
        {
            _item = item;
            Label = string.IsNullOrWhiteSpace(item.DisplayName) ? "Shared window" : item.DisplayName;

            CreateDevices();

            var size = item.Size;
            if (size.Width < 2 || size.Height < 2)
                size = new SizeInt32(2, 2);
            _lastSize = size;

            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                size);

            _session = _pool.CreateCaptureSession(item);
            try { _session.IsCursorCaptureEnabled = false; } catch { /* older OS */ }

            _item.Closed += OnItemClosed;
            _pool.FrameArrived += OnFrameArrived;
            _session.StartCapture();
        }

        public Bitmap? CloneLatest(int waitMs = 2500)
        {
            if (IsClosed)
            {
                LastError ??= _closedReason;
                return null;
            }

            if (!_ready.Wait(waitMs))
            {
                LastError = _frames == 0
                    ? "no frames yet (try CapsLock+V again in a second)"
                    : (LastError ?? "frame wait timed out");
                return null;
            }

            lock (_bmpLock)
            {
                if (_latest == null)
                {
                    LastError ??= "no bitmap stored";
                    return null;
                }
                return (Bitmap)_latest.Clone();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _closedReason = string.IsNullOrEmpty(_closedReason) ? "stopped" : _closedReason;
            _ready.Set();

            try { if (_item != null) _item.Closed -= OnItemClosed; } catch { }
            try { if (_pool != null) _pool.FrameArrived -= OnFrameArrived; } catch { }

            try { _session?.Dispose(); } catch { }
            try { _pool?.Dispose(); } catch { }
            _session = null;
            _pool = null;
            _item = null;

            lock (_gpuLock)
            {
                _staging?.Dispose();
                _staging = null;
                _ctx?.Dispose();
                _d3d?.Dispose();
                _winrtDevice?.Dispose();
                _ctx = null;
                _d3d = null;
                _winrtDevice = null;
            }

            lock (_bmpLock)
            {
                _latest?.Dispose();
                _latest = null;
            }

            _ready.Dispose();
        }

        private void OnItemClosed(GraphicsCaptureItem sender, object args)
        {
            _closedReason = "source window closed";
            LastError = _closedReason;
            _ready.Set();
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (_disposed) return;

            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null) return;

                bool recreate = false;
                var content = frame.ContentSize;
                if (content.Width != _lastSize.Width || content.Height != _lastSize.Height)
                {
                    _lastSize = content;
                    recreate = true;
                }

                Bitmap? bmp;
                lock (_gpuLock)
                {
                    bmp = CopyFrameToBitmap(frame);
                }

                if (bmp != null)
                {
                    lock (_bmpLock)
                    {
                        _latest?.Dispose();
                        _latest = bmp;
                    }

                    // First compositor frame is often blank (browsers especially).
                    if (Interlocked.Increment(ref _frames) >= 2)
                        _ready.Set();
                }

                if (recreate && _pool != null && _winrtDevice != null && content.Width > 0 && content.Height > 0)
                {
                    _pool.Recreate(
                        _winrtDevice,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        content);
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private Bitmap? CopyFrameToBitmap(Direct3D11CaptureFrame frame)
        {
            if (_d3d == null || _ctx == null) return null;

            using var gpuTex = TextureFromSurface(frame.Surface);
            var desc = gpuTex.Description;
            int w = (int)desc.Width;
            int h = (int)desc.Height;
            if (w < 1 || h < 1) return null;

            if (_staging == null ||
    _staging.Description.Width != desc.Width ||
    _staging.Description.Height != desc.Height)
{
    _staging?.Dispose();
    var stagingDesc = new Texture2DDescription(
        desc.Format,
        desc.Width,
        desc.Height,
        1,
        1,
        BindFlags.None,
        ResourceUsage.Staging,
        CpuAccessFlags.Read);

    // No initial data — single-arg overload, no Span/null ambiguity.
    _staging = _d3d.CreateTexture2D(stagingDesc);
}

            _ctx.CopyResource(_staging, gpuTex);
            _ctx.Map(_staging, 0, MapMode.Read, MapFlags.None, out MappedSubresource mapped);
            try
            {
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                var bits = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int srcPitch = (int)mapped.RowPitch;
                    int dstPitch = bits.Stride;
                    int rowBytes = w * 4;
                    unsafe
                    {
                        byte* src = (byte*)mapped.DataPointer;
                        byte* dst = (byte*)bits.Scan0;
                        for (int y = 0; y < h; y++)
                            Buffer.MemoryCopy(src + y * srcPitch, dst + y * dstPitch, dstPitch, rowBytes);
                    }
                }
                finally
                {
                    bmp.UnlockBits(bits);
                }
                return bmp;
            }
            finally
            {
                _ctx.Unmap(_staging, 0);
            }
        }

        private void CreateDevices()
{
    FeatureLevel[] levels =
    {
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    };

    // Signature: (adapter, driverType, flags, featureLevels, out device, out featureLevel, out context)
    var hr = D3D11.D3D11CreateDevice(
        null,
        DriverType.Hardware,
        DeviceCreationFlags.BgraSupport,
        levels,
        out _d3d,
        out _,
        out _ctx);

    if (hr.Failure || _d3d == null || _ctx == null)
    {
        hr = D3D11.D3D11CreateDevice(
            null,
            DriverType.Warp,
            DeviceCreationFlags.BgraSupport,
            levels,
            out _d3d,
            out _,
            out _ctx);

        if (hr.Failure || _d3d == null || _ctx == null)
            throw new InvalidOperationException($"D3D11CreateDevice failed: 0x{hr.Code:X8}");
    }

    using var dxgi = _d3d.QueryInterface<IDXGIDevice>();
    int wrapHr = CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out IntPtr inspectable);
    Marshal.ThrowExceptionForHR(wrapHr);
    try
    {
        _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
    }
    finally
    {
        Marshal.Release(inspectable);
    }
}

        private static ID3D11Texture2D TextureFromSurface(IDirect3DSurface surface)
        {
            IntPtr unk = MarshalInspectable<IDirect3DSurface>.FromManaged(surface);
            try
            {
                var accessIid = typeof(IDirect3DDxgiInterfaceAccess).GUID;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unk, ref accessIid, out IntPtr accessPtr));
                try
                {
                    var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(accessPtr);
                    var texIid = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C"); // ID3D11Texture2D
                    Marshal.ThrowExceptionForHR(access.GetInterface(ref texIid, out IntPtr texPtr));

                    // Vortice takes ownership of texPtr — do NOT Release it.
                    return new ID3D11Texture2D(texPtr);
                }
                finally
                {
                    Marshal.Release(accessPtr);
                }
            }
            finally
            {
                Marshal.Release(unk);
            }
        }

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
    }
}