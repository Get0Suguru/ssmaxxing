using System;
using System.Windows;
using System.Windows.Controls;

namespace ScreenSnipAlpha
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Result { get; private set; }

        public SettingsWindow(AppSettings current)
        {
            InitializeComponent();
            Result = new AppSettings
            {
                KeepOnClipboard = current.KeepOnClipboard,
                ModifierVk = current.ModifierVk,
                CaptureVk = current.CaptureVk,
                SourceVk = current.SourceVk,
                ModifierName = current.ModifierName,
                CaptureKeyName = current.CaptureKeyName,
                SourceKeyName = current.SourceKeyName
            };

            foreach (ComboBoxItem item in ModBox.Items)
            {
                if (item.Tag is string tag && int.TryParse(tag, out int vk) && vk == current.ModifierVk)
                {
                    ModBox.SelectedItem = item;
                    break;
                }
            }
            if (ModBox.SelectedItem == null) ModBox.SelectedIndex = 0;

            CaptureKeyBox.Text = current.CaptureKeyName;
            SourceKeyBox.Text = current.SourceKeyName;
            KeepClipCheck.IsChecked = current.KeepOnClipboard;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (ModBox.SelectedItem is not ComboBoxItem modItem || modItem.Tag is not string tag
                || !int.TryParse(tag, out int modVk))
            {
                MessageBox.Show(this, "Pick a modifier.", "ScreenSnip");
                return;
            }

            string cap = (CaptureKeyBox.Text ?? "").Trim().ToUpperInvariant();
            string src = (SourceKeyBox.Text ?? "").Trim().ToUpperInvariant();
            if (cap.Length != 1 || src.Length != 1 || !char.IsLetterOrDigit(cap[0]) || !char.IsLetterOrDigit(src[0]))
            {
                MessageBox.Show(this, "Capture and source keys must be a single letter or digit.", "ScreenSnip");
                return;
            }
            if (cap == src)
            {
                MessageBox.Show(this, "Capture and source keys must be different.", "ScreenSnip");
                return;
            }

            Result.ModifierVk = modVk;
            Result.ModifierName = modItem.Content?.ToString() ?? "Alt";
            Result.CaptureVk = char.ToUpperInvariant(cap[0]);
            Result.SourceVk = char.ToUpperInvariant(src[0]);
            Result.CaptureKeyName = cap;
            Result.SourceKeyName = src;
            Result.KeepOnClipboard = KeepClipCheck.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
