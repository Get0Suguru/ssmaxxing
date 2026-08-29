using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace ScreenSnipAlpha
{
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface([In] ref Guid iid, out IntPtr p);
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    internal static class CaptureInterop
    {
        private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
        {
            const string classId = "Windows.Graphics.Capture.GraphicsCaptureItem";
            WindowsCreateString(classId, classId.Length, out IntPtr hClassId);
            try
            {
                var interopGuid = typeof(IGraphicsCaptureItemInterop).GUID;
                RoGetActivationFactory(hClassId, ref interopGuid, out IntPtr factoryPtr);
                try
                {
                    var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
                    var itemGuid = GraphicsCaptureItemGuid;
                    IntPtr itemPtr = interop.CreateForWindow(hwnd, ref itemGuid);
                    try
                    {
                        return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
                    }
                    finally
                    {
                        Marshal.Release(itemPtr);
                    }
                }
                finally
                {
                    Marshal.Release(factoryPtr);
                }
            }
            finally
            {
                WindowsDeleteString(hClassId);
            }
        }

        [DllImport("combase.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);

        [DllImport("combase.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int WindowsDeleteString(IntPtr hstring);
    }
}
