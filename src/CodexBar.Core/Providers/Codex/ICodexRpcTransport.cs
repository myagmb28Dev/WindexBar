namespace CodexBar.Core.Providers.Codex;

public interface ICodexRpcTransport : IAsyncDisposable
{
    Task WriteLineAsync(string line, CancellationToken cancellationToken);
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);
    void Kill();
}

public interface ICodexRpcTransportFactory
{
    ICodexRpcTransport Start(string executablePath, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment);
}
