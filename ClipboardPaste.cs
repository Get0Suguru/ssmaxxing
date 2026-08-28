using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ScreenSnipAlpha
{
    public static class ClipboardPaste
    {
        public static void SetImageAndPasteAtCursor(Bitmap bmp)
        {
            // STA retry loop: clipboard ownership can transiently fail if another
            // app is mid-write. A couple retries is enough in practice.
            Exception? last = null;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetImage(bmp);
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(50);
                }
            }
            if (last != null) throw last;

            // Small delay so the target app's clipboard listener (if any) has
            // registered the new content before we synthesize the paste.
            Thread.Sleep(30);
            SendCtrlV();
        }

        private static void SendCtrlV()
        {
            var inputs = new INPUT[4];

            inputs[0] = KeyDown(VK_CONTROL);
            inputs[1] = KeyDown(VK_V);
            inputs[2] = KeyUp(VK_V);
            inputs[3] = KeyUp(VK_CONTROL);

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        private static INPUT KeyDown(ushort vk) => new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = 0 } }
        };

        private static INPUT KeyUp(ushort vk) => new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } }
        };

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_V = 0x56;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    }
}
