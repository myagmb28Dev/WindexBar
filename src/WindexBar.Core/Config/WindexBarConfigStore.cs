using System.Text.Json;
using WindexBar.Core;

namespace WindexBar.Core.Config;

public sealed class WindexBarConfigStore
{
    public WindexBarConfigStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultPath();
    }

    public string FilePath { get; }

    public WindexBarConfig LoadOrCreateDefault()
    {
        if (!File.Exists(FilePath))
        {
            var created = WindexBarConfig.Default();
            Save(created);
            return created;
        }

        var json = File.ReadAllText(FilePath);
        var config = JsonSerializer.Deserialize(json, WindexBarJsonContext.Default.WindexBarConfig) ?? WindexBarConfig.Default();
        return config.Normalized();
    }

    public void Save(WindexBarConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = JsonSerializer.Serialize(config.Normalized(), WindexBarJsonContext.Default.WindexBarConfig);
        File.WriteAllText(FilePath, json);
    }

    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "WindexBar", "config.json");
    }
}

