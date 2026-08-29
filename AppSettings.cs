using System;
using System.IO;
using System.Text.Json;

namespace ScreenSnipAlpha
{
    public sealed class AppSettings
    {
        /// <summary>
        /// After pasting into the focused field, also leave the screenshot on the clipboard.
        /// false = restore previous clipboard contents after paste.
        /// Paste itself always happens.
        /// </summary>
        public bool KeepOnClipboard { get; set; } = true;

        public int ModifierVk { get; set; } = 0x12; // Alt
        public int CaptureVk { get; set; } = 0x56;  // V
        public int SourceVk { get; set; } = 0x53;   // S

        public string ModifierName { get; set; } = "Alt";
        public string CaptureKeyName { get; set; } = "V";
        public string SourceKeyName { get; set; } = "S";

        static string Path =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ScreenSnipAlpha",
                "settings.json");

        public static AppSettings Load()
        {
            try
            {
                var path = Path;
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) return s;
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
