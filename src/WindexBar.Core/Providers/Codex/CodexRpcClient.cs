using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using WindexBar.Core;

namespace WindexBar.Core.Providers.Codex;

public sealed class CodexRpcException : Exception
{
    public CodexRpcException(string message) : base(message) { }
}

public sealed class CodexRpcTimeoutException : TimeoutException
{
    public CodexRpcTimeoutException(string method) : base($"Codex RPC timed out waiting for `{method}` reply.")
    {
        Method = method;
    }

    public string Method { get; }
}

public sealed class CodexRpcClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICodexRpcTransport _transport;
    private readonly TimeSpan _initializeTimeout;
    private readonly TimeSpan _requestTimeout;
    private int _nextId = 1;

    public CodexRpcClient(ICodexRpcTransport transport, TimeSpan initializeTimeout, TimeSpan requestTimeout)
    {
        _transport = transport;
        _initializeTimeout = initializeTimeout;
        _requestTimeout = requestTimeout;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await RequestAsync(
            "initialize",
            new JsonObject
            {
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "windexbar",
                    ["version"] = "0.1.0"
                }
            },
            _initializeTimeout,
            cancellationToken).ConfigureAwait(false);

        await SendAsync(new JsonObject
        {
            ["method"] = "initialized",
            ["params"] = new JsonObject()
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<RpcRateLimitsResponse> FetchRateLimitsAsync(CancellationToken cancellationToken) =>
        RequestAsync("account/rateLimits/read", null, _requestTimeout, WindexBarJsonContext.Default.RpcRateLimitsResponse, cancellationToken);

    public Task<RpcAccountResponse> FetchAccountAsync(CancellationToken cancellationToken) =>
        RequestAsync("account/read", null, _requestTimeout, WindexBarJsonContext.Default.RpcAccountResponse, cancellationToken);

    private async Task RequestAsync(string method, JsonObject? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = await RequestResultAsync(method, parameters, timeout, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RequestAsync<T>(
        string method,
        JsonObject? parameters,
        TimeSpan timeout,
        JsonTypeInfo<T> resultTypeInfo,
        CancellationToken cancellationToken)
    {
        var result = await RequestResultAsync(method, parameters, timeout, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(result.GetRawText(), resultTypeInfo)
            ?? throw new CodexRpcException("Codex returned an empty JSON-RPC result.");
    }

    private async Task<JsonElement> RequestResultAsync(string method, JsonObject? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var id = _nextId++;
        var payload = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? new JsonObject()
        };

        await SendAsync(payload, cancellationToken).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (true)
            {
                var line = await _transport.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false)
                    ?? throw new CodexRpcException("Codex app-server closed stdout.");
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var message = JsonDocument.Parse(line);
                var root = message.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new CodexRpcException("Codex returned invalid JSON-RPC data.");
                }

                if (!root.TryGetProperty("id", out var messageIdNode))
                {
                    continue;
                }

                if (!messageIdNode.TryGetInt32(out var messageId) || messageId != id)
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var errorNode) && errorNode.ValueKind == JsonValueKind.Object)
                {
                    var messageText = errorNode.TryGetProperty("message", out var messageTextNode) && messageTextNode.ValueKind == JsonValueKind.String
                        ? messageTextNode.GetString()
                        : null;
                    throw new CodexRpcException(messageText ?? "Codex RPC request failed.");
                }

                if (!root.TryGetProperty("result", out var resultNode))
                {
                    throw new CodexRpcException("Codex returned a JSON-RPC response without result.");
                }

                return resultNode.Clone();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _transport.Kill();
            throw new CodexRpcTimeoutException(method);
        }
        catch (JsonException error)
        {
            throw new CodexRpcException($"Codex returned malformed JSON: {error.Message}");
        }
    }

    private async Task SendAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        var line = payload.ToJsonString(JsonOptions);
        await _transport.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}

