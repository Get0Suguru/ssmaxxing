using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenSnipAlpha
{
    public partial class SourcePicker : Window
    {
        public StickySource? SelectedSource { get; private set; }
        /// <summary>User picked the Stop-sharing card.</summary>
        public bool RequestStopShare { get; private set; }

        public SourcePicker()
        {
            InitializeComponent();
            Loaded += (_, _) => BuildCards();
        }

        private void BuildCards()
        {
            CardPanel.Children.Clear();

            // Big "Stop sharing" card first when a live session is active
            if (App.IsSharing)
            {
                CardPanel.Children.Add(MakeActionCard(
                    title: "Stop sharing",
                    subtitle: "Clear sticky source",
                    accent: System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C),
                    onClick: () =>
                    {
                        RequestStopShare = true;
                        DialogResult = true;
                        Close();
                    }));
            }

            // Entire screen
            CardPanel.Children.Add(MakeActionCard(
                title: "Entire screen",
                subtitle: "No yellow border",
                accent: System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4),
                onClick: () =>
                {
                    SelectedSource = new StickySource { IsEntireScreen = true, Label = "Entire Screen" };
                    DialogResult = true;
                    Close();
                }));

            foreach (var w in WindowEnumerator.GetVisibleWindowsOnCurrentDesktop())
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D)),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x3F)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(6),
                    Cursor = Cursors.Hand,
                    Tag = w
                };

                var stack = new StackPanel { Margin = new Thickness(8) };

                var icon = new System.Windows.Controls.Image
                {
                    Width = 32,
                    Height = 32,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                try
                {
                    var hIcon = GetIconHandle(w.Hwnd);
                    if (hIcon != IntPtr.Zero)
                    {
                        icon.Source = Imaging.CreateBitmapSourceFromHIcon(
                            hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    }
                }
                catch { }

                stack.Children.Add(icon);
                stack.Children.Add(new TextBlock
                {
                    Text = w.Title.Length > 40 ? w.Title.Substring(0, 37) + "…" : w.Title,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 48
                });

                border.Child = stack;
                border.MouseEnter += (_, _) =>
                    border.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xB4, 0xFF));
                border.MouseLeave += (_, _) =>
                    border.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x3F));
                border.MouseLeftButtonUp += Card_Click;
                CardPanel.Children.Add(border);
            }
        }

        private Border MakeActionCard(string title, string subtitle, System.Windows.Media.Color accent, Action onClick)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D)),
                BorderBrush = new SolidColorBrush(accent),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(6),
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel { Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(accent),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            });
            border.Child = stack;
            border.MouseLeftButtonUp += (_, _) => onClick();
            return border;
        }

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border { Tag: WindowEntry w }) return;
            try
            {
                var item = CaptureInterop.CreateItemForWindow(w.Hwnd);
                SelectedSource = new StickySource
                {
                    IsEntireScreen = false,
                    Label = w.Title,
                    CaptureItem = item,
                    Hwnd = w.Hwnd
                };
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not capture that window:\n" + ex.Message,
                    "ScreenSnip", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void EntireScreen_Click(object sender, RoutedEventArgs e)
        {
            SelectedSource = new StickySource { IsEntireScreen = true, Label = "Entire Screen" };
            DialogResult = true;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private static IntPtr GetIconHandle(IntPtr hwnd)
        {
            IntPtr icon = SendMessage(hwnd, WM_GETICON, (IntPtr)ICON_SMALL2, IntPtr.Zero);
            if (icon == IntPtr.Zero)
                icon = SendMessage(hwnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
            if (icon == IntPtr.Zero)
                icon = GetClassLongPtr(hwnd, GCL_HICONSM);
            if (icon == IntPtr.Zero)
                icon = GetClassLongPtr(hwnd, GCL_HICON);
            return icon;
        }

        private const int WM_GETICON = 0x007F;
        private const int ICON_SMALL = 0;
        private const int ICON_SMALL2 = 2;
        private const int GCL_HICON = -14;
        private const int GCL_HICONSM = -34;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetClassLongPtr64(hWnd, nIndex);
            return new IntPtr(GetClassLong32(hWnd, nIndex));
        }

        [DllImport("user32.dll", EntryPoint = "GetClassLong")]
        private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);
    }
}
