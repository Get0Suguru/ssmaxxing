using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenSnipAlpha
{
    public record WindowEntry(IntPtr Hwnd, string Title);

    public static class WindowEnumerator
    {
        public static List<WindowEntry> GetVisibleWindows()
        {
            var result = new List<WindowEntry>();
            var selfPid = Environment.ProcessId;

            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                if (GetWindowTextLength(hwnd) == 0) return true;

                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == selfPid) return true; // don't list ourselves

                // Skip tool windows / windows with no taskbar presence.
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

                var sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                result.Add(new WindowEntry(hwnd, title));
                return true;
            }, IntPtr.Zero);

            return result;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    }
}
