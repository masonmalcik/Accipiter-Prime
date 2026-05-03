using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Models
{
    public sealed class SimulationTradeResult
    {
        public Guid OpportunityId { get; init; }
        public DateTimeOffset SimulatedAt { get; init; }
        public decimal InputAmountUSDC { get; init; }
        public decimal AdjustedOutputAmountUSDC { get; init; }
        public decimal NetProfitUSDC { get; init; }
        public bool WouldHaveReverted { get; init; }
    }
}
