using System;
using System.Runtime.InteropServices;

namespace ScreenSnipAlpha
{
    /// <summary>
    /// Global low-level keyboard hook. CapsLock is a modifier only:
    /// CapsLock+V / CapsLock+S fire our actions. Win/Ctrl/Alt combos
    /// (including Win+V clipboard history) are never swallowed.
    /// </summary>
    public class HotkeyHook
    {
        public event Action? CapsLockPlusV;
        public event Action? CapsLockPlusS;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int VK_CAPITAL = 0x14;
        private const int VK_V = 0x56;
        private const int VK_S = 0x53;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;   // Alt
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private const uint LLKHF_INJECTED = 0x10;
        private const uint LLKHF_LOWER_IL_INJECTED = 0x02;

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _capsHeld;

        public HotkeyHook()
        {
            _proc = HookCallback;
        }

        public void Start()
        {
            _hookId = SetHook(_proc);
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
                UnhookWindowsHookEx(_hookId);
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
            if (!isDown && !isUp)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool injected = (info.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0;

            // Let SendInput (Ctrl+V paste) and other injected keys through untouched.
            if (injected)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            if (info.vkCode == VK_CAPITAL)
            {
                if (isDown) _capsHeld = true;
                if (isUp) _capsHeld = false;
                return (IntPtr)1; // swallow so CapsLock never toggles
            }

            if (_capsHeld && isDown && (info.vkCode == VK_V || info.vkCode == VK_S))
            {
                // Win+V, Ctrl+V, Alt+V must reach the OS / the focused app.
                if (KeyDown(VK_LWIN) || KeyDown(VK_RWIN) || KeyDown(VK_CONTROL) || KeyDown(VK_MENU))
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                if (info.vkCode == VK_V)
                    CapsLockPlusV?.Invoke();
                else
                    CapsLockPlusS?.Invoke();
                return (IntPtr)1;
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
