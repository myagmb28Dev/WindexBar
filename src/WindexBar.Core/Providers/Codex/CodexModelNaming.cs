using WindexBar.Core.Models;

namespace WindexBar.Core.Providers.Codex;

internal static class CodexModelNaming
{
    private static readonly string[] ReasoningSuffixes =
    [
        " ultra reasoning effort", " max reasoning effort", " ultra reasoning", " max reasoning",
        " extra high reasoning effort", " extra high reasoning", " xhigh reasoning effort", " xhigh reasoning",
        " high reasoning effort", " high reasoning", " medium reasoning effort", " medium reasoning",
        " low reasoning effort", " low reasoning", " minimal reasoning effort", " minimal reasoning",
        " no reasoning", " none reasoning", " extra high", " ultra", " max", " xhigh", " high",
        " medium", " low", " minimal", " none", " reasoning effort", " reasoning"
    ];

    public static string FormatModelName(string rawName)
    {
        var normalized = rawName.Replace('_', ' ').Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Codex";
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (words.Count >= 2
            && string.Equals(words[0], "gpt", StringComparison.OrdinalIgnoreCase)
            && char.IsDigit(words[1][0]))
        {
            words[0] = $"GPT-{words[1]}";
            words.RemoveAt(1);
        }

        while (words.Count > 0
            && (string.Equals(words[^1], "reasoning", StringComparison.OrdinalIgnoreCase)
                || string.Equals(words[^1], "effort", StringComparison.OrdinalIgnoreCase)))
        {
            words.RemoveAt(words.Count - 1);
        }

        if (words.Count >= 2
            && string.Equals(words[^2], "extra", StringComparison.OrdinalIgnoreCase)
            && string.Equals(words[^1], "high", StringComparison.OrdinalIgnoreCase))
        {
            words.RemoveAt(words.Count - 1);
            words[^1] = "xhigh";
        }

        return string.Join(" ", words.Select(FormatModelWord));
    }

    public static string NormalizeModelKey(string value)
    {
        var chars = StripReasoningSuffix(value)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    public static CodexModelSelection CreateSelection(
        string model,
        string? effort,
        string? serviceTier,
        DateTimeOffset? updatedAt)
    {
        var normalizedEffort = NormalizeEffort(effort);
        var normalizedServiceTier = NormalizeServiceTier(serviceTier);
        var effortName = normalizedEffort switch
        {
            "ultra" => "Ultra",
            "max" => "Max",
            "xhigh" => "XHigh",
            "high" => "High",
            "medium" => "Medium",
            "low" => "Low",
            "minimal" => "Minimal",
            "none" => "None",
            _ => null
        };
        var serviceTierName = normalizedServiceTier == "fast" ? "Fast" : null;
        var displayName = string.Join(" ", new[] { FormatModelName(model), effortName, serviceTierName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        return new CodexModelSelection(model, normalizedEffort, normalizedServiceTier, displayName, updatedAt);
    }

    private static string StripReasoningSuffix(string rawName)
    {
        var trimmed = rawName.Replace('_', ' ').Replace('-', ' ').Trim();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var suffix in ReasoningSuffixes)
            {
                if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                trimmed = trimmed[..^suffix.Length].Trim();
                changed = true;
                break;
            }
        }

        return trimmed;
    }

    private static string FormatModelWord(string word)
    {
        if (word.StartsWith("GPT-", StringComparison.OrdinalIgnoreCase))
        {
            return "GPT-" + word[4..];
        }

        return word.ToLowerInvariant() switch
        {
            "gpt" => "GPT",
            "codex" => "Codex",
            "spark" => "Spark",
            "xhigh" => "XHigh",
            _ when word.All(character => !char.IsLetter(character)) => word,
            _ => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
        };
    }

    private static string? NormalizeEffort(string? effort)
    {
        var trimmed = effort?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    private static string? NormalizeServiceTier(string? serviceTier)
    {
        var trimmed = serviceTier?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return trimmed.ToLowerInvariant() switch
        {
            "priority" => "fast",
            "default" or "normal" => "standard",
            var normalized => normalized
        };
    }
}
