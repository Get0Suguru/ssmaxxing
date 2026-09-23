using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScreenSnipAlpha
{
    /// <summary>
    /// Global hotkeys via RegisterHotKey (reliable). Default Alt+V / Alt+S.
    /// Needs a HWND — we create a message-only window.
    /// </summary>
    public sealed class HotkeyHook : IDisposable
    {
        public event Action? CaptureHotkey;
        public event Action? SourceHotkey;

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_CAPTURE = 1;
        private const int HOTKEY_SOURCE = 2;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        private HwndSource? _source;
        private IntPtr _hwnd = IntPtr.Zero;
        private bool _registered;
        private AppSettings _settings = new();

        public void Start()
        {
            if (_source != null) return;

            var p = new HwndSourceParameters("ScreenSnipHotkeys")
            {
                Width = 0,
                Height = 0,
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
                WindowStyle = 0
            };
            _source = new HwndSource(p);
            _hwnd = _source.Handle;
            _source.AddHook(WndProc);

            ApplySettings(AppSettings.Load());
        }

        public void ApplySettings(AppSettings s)
        {
            _settings = s;
            UnregisterAll();
            if (_hwnd == IntPtr.Zero) return;

            uint mods = MapModifier(s.ModifierVk) | MOD_NOREPEAT;

            bool ok1 = RegisterHotKey(_hwnd, HOTKEY_CAPTURE, mods, (uint)s.CaptureVk);
            bool ok2 = RegisterHotKey(_hwnd, HOTKEY_SOURCE, mods, (uint)s.SourceVk);
            _registered = ok1 || ok2;

            if (!ok1 || !ok2)
            {
                // One of the combos is already taken by another app.
                System.Diagnostics.Debug.WriteLine(
                    $"RegisterHotKey failed: capture={ok1} source={ok2} err={Marshal.GetLastWin32Error()}");
            }
        }

        public void Stop() => Dispose();

        public void Dispose()
        {
            UnregisterAll();
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source.Dispose();
                _source = null;
                _hwnd = IntPtr.Zero;
            }
        }

        private void UnregisterAll()
        {
            if (_hwnd == IntPtr.Zero) return;
            UnregisterHotKey(_hwnd, HOTKEY_CAPTURE);
            UnregisterHotKey(_hwnd, HOTKEY_SOURCE);
            _registered = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_CAPTURE)
                {
                    CaptureHotkey?.Invoke();
                    handled = true;
                }
                else if (id == HOTKEY_SOURCE)
                {
                    SourceHotkey?.Invoke();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private static uint MapModifier(int vk) => vk switch
        {
            0x11 => MOD_CONTROL, // VK_CONTROL
            0x12 => MOD_ALT,     // VK_MENU
            0x10 => MOD_SHIFT,   // VK_SHIFT
            0x5B or 0x5C => MOD_WIN,
            _ => MOD_ALT
        };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
