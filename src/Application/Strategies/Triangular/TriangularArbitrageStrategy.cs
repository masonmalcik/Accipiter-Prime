using Accipiter.Application.Orchestration;
using Accipiter.Core.Domain.Enums;
using Accipiter.Core.Domain.Interfaces;
using Accipiter.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accipiter.Application.Strategies.Triangular;

public sealed class TriangularArbitrageStrategy : IArbitrageStrategy
{
    public StrategyType StrategyType => StrategyType.Triangular;

    private readonly IDexPriceAggregator _aggregator;
    private readonly CycleDetector _cycleDetector;
    private readonly OrchestratorOptions _options;
    private readonly ILogger<TriangularArbitrageStrategy> _logger;

    private static readonly Dictionary<string, string> DexProgramIds = new()
    {
        ["Orca"] = "whirLbMiicVdio4qvUfM5KAg6Ct8VwpYzGff3uctyCc",
        ["Raydium"] = "675kPX9MHTjS2zt1qfr1NYHuzeLXfQM9H24wFSUt1Mp8",
        ["Raydium CLMM"] = "CAMMCzo5YL8w4VFF8KVHrK22GGUsp5VTaW7grrKgrWqK",
        ["Meteora DLMM"] = "LBUZKhRxPF3XUpBCjp4YzTKgLccjZhTSDM9YuVaPwxo",
        ["Jupiter"] = "JUP6LkbZbjS1jKKwapdHNy74zcZ3tLUZoi5QNyVTaV4",
        ["GoonFi V2"] = "GMCJvYGf5Ex2ARiMquaBDqU6iKM8uiEQkB8jCnoNfHpC"
    };

    public TriangularArbitrageStrategy(
        IDexPriceAggregator aggregator,
        CycleDetector cycleDetector,
        IOptions<OrchestratorOptions> options,
        ILogger<TriangularArbitrageStrategy> logger)
    {
        _aggregator = aggregator;
        _cycleDetector = cycleDetector;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ArbitrageOpportunity>> ScanAsync(
        IReadOnlyList<TokenPair> pairs,
        CancellationToken ct = default)
    {
        var tradeAmount = _options.TradeAmountUSDC;
        var quotes = await _aggregator.GetQuotesAsync(pairs, tradeAmount, ct);

        // Temporary diagnostic log
        _logger.LogInformation("ScanAsync: received {Count} quotes", quotes.Count);

        if (!quotes.Any())
        {
            _logger.LogWarning("No quotes received — skipping triangular scan");
            return [];
        }

        var graph = BuildGraph(quotes);

        // Temporary diagnostic log
        _logger.LogInformation("Graph built - {Tokens} tokens, {Edges} edges",
            graph.Tokens.Count, graph.EdgeCount);

        var cycles = _cycleDetector.FindProfitableCycles(graph, tradeAmount);

        // Temporary diagnostic log
        _logger.LogInformation("Cycles found: {Count}", cycles.Count);

        if (!cycles.Any())
        {
            _logger.LogDebug("No profitable triangular cycles found this tick");
            return [];
        }

        var opportunities = new List<ArbitrageOpportunity>();

        foreach (var cycle in cycles)
        {
            // Diagnostic log to verify profit values are correct
            _logger.LogInformation(
                "Opportunity: {Desc} | input: {In:F2} | output: {Out:F6} | profit: {Profit:F6}",
                cycle.Description,
                cycle.InputAmountUSDC,
                cycle.EstimatedOutputUSDC,
                cycle.EstimatedProfitUSDC);

            opportunities.Add(new ArbitrageOpportunity
            {
                InputToken = cycle.TokenA,
                OutputToken = cycle.TokenA,
                InputAmountUSDC = cycle.InputAmountUSDC,
                EstimatedOutputAmountUSDC = cycle.EstimatedOutputUSDC,
                BuyDex = cycle.EdgeAB.Dex,
                SellDex = cycle.EdgeCA.Dex,
                Route = BuildRoute(cycle),
                StrategyType = StrategyType.Triangular
            });
        }

        _logger.LogInformation(
            "Triangular scan complete - {Count} opportunities | best: {Best}",
            opportunities.Count,
            cycles.Count > 0 ? cycles[0].Description : "none");

        return opportunities;
    }

    public Task<TradeRoute> BuildRouteAsync(
        ArbitrageOpportunity opportunity,
        CancellationToken ct = default)
        => Task.FromResult(opportunity.Route);

    private TokenGraph BuildGraph(IReadOnlyList<DexQuote> quotes)
    {
        var graph = new TokenGraph();

        foreach (var quote in quotes)
        {
            if (quote.InputAmount <= 0 || quote.OutputAmount <= 0) continue;

            var rate = quote.OutputAmount / quote.InputAmount;

            // QuoteToken is what we're spending (input)
            // BaseToken is what we're receiving (output)
            // Edge goes FROM QuoteToken TO BaseToken
            graph.AddEdge(
                fromToken: quote.Pair.QuoteToken,  // spending USDC
                toToken: quote.Pair.BaseToken,   // receiving SOL
                rate: rate,
                liquidityUSDC: quote.InputAmount * 10,
                dex: quote.Dex,
                ammKey: quote.Dex);

            //_logger.LogInformation(
            //    "Edge added: {From} -> {To} @ {Rate} via {Dex}",
            //    quote.Pair.QuoteToken,
            //    quote.Pair.BaseToken,
            //    rate,
            //    quote.Dex);
        }

        _logger.LogInformation(
            "Graph tokens: {Tokens}",
            string.Join(", ", graph.Tokens));

        return graph;
    }

    private TradeRoute BuildRoute(TriangularCycle cycle)
    {
        //const decimal feeRatePerLeg = 0.003m;
        //var totalFees = cycle.InputAmountUSDC * feeRatePerLeg * 3;

        return new TradeRoute
        {
            TotalFeeEstimateUSDC = 0m,
            EstimatedSlippageBps = _options.SlippageToleranceBps,
            Steps =
            [
                new TradeStep
                {
                    Order              = 1,
                    Dex                = cycle.EdgeAB.Dex,
                    FromToken          = cycle.TokenA,
                    ToToken            = cycle.TokenB,
                    InAmount           = cycle.InputAmountUSDC,
                    EstimatedOutAmount = cycle.InputAmountUSDC * cycle.EdgeAB.Rate,
                    ProgramId          = DexProgramIds.GetValueOrDefault(
                                             cycle.EdgeAB.Dex, cycle.EdgeAB.AmmKey)
                },
                new TradeStep
                {
                    Order              = 2,
                    Dex                = cycle.EdgeBC.Dex,
                    FromToken          = cycle.TokenB,
                    ToToken            = cycle.TokenC,
                    InAmount           = cycle.InputAmountUSDC * cycle.EdgeAB.Rate,
                    EstimatedOutAmount = cycle.InputAmountUSDC * cycle.EdgeAB.Rate
                                         * cycle.EdgeBC.Rate,
                    ProgramId          = DexProgramIds.GetValueOrDefault(
                                             cycle.EdgeBC.Dex, cycle.EdgeBC.AmmKey)
                },
                new TradeStep
                {
                    Order              = 3,
                    Dex                = cycle.EdgeCA.Dex,
                    FromToken          = cycle.TokenC,
                    ToToken            = cycle.TokenA,
                    InAmount           = cycle.InputAmountUSDC * cycle.EdgeAB.Rate
                                         * cycle.EdgeBC.Rate,
                    EstimatedOutAmount = cycle.EstimatedOutputUSDC,
                    ProgramId          = DexProgramIds.GetValueOrDefault(
                                             cycle.EdgeCA.Dex, cycle.EdgeCA.AmmKey)
                }
            ]
        };
    }
}
