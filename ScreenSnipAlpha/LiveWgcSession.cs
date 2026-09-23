using System;
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
        private int _lastFrameTick;
        private readonly IntPtr _hwnd;
        private bool _disposed;
        private string _closedReason = "";

        public string Label { get; }
        public string? LastError { get; private set; }
        public bool IsClosed => _disposed || !string.IsNullOrEmpty(_closedReason);

        public LiveWgcSession(GraphicsCaptureItem item, IntPtr hwnd = default)
        {
            _item = item;
            _hwnd = hwnd;
            _lastFrameTick = Environment.TickCount;
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
            try { _session.IsCursorCaptureEnabled = false; } catch { }

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

            // Chromium/Electron (which is what nearly every AI-chatbot desktop app is
            // built on) actively PAUSES its renderer — not just throttles it — once the
            // OS reports the window as fully occluded by another window. That's a real,
            // currently-documented Chromium behavior (the same flags — 
            // --disable-backgrounding-occluded-windows / --disable-renderer-backgrounding /
            // --disable-features=CalculateNativeWinOcclusion — show up across many
            // Electron apps' bug trackers for this exact symptom). WGC only ever relays a
            // frame the app itself actually renders, so if the app has stopped rendering,
            // no amount of waiting produces a new frame — this has nothing to do with
            // WGC's own capabilities (it captures occluded windows fine, same as
            // Google's app) and everything to do with the target's own render loop being
            // asleep. Skip the nudge if we've had a frame recently — the window is
            // evidently visible and rendering normally, no need to disturb anything.
            int framesBefore = Volatile.Read(ref _frames);
            int idleMs = Environment.TickCount - Volatile.Read(ref _lastFrameTick);
            if (framesBefore == 0 || idleMs > 800)
                NudgeIntoView(framesBefore, waitMs);

            if (!_ready.Wait(waitMs))
            {
                LastError = _frames == 0
                    ? "no frames yet (try again in a second)"
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

        /// <summary>
        /// Briefly raises the target above whatever currently covers it — WITHOUT
        /// activating it (SWP_NOACTIVATE), so keyboard focus never leaves whatever the
        /// user is actually typing into — then puts the previous foreground window back
        /// on top once we've got a fresh frame (or we've waited long enough). This causes
        /// a brief on-screen flash of the target window; that's the unavoidable
        /// trade-off of un-occluding it without editing that app's own launch flags.
        /// No-ops for minimized windows — restoring from minimized is a much bigger,
        /// more disruptive visible change, so that case isn't handled automatically.
        /// </summary>
        private void NudgeIntoView(int framesBefore, int overallWaitMs)
        {
            if (_hwnd == IntPtr.Zero || IsIconic(_hwnd)) return;

            try
            {
                IntPtr prevForeground = GetForegroundWindow();

                SetWindowPos(_hwnd, HWND_TOP, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

                int budget = Math.Min(overallWaitMs, 700);
                int start = Environment.TickCount;
                while (Volatile.Read(ref _frames) == framesBefore &&
                       Environment.TickCount - start < budget)
                {
                    Thread.Sleep(15);
                }

                if (prevForeground != IntPtr.Zero && prevForeground != _hwnd)
                {
                    SetWindowPos(prevForeground, HWND_TOP, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
            catch { /* best effort — worst case we fall through to the normal timeout */ }
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

                    Volatile.Write(ref _lastFrameTick, Environment.TickCount);

                    // A single real frame is valid and usable. Requiring a 2nd frame here
                    // used to mean a window that's never focused (so it never gets the
                    // "extra" frame from a title-bar/chrome redraw on focus change) would
                    // sit at _frames == 1 forever and CloneLatest() would always time out,
                    // even though we already have a perfectly good frame in hand.
                    if (Interlocked.Increment(ref _frames) >= 1)
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

            try
            {
                // Overload that returns the device directly (no Result type needed).
                _d3d = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels);
            }
            catch
            {
                _d3d = D3D11.D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport, levels);
            }

            if (_d3d == null)
                throw new InvalidOperationException("D3D11CreateDevice failed (hardware and WARP).");

            _ctx = _d3d.ImmediateContext;

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
                    var texIid = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
                    Marshal.ThrowExceptionForHR(access.GetInterface(ref texIid, out IntPtr texPtr));
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

        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
    }
}
