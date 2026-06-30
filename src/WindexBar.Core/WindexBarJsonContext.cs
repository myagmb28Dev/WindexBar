using System.Text.Json.Serialization;
using WindexBar.Core.Config;
using WindexBar.Core.Providers.Codex;

namespace WindexBar.Core;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WindexBarConfig))]
[JsonSerializable(typeof(RpcRateLimitsResponse))]
[JsonSerializable(typeof(RpcAccountResponse))]
internal sealed partial class WindexBarJsonContext : JsonSerializerContext;
