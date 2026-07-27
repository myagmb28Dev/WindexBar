namespace WindexBar.Windows;

internal static class AppLog
{
    public static void Write(string message, Exception? error = null)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDirectory = Path.Combine(appData, "WindexBar");
            Directory.CreateDirectory(logDirectory);
            var detail = error is null ? string.Empty : $"{Environment.NewLine}{error}";
            File.AppendAllText(
                Path.Combine(logDirectory, "windexbar.log"),
                $"[{DateTimeOffset.Now:O}] {message}{detail}{Environment.NewLine}");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
        }
    }
}
