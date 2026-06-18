using CodexBar.Core.Models;
using CodexBar.Core.Providers;

namespace CodexBar.Core.Providers.Codex;

public static class CodexProviderDescriptor
{
    public static ProviderDescriptor Create(ICodexRpcTransportFactory? transportFactory = null) => new(
        UsageProvider.Codex,
        "Codex",
        "Session",
        "Weekly",
        "codex",
        true,
        new ProviderFetchPipeline([new CodexCliFetchStrategy(transportFactory)]));
}

