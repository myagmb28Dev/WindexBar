using System.Diagnostics;
using System.Text;

namespace WindexBar.Core.Providers.Codex;

public sealed class ProcessCodexRpcTransportFactory : ICodexRpcTransportFactory
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public ICodexRpcTransport Start(string executablePath, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Codex app-server.");
        return new ProcessCodexRpcTransport(process);
    }
}

public sealed class ProcessCodexRpcTransport : ICodexRpcTransport
{
    private readonly Process _process;
    private readonly Task _stderrDrain;

    public ProcessCodexRpcTransport(Process process)
    {
        _process = process;
        _stderrDrain = Task.Run(async () =>
        {
            try
            {
                while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { })
                {
                    // Drain stderr so the child process cannot block on a full pipe.
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        return await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        Kill();
        try
        {
            await _stderrDrain.ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }

        _process.Dispose();
    }
}

