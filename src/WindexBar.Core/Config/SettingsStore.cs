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

    public event EventHandler? Changed;

    public void Update(Action<WindexBarConfig> mutate)
    {
        mutate(Config);
        Save();
    }

    public void UpdateCodex(Action<ProviderConfig> mutate)
    {
        var codex = Config.GetProviderConfig(UsageProvider.Codex);
        mutate(codex);
        Config.SetProviderConfig(codex);
        Save();
    }

    public void Reload()
    {
        Config = _store.LoadOrCreateDefault();
        OnChanged();
    }

    public void Save()
    {
        Persist();
        OnChanged();
    }

    public void Persist()
    {
        _store.Save(Config);
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
