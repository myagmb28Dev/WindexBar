using System.Text.Json;
using System.Text.Json.Nodes;

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
        _ = await RequestAsync<JsonObject>(
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
        RequestAsync<RpcRateLimitsResponse>("account/rateLimits/read", null, _requestTimeout, cancellationToken);

    public Task<RpcAccountResponse> FetchAccountAsync(CancellationToken cancellationToken) =>
        RequestAsync<RpcAccountResponse>("account/read", null, _requestTimeout, cancellationToken);

    private async Task<T> RequestAsync<T>(string method, JsonObject? parameters, TimeSpan timeout, CancellationToken cancellationToken)
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

                var message = JsonNode.Parse(line)?.AsObject()
                    ?? throw new CodexRpcException("Codex returned invalid JSON-RPC data.");

                if (!message.TryGetPropertyValue("id", out var messageIdNode) || messageIdNode is null)
                {
                    continue;
                }

                if (messageIdNode.GetValue<int>() != id)
                {
                    continue;
                }

                if (message.TryGetPropertyValue("error", out var errorNode) && errorNode is JsonObject errorObject)
                {
                    var messageText = errorObject["message"]?.GetValue<string>() ?? "Codex RPC request failed.";
                    throw new CodexRpcException(messageText);
                }

                if (!message.TryGetPropertyValue("result", out var resultNode) || resultNode is null)
                {
                    throw new CodexRpcException("Codex returned a JSON-RPC response without result.");
                }

                return resultNode.Deserialize<T>(JsonOptions)
                    ?? throw new CodexRpcException("Codex returned an empty JSON-RPC result.");
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

