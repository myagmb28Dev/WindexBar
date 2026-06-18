using WindexBar.Core.Models;

namespace WindexBar.Core.Config;

public sealed class SettingsStore
{
    private readonly WindexBarConfigStore _store;

    public SettingsStore(WindexBarConfigStore store)
    {
        _store = store;
        Config = _store.LoadOrCreateDefault();
    }

    public WindexBarConfig Config { get; private set; }

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
