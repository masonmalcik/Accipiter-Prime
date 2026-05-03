using Accipiter.Core.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Models
{
    public sealed class ArbitrageOpportunity
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.UtcNow;

        public required string InputToken { get; init; }   // e.g. "USDC"
        public required string OutputToken { get; init; }  // e.g. "USDC" (same for round-trip arb)

        public decimal InputAmountUSDC { get; init; }
        public decimal EstimatedOutputAmountUSDC { get; init; }
        public decimal EstimatedProfitUSDC => EstimatedOutputAmountUSDC - InputAmountUSDC;

        public required string BuyDex { get; init; }
        public required string SellDex { get; init; }

        public required TradeRoute Route { get; init; }

        public double ConfidenceScore { get; set; }  // 0-1, set by OpportunityScorer
        public OpportunityStatus Status { get; set; } = OpportunityStatus.Discovered;

        public StrategyType StrategyType { get; init; }
    }
}
