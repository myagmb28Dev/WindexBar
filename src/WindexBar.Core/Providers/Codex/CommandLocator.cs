namespace WindexBar.Core.Providers.Codex;

public static class CommandLocator
{
    public static string? ResolveExecutable(string? configuredPath, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var trimmed = configuredPath.Trim();
            if (Path.IsPathRooted(trimmed) && File.Exists(trimmed))
            {
                return trimmed;
            }

            return ResolveOnPath(trimmed, environment);
        }

        return ResolveOnPath("codex", environment);
    }

    private static string? ResolveOnPath(string command, IReadOnlyDictionary<string, string>? environment)
    {
        if (Path.IsPathRooted(command))
        {
            return File.Exists(command) ? command : null;
        }

        var paths = GetEnvironmentValue(environment, "PATH")?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];
        var extensions = GetEnvironmentValue(environment, "PATHEXT")?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            ?? [".COM", ".EXE", ".BAT", ".CMD"];

        var hasExtension = Path.HasExtension(command);
        foreach (var directory in paths)
        {
            var candidate = Path.Combine(directory.Trim(), command);
            if (hasExtension && File.Exists(candidate))
            {
                return candidate;
            }

            if (!hasExtension)
            {
                foreach (var extension in extensions)
                {
                    var withExtension = candidate + extension.ToLowerInvariant();
                    if (File.Exists(withExtension))
                    {
                        return withExtension;
                    }

                    withExtension = candidate + extension.ToUpperInvariant();
                    if (File.Exists(withExtension))
                    {
                        return withExtension;
                    }
                }
            }
        }

        return null;
    }

    private static string? GetEnvironmentValue(IReadOnlyDictionary<string, string>? environment, string key)
    {
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        return Environment.GetEnvironmentVariable(key);
    }
}

