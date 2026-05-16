using System;
using System.IO;
using System.Text.Json;

namespace HWT905Dashboard.Services;

public sealed class AppSettings
{
    public string PortName { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 9600;
    public string OutputRateText { get; set; } = "100 Hz";
    public string SaveFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWT905Dashboard");
    public double ForwardYawOffset { get; set; }
    public bool UseForwardOffset { get; set; }

    public static string SettingsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWT905Dashboard");
    public static string SettingsFile => Path.Combine(SettingsFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            if (File.Exists(SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile));
                if (settings != null)
                    return settings;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsFolder);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFile, json);
    }
}
