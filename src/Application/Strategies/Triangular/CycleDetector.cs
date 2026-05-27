using Accipiter.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Accipiter.Application.Strategies.Triangular;

/// <summary>
/// Finds all profitable 3-token cycles in a TokenGraph.
/// A cycle A→B→C→A is profitable when:
///   rate(A→B) × rate(B→C) × rate(C→A) × (1 - feePerLeg)^3 > 1.0
/// </summary>
public sealed class CycleDetector
{
    // Fee per swap leg — Orca Whirlpool / Raydium CLMM typically 0.01%-0.30%
    // Using 0.3% (30 bps) as conservative estimate
    private const decimal FeePerLeg = 0.003m;
    private const decimal FeeFactor = 1m - FeePerLeg;  // 0.997 per leg
    private const decimal ThreeLegFeeFactor =           // 0.991 after 3 legs
        FeeFactor * FeeFactor * FeeFactor;

    private const decimal MinLiquidityUSDC = 10_000m;  // ignore illiquid pools
    private const int MaxCyclesPerScan = 50;            // cap to avoid overload

    private readonly ILogger<CycleDetector> _logger;

    public CycleDetector(ILogger<CycleDetector> logger)
        => _logger = logger;

    /// <summary>
    /// Enumerate all profitable 3-cycles in the graph.
    /// Returns cycles ordered by estimated profit descending.
    /// </summary>
    public IReadOnlyList<TriangularCycle> FindProfitableCycles(
        TokenGraph graph,
        decimal inputAmountUSDC)
    {
        var cycles = new List<TriangularCycle>();
        var tokens = graph.Tokens.ToList();

        // Fix start token as USDC — all cycles begin and end in USDC
        // This ensures profit is always measured in the same unit
        const string startToken = "USDC";

        if (!graph.HasToken(startToken))
        {
            _logger.LogWarning("USDC not found in token graph — no cycles possible");
            return cycles;
        }

        // Enumerate: USDC → B → C → USDC
        var edgesFromUsdc = graph.GetEdgesFrom(startToken);

        foreach (var (tokenB, edgeAB) in edgesFromUsdc)
        {
            if (edgeAB.IsStale()) continue;
            if (edgeAB.LiquidityUSDC < MinLiquidityUSDC) continue;
            if (tokenB == startToken) continue;

            var edgesFromB = graph.GetEdgesFrom(tokenB);

            foreach (var (tokenC, edgeBC) in edgesFromB)
            {
                if (edgeBC.IsStale()) continue;
                if (edgeBC.LiquidityUSDC < MinLiquidityUSDC) continue;
                if (tokenC == startToken || tokenC == tokenB) continue;

                var edgesFromC = graph.GetEdgesFrom(tokenC);

                if (!edgesFromC.TryGetValue(startToken, out var edgeCA)) continue;
                if (edgeCA.IsStale()) continue;
                if (edgeCA.LiquidityUSDC < MinLiquidityUSDC) continue;

                // Calculate the gross rate product
                var grossRateProduct = edgeAB.Rate * edgeBC.Rate * edgeCA.Rate;

                // Apply three-leg fee factor
                var netRateProduct = grossRateProduct * ThreeLegFeeFactor;

                if (netRateProduct <= 1.0m) continue; // not profitable after fees

                // Calculate estimated profit in USDC
                var outputAmount = inputAmountUSDC * netRateProduct;
                var estimatedProfit = outputAmount - inputAmountUSDC;

                cycles.Add(new TriangularCycle
                {
                    TokenA = startToken,
                    TokenB = tokenB,
                    TokenC = tokenC,
                    EdgeAB = edgeAB,
                    EdgeBC = edgeBC,
                    EdgeCA = edgeCA,
                    GrossRateProduct = grossRateProduct,
                    NetRateProduct = netRateProduct,
                    InputAmountUSDC = inputAmountUSDC,
                    EstimatedOutputUSDC = outputAmount,
                    EstimatedProfitUSDC = estimatedProfit
                });

                if (cycles.Count >= MaxCyclesPerScan) break;
            }

            if (cycles.Count >= MaxCyclesPerScan) break;
        }

        var ranked = cycles
            .OrderByDescending(c => c.EstimatedProfitUSDC)
            .ToList();

        _logger.LogDebug("Cycle detection complete — {Count} profitable cycles found",
            ranked.Count);

        return ranked;
    }
}

public sealed class TriangularCycle
{
    public required string TokenA { get; init; }  // always USDC
    public required string TokenB { get; init; }  // intermediate token 1
    public required string TokenC { get; init; }  // intermediate token 2

    public required EdgeData EdgeAB { get; init; }
    public required EdgeData EdgeBC { get; init; }
    public required EdgeData EdgeCA { get; init; }

    public decimal GrossRateProduct { get; init; }
    public decimal NetRateProduct { get; init; }
    public decimal InputAmountUSDC { get; init; }
    public decimal EstimatedOutputUSDC { get; init; }
    public decimal EstimatedProfitUSDC { get; init; }

    public string Description =>
        $"{TokenA} → {TokenB} ({EdgeAB.Dex}) → {TokenC} ({EdgeBC.Dex}) → {TokenA} ({EdgeCA.Dex})";
}
