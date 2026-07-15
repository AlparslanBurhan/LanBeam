using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanBeam.Core.Settings;

/// <summary>%APPDATA%\LanBeam\settings.json altında ayarları okur/yazar.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly object _gate = new();

    public string DataDirectory { get; }
    public string SettingsPath { get; }
    public AppSettings Current { get; private set; }

    public SettingsStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanBeam");
        Directory.CreateDirectory(DataDirectory);
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (loaded is not null)
                {
                    loaded.StreamCount = Math.Clamp(loaded.StreamCount, 1, 8);
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // Bozuk ayar dosyası: varsayılanlarla devam et, kaydetme sırasında üzerine yazılır.
        }

        var fresh = new AppSettings();
        Save(fresh);
        return fresh;
    }

    public void Save(AppSettings? settings = null)
    {
        lock (_gate)
        {
            if (settings is not null)
                Current = settings;

            string tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
    }
}
