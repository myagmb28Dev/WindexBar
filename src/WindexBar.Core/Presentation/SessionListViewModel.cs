using WindexBar.Core.Formatting;
using WindexBar.Core.Models;

namespace WindexBar.Core.Presentation;

public sealed record SessionCardViewModel(
    string SessionId,
    string DisplayName,
    string? ProjectPath,
    DateTimeOffset UpdatedAt,
    string ContextPercentText,
    double ContextPercent,
    string TokenDetails);

public sealed record SessionProjectViewModel(
    string Key,
    string ProjectName,
    bool IsNonProject,
    IReadOnlyList<SessionCardViewModel> Sessions);

public sealed record SessionListViewModel(
    string EmptyMessage,
    string ContextLabel,
    IReadOnlyList<SessionProjectViewModel> Projects);

public static class SessionListViewModelFactory
{
    public static SessionListViewModel Create(
        IReadOnlyList<CodexSessionUsageSnapshot>? sessions,
        bool projectSessionsFirst,
        string language)
    {
        var emptyMessage = Text(language, "No session token usage", "\uC138\uC158 \uD1A0\uD070 \uC0AC\uC6A9\uB7C9 \uC5C6\uC74C");
        var contextLabel = Text(language, "Context", "\uCEE8\uD14D\uC2A4\uD2B8");
        if (sessions is null || sessions.Count == 0)
        {
            return new SessionListViewModel(emptyMessage, contextLabel, []);
        }

        var projects = sessions
            .GroupBy(session => SessionGroupKey(session.ProjectPath), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedSessions = group.OrderByDescending(item => item.UpdatedAt).ToArray();
                var projectPath = orderedSessions[0].ProjectPath;
                var isNonProject = string.IsNullOrWhiteSpace(projectPath) || IsDefaultSessionPath(projectPath);
                var cards = orderedSessions.Select(session => CreateCard(session, language)).ToArray();
                return new SessionProjectViewModel(
                    group.Key,
                    SessionProjectDisplayName(projectPath, cards.Length, language),
                    isNonProject,
                    cards);
            })
            .OrderBy(group => projectSessionsFirst ? (group.IsNonProject ? 1 : 0) : (group.IsNonProject ? 0 : 1))
            .ThenBy(group => group.ProjectName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new SessionListViewModel(emptyMessage, contextLabel, projects);
    }

    private static SessionCardViewModel CreateCard(CodexSessionUsageSnapshot session, string language)
    {
        var percent = TokenContextPercent(session.TokenUsage);
        return new SessionCardViewModel(
            session.SessionId,
            SessionDisplayName(session, language),
            session.ProjectPath,
            session.UpdatedAt,
            percent is null ? Text(language, "unknown", "\uC54C \uC218 \uC5C6\uC74C") : $"{percent.Value:0.#}%",
            percent ?? 0,
            FormatTokenDetails(session.TokenUsage, language));
    }

    private static string FormatTokenDetails(TokenUsageSnapshot tokenUsage, string language)
    {
        var values = new List<string>();
        var current = tokenUsage.Last ?? tokenUsage.Total;
        if (current is not null && tokenUsage.ModelContextWindow is { } contextWindow)
        {
            values.Add($"{TokenCountFormatter.Format(current.TotalTokens, language)} / {TokenCountFormatter.Format(contextWindow, language)}");
        }
        else if (current is not null)
        {
            values.Add(TokenCountFormatter.Format(current.TotalTokens, language));
        }

        if (tokenUsage.Total is not null)
        {
            values.Add($"{Text(language, "Session total", "\uC138\uC158 \uD569\uACC4")}: {TokenCountFormatter.Format(tokenUsage.Total.TotalTokens, language)}");
        }

        return values.Count == 0 ? Text(language, "unknown", "\uC54C \uC218 \uC5C6\uC74C") : string.Join(Environment.NewLine, values);
    }

    private static string SessionDisplayName(CodexSessionUsageSnapshot session, string language)
    {
        if (!string.IsNullOrWhiteSpace(session.SessionName))
        {
            return session.SessionName;
        }

        var shortId = session.SessionId.Length <= 8 ? session.SessionId : session.SessionId[..8];
        return $"{Text(language, "Session", "\uC138\uC158")} {shortId}";
    }

    private static string SessionProjectDisplayName(string? projectPath, int sessionCount, string language)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return Text(language, sessionCount == 1 ? "General session" : "General sessions", "\uC77C\uBC18 \uC138\uC158");
        }

        if (IsDefaultSessionPath(projectPath))
        {
            return Text(language, "No project", "\uD504\uB85C\uC81D\uD2B8 \uC5C6\uC74C");
        }

        var normalized = NormalizeProjectPath(projectPath);
        var name = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(name) ? normalized : name;
    }

    private static string NormalizeProjectPath(string? projectPath) =>
        string.IsNullOrWhiteSpace(projectPath)
            ? string.Empty
            : projectPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string SessionGroupKey(string? projectPath) =>
        IsDefaultSessionPath(projectPath) ? "default-session" : NormalizeProjectPath(projectPath);

    private static bool IsDefaultSessionPath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return false;
        }

        var normalized = NormalizeProjectPath(projectPath);
        var folderName = Path.GetFileName(normalized);
        var datePath = Path.GetDirectoryName(normalized);
        var codexPath = string.IsNullOrWhiteSpace(datePath) ? null : Path.GetDirectoryName(datePath);
        var documentsPath = string.IsNullOrWhiteSpace(codexPath) ? null : Path.GetDirectoryName(codexPath);
        var dateFolder = string.IsNullOrWhiteSpace(datePath) ? null : Path.GetFileName(datePath);
        return !string.IsNullOrWhiteSpace(folderName)
            && string.Equals(Path.GetFileName(codexPath), "Codex", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(documentsPath), "Documents", StringComparison.OrdinalIgnoreCase)
            && DateTime.TryParseExact(
                dateFolder,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _);
    }

    private static double? TokenContextPercent(TokenUsageSnapshot? tokenUsage)
    {
        var current = tokenUsage?.Last ?? tokenUsage?.Total;
        if (current is null || tokenUsage?.ModelContextWindow is not { } contextWindow || contextWindow <= 0)
        {
            return null;
        }

        return Math.Clamp(current.TotalTokens * 100d / contextWindow, 0, 100);
    }

    private static string Text(string language, string english, string korean) =>
        string.Equals(language, "ko", StringComparison.OrdinalIgnoreCase) ? korean : english;
}
