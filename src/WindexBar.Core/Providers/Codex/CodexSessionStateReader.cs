using System.Globalization;
using System.Text.Json;
using WindexBar.Core.Models;

namespace WindexBar.Core.Providers.Codex;

public static class CodexSessionStateReader
{
    private const int MaxSessionFilesToScan = 40;

    public static CodexModelSelection? ReadLatest(IReadOnlyDictionary<string, string>? environment = null)
    {
        return ReadLatestState(environment)?.ActiveModel;
    }

    public static CodexSessionStateSnapshot? ReadLatestState(IReadOnlyDictionary<string, string>? environment = null)
    {
        try
        {
            var codexHome = ResolveCodexHome(environment);
            if (string.IsNullOrWhiteSpace(codexHome))
            {
                return null;
            }

            var fromSession = ReadLatestSession(codexHome);
            if (fromSession is not null)
            {
                return fromSession.ActiveModel is null
                    ? fromSession with { ActiveModel = ReadConfigDefaults(codexHome) }
                    : fromSession;
            }

            var configDefault = ReadConfigDefaults(codexHome);
            return configDefault is null ? null : new CodexSessionStateSnapshot(configDefault, []);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static CodexSessionStateSnapshot? ReadLatestSession(string codexHome)
    {
        var sessionsRoot = Path.Combine(codexHome, "sessions");
        if (!Directory.Exists(sessionsRoot))
        {
            return null;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        CodexSessionStateSnapshot? latestStateWithActiveModel = null;
        CodexSessionStateSnapshot? latestStateWithLimits = null;
        CodexSessionStateSnapshot? latestStateWithTokenUsage = null;

        foreach (var file in Directory.EnumerateFiles(sessionsRoot, "rollout-*.jsonl", options)
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Take(MaxSessionFilesToScan))
        {
            var state = ReadSessionFile(file.FullName, out var isSubagent);
            if (state is null || isSubagent)
            {
                continue;
            }

            if (state.ActiveModel is not null)
            {
                if (latestStateWithActiveModel is null
                    || CompareSelectionTime(state.ActiveModel, latestStateWithActiveModel.ActiveModel) > 0)
                {
                    latestStateWithActiveModel = state;
                }

                continue;
            }

            if (state.Models.Count > 0 && latestStateWithLimits is null)
            {
                latestStateWithLimits = state;
            }

            if (state.TokenUsage is not null && latestStateWithTokenUsage is null)
            {
                latestStateWithTokenUsage = state;
            }
        }

        return latestStateWithActiveModel ?? latestStateWithLimits ?? latestStateWithTokenUsage;
    }

    private static CodexSessionStateSnapshot? ReadSessionFile(string path, out bool isSubagent)
    {
        isSubagent = false;
        CodexModelSelection? latestSelection = null;
        TokenUsageSnapshot? latestTokenUsage = null;
        var modelLimits = new List<ModelUsageSnapshot>();

        try
        {
            foreach (var line in ReadSharedLines(path))
            {
                if (!line.Contains("\"type\"", StringComparison.Ordinal)
                    || (!line.Contains("turn_context", StringComparison.Ordinal)
                        && !line.Contains("session_meta", StringComparison.Ordinal)
                        && !line.Contains("thread_settings", StringComparison.Ordinal)
                        && !line.Contains("threadSettings", StringComparison.Ordinal)
                        && !line.Contains("rate_limits", StringComparison.Ordinal)
                        && !line.Contains("rateLimits", StringComparison.Ordinal)
                        && !line.Contains("token_count", StringComparison.Ordinal)
                        && !line.Contains("token_usage", StringComparison.Ordinal)
                        && !line.Contains("tokenUsage", StringComparison.Ordinal)))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!TryGetString(root, "type", out var type) || type is null)
                    {
                        continue;
                    }

                    if (string.Equals(type, "session_meta", StringComparison.OrdinalIgnoreCase))
                    {
                        isSubagent = isSubagent || IsSubagentSession(root);
                        continue;
                    }

                    if (!root.TryGetProperty("payload", out var payload))
                    {
                        continue;
                    }

                    CodexModelSelection? selection = null;
                    if (string.Equals(type, "turn_context", StringComparison.OrdinalIgnoreCase))
                    {
                        selection = ReadTurnContext(root, payload);
                    }
                    else if (ContainsToken(type, "thread_settings") || ContainsToken(type, "threadSettings"))
                    {
                        selection = ReadThreadSettings(root, payload);
                    }

                    if (selection is not null && !IsInternalModel(selection.Model))
                    {
                        latestSelection = selection;
                    }

                    if (TryReadRateLimits(payload, out var rateLimitModel))
                    {
                        AddOrReplaceModel(modelLimits, rateLimitModel);
                    }

                    if (TryReadTokenUsage(root, payload, out var tokenUsage))
                    {
                        latestTokenUsage = tokenUsage;
                    }
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (IOException)
        {
            return latestSelection is null && modelLimits.Count == 0 && latestTokenUsage is null
                ? null
                : new CodexSessionStateSnapshot(latestSelection, modelLimits, latestTokenUsage);
        }
        catch (UnauthorizedAccessException)
        {
            return latestSelection is null && modelLimits.Count == 0 && latestTokenUsage is null
                ? null
                : new CodexSessionStateSnapshot(latestSelection, modelLimits, latestTokenUsage);
        }

        return latestSelection is null && modelLimits.Count == 0 && latestTokenUsage is null
            ? null
            : new CodexSessionStateSnapshot(latestSelection, modelLimits, latestTokenUsage);
    }

    private static bool ContainsToken(string value, string token) =>
        value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static IEnumerable<string> ReadSharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    private static int CompareSelectionTime(CodexModelSelection lhs, CodexModelSelection? rhs)
    {
        if (rhs is null)
        {
            return 1;
        }

        return Nullable.Compare(lhs.UpdatedAt, rhs.UpdatedAt);
    }

    private static bool IsInternalModel(string model) =>
        model.IndexOf("auto-review", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsSubagentSession(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload))
        {
            return false;
        }

        if (TryGetString(payload, "thread_source", out var threadSource)
            && string.Equals(threadSource, "subagent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return payload.TryGetProperty("source", out var source)
            && source.ValueKind == JsonValueKind.Object
            && source.TryGetProperty("subagent", out _);
    }

    private static CodexModelSelection? ReadTurnContext(JsonElement root, JsonElement payload)
    {
        var model = ReadModel(payload);
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var effort = ReadReasoningEffort(payload);
        var timestamp = TryGetTimestamp(root);
        return CreateSelection(model, effort, timestamp);
    }

    private static CodexModelSelection? ReadThreadSettings(JsonElement root, JsonElement payload)
    {
        var settings = payload.TryGetProperty("threadSettings", out var threadSettings)
            ? threadSettings
            : payload.TryGetProperty("thread_settings", out var snakeThreadSettings) ? snakeThreadSettings : payload;
        var model = TryReadCollaborationModeModel(settings, out var collaborationModel)
            ? collaborationModel
            : ReadModel(settings);
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var effort = TryReadCollaborationModeReasoningEffort(settings, out var collaborationEffort)
            ? collaborationEffort
            : ReadReasoningEffort(settings);
        var timestamp = TryGetTimestamp(root);
        return CreateSelection(model, effort, timestamp);
    }

    private static bool TryReadRateLimits(JsonElement payload, out ModelUsageSnapshot model)
    {
        model = null!;
        if (!TryGetObject(payload, "rate_limits", out var limits)
            && !TryGetObject(payload, "rateLimits", out limits))
        {
            return false;
        }

        var current = TryReadRateWindow(limits, "primary");
        var weekly = TryReadRateWindow(limits, "secondary");
        if (current is null && weekly is null)
        {
            return false;
        }

        model = new ModelUsageSnapshot(ReadRateLimitModelName(limits), current, weekly);
        return true;
    }

    private static RateWindow? TryReadRateWindow(JsonElement limits, string name)
    {
        if (!TryGetObject(limits, name, out var window)
            || !TryGetDoubleAny(window, out var usedPercent, "used_percent", "usedPercent"))
        {
            return null;
        }

        int? windowMinutes = null;
        if (TryGetIntAny(window, out var parsedMinutes, "window_minutes", "windowDurationMins", "windowDurationMinutes"))
        {
            windowMinutes = parsedMinutes;
        }

        DateTimeOffset? resetsAt = null;
        if (TryGetLongAny(window, out var resetsAtUnix, "resets_at", "resetsAt"))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetsAtUnix);
        }

        var resetDescription = resetsAt.HasValue ? CodexUsageMapper.ResetDescription(resetsAt.Value) : null;
        return new RateWindow(usedPercent, windowMinutes, resetsAt, resetDescription);
    }

    private static string ReadRateLimitModelName(JsonElement limits)
    {
        return TryGetStringAny(limits, out var limitName, "limit_name", "limitName")
            || TryGetStringAny(limits, out limitName, "limit_id", "limitId")
            ? CodexUsageMapper.FormatModelName(limitName!)
            : "Codex";
    }

    private static bool TryReadTokenUsage(JsonElement root, JsonElement payload, out TokenUsageSnapshot tokenUsage)
    {
        tokenUsage = null!;
        if (!TryGetObject(payload, "info", out var info))
        {
            return false;
        }

        _ = TryReadTokenUsageBreakdown(info, out var total, "total_token_usage", "totalTokenUsage");
        _ = TryReadTokenUsageBreakdown(info, out var last, "last_token_usage", "lastTokenUsage");
        if (total is null && last is null)
        {
            return false;
        }

        int? modelContextWindow = null;
        if (TryGetIntAny(info, out var parsedWindow, "model_context_window", "modelContextWindow"))
        {
            modelContextWindow = parsedWindow;
        }

        tokenUsage = new TokenUsageSnapshot(total, last, modelContextWindow, TryGetTimestamp(root));
        return tokenUsage.HasUsage;
    }

    private static bool TryReadTokenUsageBreakdown(JsonElement info, out TokenUsageBreakdown? usage, params string[] names)
    {
        usage = null;
        if (!TryGetPropertyAny(info, out var tokenNode, names)
            || tokenNode.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var hasInput = TryGetLongAny(tokenNode, out var inputTokens, "input_tokens", "inputTokens");
        var hasCached = TryGetLongAny(tokenNode, out var cachedInputTokens, "cached_input_tokens", "cachedInputTokens");
        var hasOutput = TryGetLongAny(tokenNode, out var outputTokens, "output_tokens", "outputTokens");
        var hasReasoning = TryGetLongAny(tokenNode, out var reasoningOutputTokens, "reasoning_output_tokens", "reasoningOutputTokens");
        var hasTotal = TryGetLongAny(tokenNode, out var totalTokens, "total_tokens", "totalTokens");
        if (!hasInput && !hasCached && !hasOutput && !hasReasoning && !hasTotal)
        {
            return false;
        }

        if (!hasTotal)
        {
            totalTokens = inputTokens + outputTokens;
        }

        usage = new TokenUsageBreakdown(
            inputTokens,
            cachedInputTokens,
            outputTokens,
            reasoningOutputTokens,
            totalTokens);
        return true;
    }

    private static void AddOrReplaceModel(List<ModelUsageSnapshot> models, ModelUsageSnapshot model)
    {
        var existingIndex = models.FindIndex(candidate => string.Equals(candidate.ModelName, model.ModelName, StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
        {
            models.Add(model);
            return;
        }

        models[existingIndex] = model;
    }

    private static string? ReadModel(JsonElement payload)
    {
        if (TryGetModelString(payload, "model", out var model))
        {
            return model;
        }

        if (TryGetModelString(payload, "modelName", out var modelFromCamelAlias))
        {
            return modelFromCamelAlias;
        }

        if (TryGetModelString(payload, "model_name", out var modelFromAlias))
        {
            return modelFromAlias;
        }

        if (TryGetModelString(payload, "selectedModel", out var camelSelectedModel))
        {
            return camelSelectedModel;
        }

        if (TryGetModelString(payload, "selected_model", out var selectedModel))
        {
            return selectedModel;
        }

        return TryGetCollaborationModeSetting(payload, "model", out var fallback)
            ? fallback
            : TryGetCollaborationModeSetting(payload, "model_name", out var fallbackAlias) ? fallbackAlias : null;
    }

    private static string? ReadReasoningEffort(JsonElement payload)
    {
        if (TryGetString(payload, "effort", out var effort))
        {
            return effort;
        }

        if (TryGetString(payload, "reasoning_effort", out effort))
        {
            return effort;
        }

        if (TryGetString(payload, "reasoningEffort", out effort))
        {
            return effort;
        }

        if (TryGetString(payload, "model_reasoning_effort", out effort))
        {
            return effort;
        }

        if (TryGetString(payload, "modelReasoningEffort", out effort))
        {
            return effort;
        }

        if (TryGetModelEffortFromObject(payload, out effort))
        {
            return effort;
        }

        return TryReadCollaborationModeReasoningEffort(payload, out var fallback) ? fallback : null;
    }

    private static bool TryReadCollaborationModeModel(JsonElement payload, out string? value) =>
        TryGetCollaborationModeSetting(payload, "model", out value)
        || TryGetCollaborationModeSetting(payload, "model_name", out value);

    private static bool TryReadCollaborationModeReasoningEffort(JsonElement payload, out string? value) =>
        TryGetCollaborationModeSetting(payload, "reasoning_effort", out value)
        || TryGetCollaborationModeSetting(payload, "reasoningEffort", out value)
        || TryGetCollaborationModeSetting(payload, "model_reasoning_effort", out value)
        || TryGetCollaborationModeSetting(payload, "modelReasoningEffort", out value)
        || TryGetCollaborationModeSetting(payload, "effort", out value);

    private static bool TryGetCollaborationModeSetting(JsonElement payload, string name, out string? value)
    {
        value = null;
        if (!TryGetObject(payload, "collaboration_mode", out var collaborationMode)
            && !TryGetObject(payload, "collaborationMode", out collaborationMode))
        {
            return false;
        }

        if (!TryGetObject(collaborationMode, "settings", out var settings))
        {
            return false;
        }

        return TryGetString(settings, name, out value)
            || TryGetString(settings, ToCamelCase(name), out value);
    }

    private static bool TryGetObject(JsonElement element, string name, out JsonElement value)
    {
        if (!element.TryGetProperty(name, out value)
            || value.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        return true;
    }

    private static bool TryGetPropertyAny(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value)
                && value.ValueKind != JsonValueKind.Null
                && value.ValueKind != JsonValueKind.Undefined)
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetStringAny(JsonElement element, out string? value, params string[] names)
    {
        value = null;
        if (!TryGetPropertyAny(element, out var property, names)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetDoubleAny(JsonElement element, out double value, params string[] names)
    {
        value = 0;
        return TryGetPropertyAny(element, out var property, names) && TryReadDouble(property, out value);
    }

    private static bool TryGetIntAny(JsonElement element, out int value, params string[] names)
    {
        value = 0;
        if (!TryGetPropertyAny(element, out var property, names)
            || !TryReadInt64(property, out var parsed))
        {
            return false;
        }

        value = (int)parsed;
        return true;
    }

    private static bool TryGetLongAny(JsonElement element, out long value, params string[] names)
    {
        value = 0;
        return TryGetPropertyAny(element, out var property, names) && TryReadInt64(property, out value);
    }

    private static bool TryReadDouble(JsonElement value, out double result)
    {
        if (value.ValueKind is JsonValueKind.Number)
        {
            return value.TryGetDouble(out result);
        }

        if (value.ValueKind is JsonValueKind.String)
        {
            return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        result = 0;
        return false;
    }

    private static bool TryReadInt64(JsonElement value, out long result)
    {
        if (value.ValueKind is JsonValueKind.Number)
        {
            return value.TryGetInt64(out result);
        }

        if (value.ValueKind is JsonValueKind.String)
        {
            return long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        result = 0;
        return false;
    }

    private static string ToCamelCase(string name)
    {
        var builder = new System.Text.StringBuilder();
        var upperNext = false;
        foreach (var character in name)
        {
            if (character == '_')
            {
                upperNext = true;
                continue;
            }

            builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
            upperNext = false;
        }

        return builder.ToString();
    }

    private static bool TryGetModelString(JsonElement payload, string propertyName, out string? value)
    {
        value = null;
        if (!payload.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        if (property.ValueKind == JsonValueKind.Object)
        {
            return TryGetString(property, "name", out value)
                || TryGetString(property, "id", out value)
                || TryGetString(property, "value", out value);
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            if (property.TryGetDouble(out var doubleValue))
            {
                value = doubleValue.ToString(CultureInfo.InvariantCulture);
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetModelEffortFromObject(JsonElement payload, out string? effort)
    {
        if (!payload.TryGetProperty("model", out var model))
        {
            effort = null;
            return false;
        }

        if (model.ValueKind != JsonValueKind.Object)
        {
            effort = null;
            return false;
        }

        if (TryGetString(model, "effort", out effort))
        {
            return true;
        }

        if (TryGetString(model, "reasoning_effort", out effort))
        {
            return true;
        }

        if (TryGetString(model, "reasoningEffort", out effort))
        {
            return true;
        }

        return false;
    }

    private static CodexModelSelection? ReadConfigDefaults(string codexHome)
    {
        var configPath = Path.Combine(codexHome, "config.toml");
        if (!File.Exists(configPath))
        {
            return null;
        }

        string? model = null;
        string? effort = null;

        foreach (var rawLine in File.ReadLines(configPath))
        {
            var line = StripTomlComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryReadTomlString(line, "model", out var parsedModel))
            {
                model = parsedModel;
                continue;
            }

            if (TryReadTomlString(line, "model_reasoning_effort", out var parsedEffort))
            {
                effort = parsedEffort;
            }
        }

        return string.IsNullOrWhiteSpace(model) ? null : CreateSelection(model, effort, File.GetLastWriteTimeUtc(configPath));
    }

    private static string StripTomlComment(string line)
    {
        var inString = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"' && (index == 0 || line[index - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (line[index] == '#' && !inString)
            {
                return line[..index];
            }
        }

        return line;
    }

    private static bool TryReadTomlString(string line, string key, out string? value)
    {
        value = null;
        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        var lhs = line[..separator].Trim();
        if (!string.Equals(lhs, key, StringComparison.Ordinal))
        {
            return false;
        }

        var rhs = line[(separator + 1)..].Trim();
        if (rhs.Length >= 2 && rhs[0] == '"' && rhs[^1] == '"')
        {
            value = rhs[1..^1];
            return true;
        }

        value = rhs.Length == 0 ? null : rhs;
        return value is not null;
    }

    private static CodexModelSelection CreateSelection(string model, string? effort, DateTimeOffset? updatedAt)
    {
        var modelName = CodexUsageMapper.FormatModelName(model);
        var effortName = FormatReasoningEffort(effort);
        var displayName = string.IsNullOrWhiteSpace(effortName) ? modelName : $"{modelName} {effortName}";
        return new CodexModelSelection(model, NormalizeEffort(effort), displayName, updatedAt);
    }

    private static string? FormatReasoningEffort(string? effort)
    {
        return NormalizeEffort(effort) switch
        {
            "xhigh" => "XHigh",
            "high" => "High",
            "medium" => "Medium",
            "low" => "Low",
            "minimal" => "Minimal",
            "none" => "None",
            _ => null
        };
    }

    private static string? NormalizeEffort(string? effort)
    {
        var trimmed = effort?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    private static DateTimeOffset? TryGetTimestamp(JsonElement root)
    {
        if (TryGetString(root, "timestamp", out var timestamp)
            && DateTimeOffset.TryParse(timestamp, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? ResolveCodexHome(IReadOnlyDictionary<string, string>? environment)
    {
        if (TryGetEnvironmentValue(environment, "CODEX_HOME", out var codexHome))
        {
            return codexHome;
        }

        if (TryGetEnvironmentValue(environment, "USERPROFILE", out var userProfile))
        {
            return Path.Combine(userProfile, ".codex");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile) ? null : Path.Combine(profile, ".codex");
    }

    private static bool TryGetEnvironmentValue(IReadOnlyDictionary<string, string>? environment, string key, out string value)
    {
        value = string.Empty;
        if (environment is not null && environment.TryGetValue(key, out var fromEnvironment) && !string.IsNullOrWhiteSpace(fromEnvironment))
        {
            value = fromEnvironment;
            return true;
        }

        var processValue = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(processValue))
        {
            return false;
        }

        value = processValue;
        return true;
    }

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }
}

public sealed record CodexSessionStateSnapshot(
    CodexModelSelection? ActiveModel,
    IReadOnlyList<ModelUsageSnapshot> Models,
    TokenUsageSnapshot? TokenUsage = null);
