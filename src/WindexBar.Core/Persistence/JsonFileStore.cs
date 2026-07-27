using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WindexBar.Core.Persistence;

internal static class JsonFileStore
{
    public static T LoadOrDefault<T>(string filePath, JsonTypeInfo<T> typeInfo, Func<T> createDefault)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return createDefault();
            }

            return JsonSerializer.Deserialize(File.ReadAllText(filePath), typeInfo) ?? createDefault();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return createDefault();
        }
    }

    public static void SaveAtomic<T>(string filePath, T value, JsonTypeInfo<T> typeInfo)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{filePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, typeInfo));
            File.Move(temporaryPath, filePath, true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public static bool TrySaveAtomic<T>(string filePath, T value, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            SaveAtomic(filePath, value, typeInfo);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
