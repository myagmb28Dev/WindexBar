using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBar.Core.Providers.Codex;

public sealed class RpcRateLimitsResponse
{
    [JsonPropertyName("rateLimits")]
    public RpcRateLimitSnapshot RateLimits { get; set; } = new();
}

public sealed class RpcRateLimitSnapshot
{
    private const string PrimaryKey = "primary";
    private const string SecondaryKey = "secondary";
    private const string CreditsKey = "credits";
    private const string PlanTypeKey = "planType";

    [JsonPropertyName("primary")]
    public RpcRateLimitWindow? Primary { get; set; }

    [JsonPropertyName("secondary")]
    public RpcRateLimitWindow? Secondary { get; set; }

    [JsonPropertyName("credits")]
    public RpcCreditsSnapshot? Credits { get; set; }

    [JsonPropertyName("planType")]
    public string? PlanType { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalRateLimits { get; set; } = [];

    public IEnumerable<KeyValuePair<string, JsonElement>> UnknownWindows()
    {
        foreach (var kvp in AdditionalRateLimits)
        {
            if (string.Equals(kvp.Key, PrimaryKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kvp.Key, SecondaryKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kvp.Key, CreditsKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kvp.Key, PlanTypeKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return kvp;
        }
    }
}

public sealed class RpcRateLimitWindow
{
    [JsonPropertyName("usedPercent")]
    public double UsedPercent { get; set; }

    [JsonPropertyName("windowDurationMins")]
    public int? WindowDurationMins { get; set; }

    [JsonPropertyName("resetsAt")]
    public long? ResetsAt { get; set; }
}

public sealed class RpcCreditsSnapshot
{
    [JsonPropertyName("hasCredits")]
    public bool HasCredits { get; set; }

    [JsonPropertyName("unlimited")]
    public bool Unlimited { get; set; }

    [JsonPropertyName("balance")]
    public string? Balance { get; set; }
}

public sealed class RpcAccountResponse
{
    [JsonPropertyName("account")]
    public RpcAccountDetails? Account { get; set; }

    [JsonPropertyName("requiresOpenAiAuth")]
    public bool? RequiresOpenAiAuth { get; set; }
}

public sealed class RpcAccountDetails
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("planType")]
    public string? PlanType { get; set; }
}
