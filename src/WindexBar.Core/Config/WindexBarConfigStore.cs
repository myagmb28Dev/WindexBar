using WindexBar.Core;
using WindexBar.Core.Persistence;

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
        try
        {
            if (!File.Exists(FilePath))
            {
                var created = WindexBarConfig.Default();
                Save(created);
                return created;
            }

            var config = JsonFileStore.LoadOrDefault(
                FilePath,
                WindexBarJsonContext.Default.WindexBarConfig,
                WindexBarConfig.Default);
            return config.Normalized();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return WindexBarConfig.Default();
        }
    }

    public void Save(WindexBarConfig config)
    {
        JsonFileStore.SaveAtomic(
            FilePath,
            config.Normalized(),
            WindexBarJsonContext.Default.WindexBarConfig);
    }

    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "WindexBar", "config.json");
    }
}

