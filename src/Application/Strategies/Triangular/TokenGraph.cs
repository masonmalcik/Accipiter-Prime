using Accipiter.Core.Domain.Models;

namespace Accipiter.Application.Strategies.Triangular;

/// <summary>
/// Directed weighted graph of token exchange rates.
/// Each edge A→B represents the exchange rate for swapping token A to token B.
/// Built fresh from live Jupiter quotes on every scan cycle.
/// </summary>
public sealed class TokenGraph
{
    // [fromToken][toToken] = EdgeData
    private readonly Dictionary<string, Dictionary<string, EdgeData>> _edges = new();

    public void AddEdge(string fromToken, string toToken, decimal rate,
        decimal liquidityUSDC, string dex, string ammKey)
    {
        if (!_edges.ContainsKey(fromToken))
            _edges[fromToken] = new Dictionary<string, EdgeData>();

        // Keep the best rate if multiple DEXes offer the same pair
        if (!_edges[fromToken].TryGetValue(toToken, out var existing) ||
            rate > existing.Rate)
        {
            _edges[fromToken][toToken] = new EdgeData
            {
                FromToken = fromToken,
                ToToken = toToken,
                Rate = rate,
                LiquidityUSDC = liquidityUSDC,
                Dex = dex,
                AmmKey = ammKey,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public IReadOnlyDictionary<string, EdgeData> GetEdgesFrom(string token)
        => _edges.TryGetValue(token, out var edges)
            ? edges
            : new Dictionary<string, EdgeData>();

    public IReadOnlyCollection<string> Tokens => _edges.Keys;

    public bool HasToken(string token) => _edges.ContainsKey(token);

    public int EdgeCount => _edges.Values.Sum(d => d.Count);
}

public sealed class EdgeData
{
    public required string FromToken { get; init; }
    public required string ToToken { get; init; }
    public decimal Rate { get; init; }          // output per 1 unit of input
    public decimal LiquidityUSDC { get; init; } // pool liquidity estimate
    public required string Dex { get; init; }
    public required string AmmKey { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsStale(int maxAgeMs = 5000) =>
        (DateTimeOffset.UtcNow - UpdatedAt).TotalMilliseconds > maxAgeMs;
}






