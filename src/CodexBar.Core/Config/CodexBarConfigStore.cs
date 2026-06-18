using System.Text.Json;

namespace CodexBar.Core.Config;

public sealed class CodexBarConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public CodexBarConfigStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultPath();
    }

    public string FilePath { get; }

    public CodexBarConfig LoadOrCreateDefault()
    {
        if (!File.Exists(FilePath))
        {
            var created = CodexBarConfig.Default();
            Save(created);
            return created;
        }

        var json = File.ReadAllText(FilePath);
        var config = JsonSerializer.Deserialize<CodexBarConfig>(json, JsonOptions) ?? CodexBarConfig.Default();
        return config.Normalized();
    }

    public void Save(CodexBarConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = JsonSerializer.Serialize(config.Normalized(), JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "WinCodexBar", "config.json");
    }
}

