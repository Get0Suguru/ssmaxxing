using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Windows.Graphics.Capture;

namespace ScreenSnipAlpha
{
    public partial class SourcePicker : Window
    {
        public StickySource? SelectedSource { get; private set; }

        public SourcePicker()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                ShareButton.Focus();
                await OpenSystemPickerAsync();
            };
        }

        private async void ShareButton_Click(object sender, RoutedEventArgs e)
            => await OpenSystemPickerAsync();

        private void EntireScreen_Click(object sender, RoutedEventArgs e)
        {
            SelectedSource = new StickySource { IsEntireScreen = true, Label = "Entire Screen" };
            DialogResult = true;
            Close();
        }

        private async Task OpenSystemPickerAsync()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).EnsureHandle();
                var picker = new GraphicsCapturePicker();
                BindPickerToWindow(picker, hwnd);

                GraphicsCaptureItem? item = await picker.PickSingleItemAsync();
                if (item == null)
                    return; // cancelled — keep this window so they can pick Entire Screen or retry

                SelectedSource = new StickySource
                {
                    IsEntireScreen = false,
                    Label = string.IsNullOrWhiteSpace(item.DisplayName) ? "Shared window" : item.DisplayName,
                    CaptureItem = item
                };
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "ScreenSnip", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void BindPickerToWindow(GraphicsCapturePicker picker, IntPtr hwnd)
        {
            try
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                return;
            }
            catch
            {
                // Fall through to IInitializeWithWindow for older projections.
            }

            var unk = Marshal.GetIUnknownForObject(picker);
            try
            {
                var init = (IInitializeWithWindow)Marshal.GetTypedObjectForIUnknown(unk, typeof(IInitializeWithWindow));
                init.Initialize(hwnd);
            }
            finally
            {
                Marshal.Release(unk);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }
    }
}
