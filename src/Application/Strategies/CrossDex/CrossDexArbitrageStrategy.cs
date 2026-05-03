using Accipiter.Application.Orchestration;
using Accipiter.Core.Domain.Enums;
using Accipiter.Core.Domain.Interfaces;
using Accipiter.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Application.Strategies.CrossDex
{
    /// <summary>
    /// Cross-DEX arbitrage: buy the same token pair on the cheapest DEX,
    /// sell on the most expensive DEX. Two swap legs total.
    /// </summary>
    public sealed class CrossDexArbitrageStrategy : IArbitrageStrategy
    {
        public StrategyType StrategyType => StrategyType.CrossDex;

        private readonly IDexPriceAggregator _aggregator;
        private readonly OrchestratorOptions _options;
        private readonly ILogger<CrossDexArbitrageStrategy> _logger;

        // DEX program IDs (Solana mainnet)
        private static readonly Dictionary<string, string> DexProgramIds = new()
        {
            ["Raydium"] = "675kPX9MHTjS2zt1qfr1NYHuzeLXfQM9H24wFSUt1Mp8",
            ["Orca"] = "whirLbMiicVdio4qvUfM5KAg6Ct8VwpYzGff3uctyCc",
            ["Jupiter"] = "JUP6LkbZbjS1jKKwapdHNy74zcZ3tLUZoi5QNyVTaV4",
            ["Phoenix"] = "PhoeNiXZ8ByJGLkxNfZRnkUfjvmuYqLR89jjFHGqdXY",
            ["Lifinity"] = "EewxydAPCCVuNEyrVN68PuSYdQ7wKn27V9Gjeoi8dy3S"
        };

        public CrossDexArbitrageStrategy(
            IDexPriceAggregator aggregator,
            IOptions<OrchestratorOptions> options,
            ILogger<CrossDexArbitrageStrategy> logger)
        {
            _aggregator = aggregator;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<ArbitrageOpportunity>> ScanAsync(
            IReadOnlyList<TokenPair> pairs,
            CancellationToken ct = default)
        {
            var tradeAmount = _options.TradeAmountUSDC;
            var quotes = await _aggregator.GetQuotesAsync(pairs, tradeAmount, ct);

            var opportunities = new List<ArbitrageOpportunity>();

            // Group quotes by pair, then find best buy/sell spread across DEXes
            foreach (var pairGroup in quotes.GroupBy(q => q.Pair))
            {
                var validQuotes = pairGroup
                    .Where(q => !q.IsStale())
                    .OrderByDescending(q => q.OutputAmount)
                    .ToList();

                if (validQuotes.Count < 2) continue;

                var bestSell = validQuotes[0];   // highest output = sell here
                var bestBuy = validQuotes[^1];  // lowest output = buy here (most tokens per USDC)

                if (bestSell.Dex == bestBuy.Dex) continue;

                var spreadUSDC = bestSell.OutputAmount - bestBuy.OutputAmount;

                if (spreadUSDC <= 0) continue;

                var route = await BuildRouteAsync(
                    buyDex: bestBuy.Dex,
                    sellDex: bestSell.Dex,
                    pair: pairGroup.Key,
                    tradeAmount: tradeAmount);

                opportunities.Add(new ArbitrageOpportunity
                {
                    InputToken = pairGroup.Key.QuoteToken,
                    OutputToken = pairGroup.Key.QuoteToken,
                    InputAmountUSDC = tradeAmount,
                    EstimatedOutputAmountUSDC = tradeAmount + spreadUSDC,
                    BuyDex = bestBuy.Dex,
                    SellDex = bestSell.Dex,
                    Route = route,
                    StrategyType = StrategyType.CrossDex
                });
            }

            _logger.LogDebug("CrossDex scan complete — {Count} raw opportunities found", opportunities.Count);
            return opportunities;
        }

        public Task<TradeRoute> BuildRouteAsync(
            ArbitrageOpportunity opportunity,
            CancellationToken ct = default)
            => BuildRouteAsync(opportunity.BuyDex, opportunity.SellDex,
                new TokenPair(opportunity.InputToken, opportunity.OutputToken),
                opportunity.InputAmountUSDC);

        private Task<TradeRoute> BuildRouteAsync(
            string buyDex, string sellDex, TokenPair pair, decimal tradeAmount)
        {
            // Estimated fees: ~0.3% per swap leg on most AMMs
            const decimal feeRatePerLeg = 0.003m;
            var totalFees = tradeAmount * feeRatePerLeg * 2;

            var route = new TradeRoute
            {
                TotalFeeEstimateUSDC = totalFees,
                EstimatedSlippageBps = _options.SlippageToleranceBps,
                Steps =
                [
                    new TradeStep
                {
                    Order = 1,
                    Dex = buyDex,
                    FromToken = pair.QuoteToken,   // USDC in
                    ToToken = pair.BaseToken,       // e.g. SOL out
                    InAmount = tradeAmount,
                    EstimatedOutAmount = tradeAmount, // set by aggregator — placeholder here
                    ProgramId = DexProgramIds.GetValueOrDefault(buyDex, "unknown")
                },
                new TradeStep
                {
                    Order = 2,
                    Dex = sellDex,
                    FromToken = pair.BaseToken,     // SOL in
                    ToToken = pair.QuoteToken,      // USDC out
                    InAmount = tradeAmount,
                    EstimatedOutAmount = tradeAmount,
                    ProgramId = DexProgramIds.GetValueOrDefault(sellDex, "unknown")
                }
                ]
            };

            return Task.FromResult(route);
        }
    }

}
