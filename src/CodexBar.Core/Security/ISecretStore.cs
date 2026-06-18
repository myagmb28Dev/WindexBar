namespace CodexBar.Core.Security;

public interface ISecretStore
{
    string? Read(string key);
    void Write(string key, string value);
    void Delete(string key);
}

public sealed class NullSecretStore : ISecretStore
{
    public string? Read(string key) => null;
    public void Write(string key, string value) { }
    public void Delete(string key) { }
}
