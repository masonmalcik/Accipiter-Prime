using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence
{
    public sealed class OpportunityLog
    {
        public Guid Id { get; set; }
        public DateTimeOffset DiscoveredAt { get; set; }
        public string StrategyType { get; set; } = default!;
        public string InputToken { get; set; } = default!;
        public string OutputToken { get; set; } = default!;
        public decimal InputAmountUSDC { get; set; }
        public decimal EstimatedOutputAmountUSDC { get; set; }
        public decimal EstimatedProfitUSDC { get; set; }
        public string BuyDex { get; set; } = default!;
        public string SellDex { get; set; } = default!;
        public string RouteJson { get; set; } = default!;
        public string Status { get; set; } = default!;
        public double ConfidenceScore { get; set; }
    }
}
