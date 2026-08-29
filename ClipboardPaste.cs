using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ScreenSnipAlpha
{
    public static class ClipboardPaste
    {
        /// <summary>
        /// Always tries to paste the image into the currently focused control.
        /// keepOnClipboard = true  → leave the image on the clipboard after paste
        /// keepOnClipboard = false → restore whatever was on the clipboard before
        /// </summary>
        public static void Deliver(Bitmap bmp, bool keepOnClipboard)
        {
            // Snapshot previous clipboard so we can restore when user doesn't want to keep the image there.
            IDataObject? previous = null;
            if (!keepOnClipboard)
            {
                try { previous = Clipboard.GetDataObject(); } catch { /* empty / locked */ }
            }

            // 1) Put image on clipboard (required path for almost every app).
            SetClipboardImage(bmp);

            // 2) Give the target a moment to notice the new content.
            Thread.Sleep(50);

            // 3) Paste into whatever currently has focus.
            SendCtrlV();

            // 4) Restore old clipboard if user asked not to keep the screenshot there.
            if (!keepOnClipboard)
            {
                Thread.Sleep(120); // let the target finish reading CF_BITMAP
                try
                {
                    if (previous != null)
                        Clipboard.SetDataObject(previous, true);
                    else
                        Clipboard.Clear();
                }
                catch { /* ignore restore failures */ }
            }
        }

        private static void SetClipboardImage(Bitmap bmp)
        {
            Exception? last = null;
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    // Clone so caller can dispose their copy safely.
                    using var clone = new Bitmap(bmp);
                    Clipboard.SetImage(clone);
                    last = null;
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(40);
                }
            }
            if (last != null) throw last;
        }

        /// <summary>
        /// Synthesizes a clean Ctrl+V — this, and only this, is what actually gets
        /// recognized by Electron/Chromium chat UIs (they listen for real keyboard
        /// events, not the classic WM_PASTE message, so that path was dropped
        /// entirely — it can't paste into these apps and risked a double-paste if
        /// combined with a working method).
        ///
        /// Everything here goes through SendInput exclusively. The previous version
        /// released modifiers via the older keybd_event() while doing the actual
        /// combo via SendInput — mixing the two APIs for one logical action is an
        /// unnecessary inconsistency and was dropped in favor of one call.
        ///
        /// Alt is still physically down when this runs (RegisterHotKey's WM_HOTKEY
        /// only fires while the modifier is held — the user's fingers haven't
        /// released Alt yet by the time this executes, a handful of milliseconds
        /// later). If we fire Ctrl+V while Alt/Shift/Win are still logically down,
        /// the target sees Ctrl+Alt+V — bound to nothing — and silently drops it.
        /// GetAsyncKeyState is checked live for each modifier so we only release
        /// ones that are ACTUALLY down right now, rather than blindly firing KEYUP
        /// for all of them, and we give that release a moment to land before
        /// starting the Ctrl+V combo.
        /// </summary>
        private static void SendCtrlV()
        {
            try
            {
                var release = new List<INPUT>();
                void ReleaseIfDown(ushort vk)
                {
                    if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                        release.Add(VkInput(vk, down: false));
                }

                ReleaseIfDown(VK_LMENU);
                ReleaseIfDown(VK_RMENU);
                ReleaseIfDown(VK_MENU);
                ReleaseIfDown(VK_LSHIFT);
                ReleaseIfDown(VK_RSHIFT);
                ReleaseIfDown(VK_SHIFT);
                ReleaseIfDown(VK_LWIN);
                ReleaseIfDown(VK_RWIN);

                if (release.Count > 0)
                {
                    SendInput((uint)release.Count, release.ToArray(), Marshal.SizeOf<INPUT>());
                    Thread.Sleep(30); // let the release land before the combo starts
                }

                // Scan codes are more reliable across keyboard layouts than raw VK codes
                // for the actual key combo.
                ushort scanCtrl = (ushort)MapVirtualKey(VK_CONTROL, 0);
                ushort scanV = (ushort)MapVirtualKey(VK_V, 0);

                INPUT[] combo =
                {
                    ScanInput(scanCtrl, down: true),
                    ScanInput(scanV, down: true),
                    ScanInput(scanV, down: false),
                    ScanInput(scanCtrl, down: false),
                };

                SendInput((uint)combo.Length, combo, Marshal.SizeOf<INPUT>());
            }
            catch { /* best effort */ }
        }

        private static INPUT VkInput(ushort vk, bool down) => new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = down ? 0u : KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        private static INPUT ScanInput(ushort scan, bool down) => new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = down
                        ? KEYEVENTF_SCANCODE
                        : KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_V = 0x56;
        private const ushort VK_MENU = 0x12;
        private const ushort VK_LMENU = 0xA4;
        private const ushort VK_RMENU = 0xA5;
        private const ushort VK_SHIFT = 0x10;
        private const ushort VK_LSHIFT = 0xA0;
        private const ushort VK_RSHIFT = 0xA1;
        private const ushort VK_LWIN = 0x5B;
        private const ushort VK_RWIN = 0x5C;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL, wParamH;
        }

        // Explicit union sized to the largest member (MOUSEINPUT on x64 = 24 bytes + alignment).
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion U;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
