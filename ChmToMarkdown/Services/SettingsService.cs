using System;
using System.IO;
using System.Text.Json;

namespace ChmToMarkdown.Services
{
    public class AppSettings
    {
        public string ChmPath { get; set; } = string.Empty;
        public string OutputDir { get; set; } = string.Empty;
        public bool IsMultiFile { get; set; } = true;
    }

    public static class SettingsService
    {
        private static readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ChmToMarkdown", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    string json = File.ReadAllText(_path);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
