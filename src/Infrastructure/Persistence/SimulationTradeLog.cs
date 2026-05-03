using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence
{
    public sealed class SimulationTradeLog
    {
        public Guid Id { get; set; }
        public Guid SimulationRunId { get; set; }
        public Guid OpportunityId { get; set; }
        public DateTimeOffset SimulatedAt { get; set; }
        public decimal InputAmountUSDC { get; set; }
        public decimal OutputAmountUSDC { get; set; }
        public decimal ProfitUSDC { get; set; }
        public bool WouldHaveReverted { get; set; }

        public SimulationRunLog SimulationRun { get; set; } = default!;
    }
}
