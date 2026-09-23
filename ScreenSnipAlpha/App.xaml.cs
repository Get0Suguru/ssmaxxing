using System;
using System.Windows;
using System.Drawing;
using Windows.Graphics.Capture;
using Forms = System.Windows.Forms;

namespace ScreenSnipAlpha
{
    public class StickySource
    {
        public bool IsEntireScreen { get; set; } = true;
        public GraphicsCaptureItem? CaptureItem { get; set; }
        public IntPtr Hwnd { get; set; }
        public string Label { get; set; } = "Entire Screen";
    }

    public partial class App : Application
    {
        private Forms.NotifyIcon? _trayIcon;
        private HotkeyHook? _hook;
        private LiveWgcSession? _live;
        private bool _busy;
        private AppSettings _settings = AppSettings.Load();

        public static StickySource CurrentSource { get; private set; } = new StickySource();
        public static bool IsSharing { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SetupTrayIcon();

            _hook = new HotkeyHook();
            _hook.CaptureHotkey += () => Dispatcher.BeginInvoke(new Action(OnCaptureAndPaste));
            _hook.SourceHotkey += () => Dispatcher.BeginInvoke(new Action(OnChangeSource));
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
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (_trayIcon == null) return;

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add($"Capture ({_settings.ModifierName}+{_settings.CaptureKeyName})", null,
                (_, _) => OnCaptureAndPaste());
            menu.Items.Add($"Change source ({_settings.ModifierName}+{_settings.SourceKeyName})", null,
                (_, _) => OnChangeSource());
            menu.Items.Add("Stop sharing", null, (_, _) => StopLive());
            menu.Items.Add(new Forms.ToolStripSeparator());

            var keepItem = new Forms.ToolStripMenuItem("Also keep on clipboard")
            {
                Checked = _settings.KeepOnClipboard,
                CheckOnClick = true
            };
            keepItem.CheckedChanged += (_, _) =>
            {
                _settings.KeepOnClipboard = keepItem.Checked;
                _settings.Save();
            };
            menu.Items.Add(keepItem);

            menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Shutdown());
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.Text = TrayText();
        }

        private void OpenSettings()
        {
            var win = new SettingsWindow(_settings);
            if (win.ShowDialog() == true && win.Result != null)
            {
                _settings = win.Result;
                _settings.Save();
                _hook?.ApplySettings(_settings);
                RebuildTrayMenu();
                _trayIcon?.ShowBalloonTip(1500, "ScreenSnip",
                    $"Hotkeys: {_settings.ModifierName}+{_settings.CaptureKeyName} / {_settings.ModifierName}+{_settings.SourceKeyName}",
                    Forms.ToolTipIcon.Info);
            }
        }

        private string TrayText() =>
            $"ScreenSnip — {CurrentSource.Label}  |  {_settings.ModifierName}+{_settings.CaptureKeyName} / {_settings.ModifierName}+{_settings.SourceKeyName}";

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
                        StopLive();
                        _trayIcon!.ShowBalloonTip(1800, "ScreenSnip",
                            $"Share ended ({why}). {_settings.ModifierName}+{_settings.SourceKeyName} to pick again.",
                            Forms.ToolTipIcon.Warning);
                        return;
                    }

                    bmp = _live.CloneLatest();
                    if (bmp == null)
                    {
                        _trayIcon!.ShowBalloonTip(1800, "ScreenSnip",
                            _live.LastError ?? "Waiting for first frame — try again.",
                            Forms.ToolTipIcon.Warning);
                        return;
                    }
                }
                else
                {
                    bmp = CaptureService.CaptureEntireScreenGdi();
                }

                // Always paste into focused field. KeepOnClipboard only controls whether
                // the image stays on the clipboard afterward.
                ClipboardPaste.Deliver(bmp, keepOnClipboard: _settings.KeepOnClipboard);
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
                if (picker.ShowDialog() != true)
                    return;

                if (picker.RequestStopShare)
                {
                    StopLive();
                    _trayIcon!.ShowBalloonTip(1200, "ScreenSnip", "Sharing stopped.", Forms.ToolTipIcon.Info);
                    return;
                }

                if (picker.SelectedSource == null)
                    return;

                var picked = picker.SelectedSource;
                if (picked.IsEntireScreen || picked.CaptureItem == null)
                {
                    StopLive();
                    _trayIcon!.ShowBalloonTip(1200, "ScreenSnip", "Source set: Entire Screen", Forms.ToolTipIcon.Info);
                    return;
                }

                LiveWgcSession? session;
                try
                {
                    session = new LiveWgcSession(picked.CaptureItem, picked.Hwnd);
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
                IsSharing = true;
                CurrentSource = new StickySource
                {
                    IsEntireScreen = false,
                    Label = session.Label
                };
                _trayIcon!.Text = TrayText();
                _trayIcon.ShowBalloonTip(1400, "ScreenSnip",
                    $"Sharing {session.Label} — yellow border stays.",
                    Forms.ToolTipIcon.Info);
            }
            finally
            {
                _busy = false;
            }
        }

        private void StopLive()
        {
            _live?.Dispose();
            _live = null;
            IsSharing = false;
            CurrentSource = new StickySource { IsEntireScreen = true, Label = "Entire Screen" };
            if (_trayIcon != null)
                _trayIcon.Text = TrayText();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hook?.Dispose();
            _live?.Dispose();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            base.OnExit(e);
        }
    }
}
