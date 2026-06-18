using CodexBar.Core.Models;

namespace CodexBar.Core.Config;

public sealed class SettingsStore
{
    private readonly CodexBarConfigStore _store;

    public SettingsStore(CodexBarConfigStore store)
    {
        _store = store;
        Config = _store.LoadOrCreateDefault();
    }

    public CodexBarConfig Config { get; private set; }

    public ProviderConfig Codex => Config.GetProviderConfig(UsageProvider.Codex);

    public void UpdateCodex(Action<ProviderConfig> mutate)
    {
        var codex = Config.GetProviderConfig(UsageProvider.Codex);
        mutate(codex);
        Config.SetProviderConfig(codex);
        Save();
    }

    public void Reload() => Config = _store.LoadOrCreateDefault();

    public void Save() => _store.Save(Config);
}
