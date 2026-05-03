using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Models
{
    /// <summary>
    /// An ordered sequence of swap steps that form one arbitrage path.
    /// Cross-DEX arb = 2 steps. Triangular arb = 3 steps.
    /// </summary>
    public sealed class TradeRoute
    {
        public IReadOnlyList<TradeStep> Steps { get; init; } = [];
        public decimal TotalFeeEstimateUSDC { get; init; }
        public decimal EstimatedSlippageBps { get; init; }
    }
}
