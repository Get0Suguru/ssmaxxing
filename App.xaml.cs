using System;
using System.Windows;
using System.Drawing;
using Windows.Graphics.Capture;
using Forms = System.Windows.Forms;

namespace ScreenSnipAlpha
{
    /// <summary>
    /// Sticky source: either GDI entire-screen, or a live WGC session
    /// (yellow border stays until you change source).
    /// </summary>
    public class StickySource
    {
        public bool IsEntireScreen { get; set; } = true;
        public GraphicsCaptureItem? CaptureItem { get; set; }
        public string Label { get; set; } = "Entire Screen";
    }

    public partial class App : Application
    {
        private Forms.NotifyIcon? _trayIcon;
        private HotkeyHook? _hook;
        private LiveWgcSession? _live;
        private bool _busy;
        public static StickySource CurrentSource { get; private set; } = new StickySource();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            SetupTrayIcon();

            _hook = new HotkeyHook();
            _hook.CapsLockPlusV += () => Dispatcher.BeginInvoke(new Action(OnCaptureAndPaste));
            _hook.CapsLockPlusS += () => Dispatcher.BeginInvoke(new Action(OnChangeSource));
            _hook.Start();
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = TrayText()
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Change source (CapsLock+S)", null, (_, _) => OnChangeSource());
            menu.Items.Add("Stop sharing", null, (_, _) => StopLive("Entire Screen"));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Shutdown());
            _trayIcon.ContextMenuStrip = menu;
        }

        private string TrayText() => $"ScreenSnip — {CurrentSource.Label}  CapsLock+V paste, CapsLock+S share";

        private void OnCaptureAndPaste()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                Bitmap? bmp;

                if (_live != null)
                {
                    if (_live.IsClosed)
                    {
                        string why = _live.LastError ?? "shared window closed";
                        StopLive(CurrentSource.Label);
                        _trayIcon!.ShowBalloonTip(1800, "ScreenSnip",
                            $"Share ended ({why}). CapsLock+S to pick again.",
                            Forms.ToolTipIcon.Warning);
                        return;
                    }

                    bmp = _live.CloneLatest();
                    if (bmp == null)
                    {
                        _trayIcon!.ShowBalloonTip(1800, "ScreenSnip",
                            _live.LastError ?? "Waiting for first frame — try CapsLock+V again.",
                            Forms.ToolTipIcon.Warning);
                        return;
                    }
                }
                else
                {
                    bmp = CaptureService.CaptureEntireScreenGdi();
                }

                ClipboardPaste.SetImageAndPasteAtCursor(bmp);
            }
            catch (Exception ex)
            {
                _trayIcon?.ShowBalloonTip(2000, "ScreenSnip error", ex.Message, Forms.ToolTipIcon.Error);
            }
            finally
            {
                _busy = false;
            }
        }

        private void OnChangeSource()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var picker = new SourcePicker();
                if (picker.ShowDialog() != true || picker.SelectedSource == null)
                    return;

                var picked = picker.SelectedSource;
                if (picked.IsEntireScreen || picked.CaptureItem == null)
                {
                    StopLive("Entire Screen");
                    _trayIcon!.ShowBalloonTip(1200, "ScreenSnip", "Source set: Entire Screen", Forms.ToolTipIcon.Info);
                    return;
                }

                LiveWgcSession? session = null;
                try
                {
                    session = new LiveWgcSession(picked.CaptureItem);
                }
                catch (Exception ex)
                {
                    _trayIcon!.ShowBalloonTip(2500, "ScreenSnip",
                        "Could not start share: " + ex.Message,
                        Forms.ToolTipIcon.Error);
                    return;
                }

                _live?.Dispose();
                _live = session;
                CurrentSource = new StickySource
                {
                    IsEntireScreen = false,
                    Label = session.Label
                };
                _trayIcon!.Text = TrayText();
                _trayIcon.ShowBalloonTip(1400, "ScreenSnip",
                    $"Sharing {session.Label} — yellow border stays. CapsLock+V pastes.",
                    Forms.ToolTipIcon.Info);
            }
            finally
            {
                _busy = false;
            }
        }

        private void StopLive(string _)
        {
            _live?.Dispose();
            _live = null;
            CurrentSource = new StickySource { IsEntireScreen = true, Label = "Entire Screen" };
            if (_trayIcon != null)
                _trayIcon.Text = TrayText();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hook?.Stop();
            _live?.Dispose();
            _trayIcon!.Visible = false;
            _trayIcon.Dispose();
            base.OnExit(e);
        }
    }
}
