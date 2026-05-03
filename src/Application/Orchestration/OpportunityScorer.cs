using Accipiter.Core.Domain.Models;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Application.Orchestration
{
    /// <summary>
    /// Scores and ranks discovered opportunities by estimated net profit.
    /// Applies slippage and fee haircuts before ranking.
    /// </summary>
    public sealed class OpportunityScorer
    {
        private readonly OrchestratorOptions _options;

        public OpportunityScorer(IOptions<OrchestratorOptions> options)
            => _options = options.Value;

        public IReadOnlyList<ArbitrageOpportunity> Rank(
            IReadOnlyList<ArbitrageOpportunity> opportunities)
        {
            foreach (var o in opportunities)
                o.ConfidenceScore = ComputeConfidence(o);

            return opportunities
                .Where(o => o.EstimatedProfitUSDC > 0)
                .OrderByDescending(o => o.EstimatedProfitUSDC * (decimal)o.ConfidenceScore)
                .ToList();
        }

        private double ComputeConfidence(ArbitrageOpportunity o)
        {
            // Confidence degrades with:
            //   - High price impact (large slippage expected)
            //   - Wide spread that may not survive execution delay
            // Future: incorporate historical fill rate, liquidity depth, volatility

            var slippagePenalty = 1.0 - (double)(o.Route.EstimatedSlippageBps / 10_000m);
            var profitMargin = (double)(o.EstimatedProfitUSDC / o.InputAmountUSDC);
            var marginBoost = Math.Min(profitMargin * 10.0, 1.0); // caps at 1.0

            return Math.Clamp(slippagePenalty * marginBoost, 0.0, 1.0);
        }
    }
}
