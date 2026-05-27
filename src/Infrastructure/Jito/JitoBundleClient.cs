using Accipiter.Core.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Accipiter.Infrastructure.Jito;

/// <summary>
/// Submits arbitrage transactions as Jito bundles for MEV protection.
///
/// A bundle is 2 transactions submitted atomically:
///   Tx 1: The arbitrage swap (your Rust program)
///   Tx 2: The Jito tip transfer to a tip account
///
/// If Tx 1 fails (revert guard triggers), the entire bundle is dropped
/// and you pay nothing — not even the tip.
/// </summary>
public sealed class JitoBundleClient
{
    private readonly HttpClient _http;
    private readonly JitoOptions _options;
    private readonly ILogger<JitoBundleClient> _logger;
    private readonly Random _random = new();

    public JitoBundleClient(
        HttpClient http,
        IConfiguration config,
        ILogger<JitoBundleClient> logger)
    {
        _http = http;
        _logger = logger;
        _options = config.GetSection("Jito").Get<JitoOptions>()
            ?? throw new InvalidOperationException("Jito config section is missing.");
    }

    /// <summary>
    /// Submits a signed arbitrage transaction as a Jito bundle.
    /// Returns the bundle ID if accepted by the block engine.
    /// </summary>
    public async Task<JitoBundleResult> SubmitBundleAsync(
        byte[] signedArbTransaction,
        byte[] signedTipTransaction,
        CancellationToken ct = default)
    {
        var bundleUrl = $"{_options.BlockEngineUrl}/api/v1/bundles";

        var arbTxBase64 = Convert.ToBase64String(signedArbTransaction);
        var tipTxBase64 = Convert.ToBase64String(signedTipTransaction);

        var request = new JitoJsonRpcRequest
        {
            Method = "sendBundle",
            Params = [[arbTxBase64, tipTxBase64]],
            Id = Guid.NewGuid().ToString(),
            JsonRpc = "2.0"
        };

        _logger.LogInformation(
            "Submitting Jito bundle to {Url} | tip: {Tip} lamports",
            bundleUrl, _options.TipLamports);

        try
        {
            var response = await _http.PostAsJsonAsync(bundleUrl, request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<JitoJsonRpcResponse>(cancellationToken: ct);

            if (result?.Error is not null)
            {
                _logger.LogError("Jito bundle rejected: {Error}", result.Error.Message);
                return new JitoBundleResult
                {
                    Accepted = false,
                    ErrorMessage = result.Error.Message
                };
            }

            _logger.LogInformation("Jito bundle accepted — id: {BundleId}", result?.Result);
            return new JitoBundleResult
            {
                Accepted = true,
                BundleId = result?.Result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jito bundle submission failed");
            return new JitoBundleResult
            {
                Accepted = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Returns a random Jito tip account from the configured list.
    /// Rotating tip accounts avoids concentration on a single account.
    /// </summary>
    public string GetRandomTipAccount()
    {
        var accounts = _options.TipAccounts;
        return accounts[_random.Next(accounts.Count)];
    }

    /// <summary>
    /// Checks the status of a submitted bundle.
    /// </summary>
    public async Task<string?> GetBundleStatusAsync(
        string bundleId,
        CancellationToken ct = default)
    {
        var url = $"{_options.BlockEngineUrl}/api/v1/bundles";

        var request = new JitoJsonRpcRequest
        {
            Method = "getBundleStatuses",
            Params = [[bundleId]],
            Id = Guid.NewGuid().ToString(),
            JsonRpc = "2.0"
        };

        try
        {
            var response = await _http.PostAsJsonAsync(url, request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            return result.GetRawText();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get bundle status for {BundleId}", bundleId);
            return null;
        }
    }
}

// ============================================================
// JSON-RPC request/response models
// ============================================================

internal sealed class JitoJsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; init; } = "2.0";
    [JsonPropertyName("id")] public string Id { get; init; } = default!;
    [JsonPropertyName("method")] public string Method { get; init; } = default!;
    [JsonPropertyName("params")] public object[][] Params { get; init; } = default!;
}

internal sealed class JitoJsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string? JsonRpc { get; init; }
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("result")] public string? Result { get; init; }
    [JsonPropertyName("error")] public JitoRpcError? Error { get; init; }
}

internal sealed class JitoRpcError
{
    [JsonPropertyName("code")] public int Code { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = default!;
}

public sealed class JitoBundleResult
{
    public bool Accepted { get; init; }
    public string? BundleId { get; init; }
    public string? ErrorMessage { get; init; }
}

