using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenSnipAlpha
{
    public record WindowEntry(IntPtr Hwnd, string Title);

    public static class WindowEnumerator
    {
        /// <summary>
        /// Top-level visible windows on the *current* virtual desktop only.
        /// Windows on other desktops are skipped — WGC can't usefully capture them
        /// until that desktop is visited.
        /// </summary>
        public static List<WindowEntry> GetVisibleWindowsOnCurrentDesktop()
        {
            var result = new List<WindowEntry>();
            var selfPid = Environment.ProcessId;

            // Virtual desktop filter (Win10+)
            IVirtualDesktopManager? vdm = null;
            try
            {
                vdm = (IVirtualDesktopManager)new VirtualDesktopManager();
            }
            catch
            {
                // Older OS or COM failure — fall through and list everything visible.
            }

            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                if (GetWindowTextLength(hwnd) == 0) return true;

                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == selfPid) return true;

                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

                // Cloaked UWP / minimized shell noise
                if (IsCloaked(hwnd)) return true;

                if (vdm != null)
                {
                    try
                    {
                        // false = not on current desktop
                        if (vdm.IsWindowOnCurrentVirtualDesktop(hwnd) == false)
                            return true;
                    }
                    catch
                    {
                        // If the query fails, keep the window rather than drop everything.
                    }
                }

                var sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                result.Add(new WindowEntry(hwnd, title));
                return true;
            }, IntPtr.Zero);

            return result;
        }

        private static bool IsCloaked(IntPtr hwnd)
        {
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
                return cloaked != 0;
            return false;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int DWMWA_CLOAKED = 14;

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

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        // COM: IVirtualDesktopManager
        [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVirtualDesktopManager
        {
            bool IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow);
            Guid GetWindowDesktopId(IntPtr topLevelWindow);
            void MoveWindowToDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.LPStruct)] Guid desktopId);
        }

        [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
        private class VirtualDesktopManager
        {
        }
    }
}
