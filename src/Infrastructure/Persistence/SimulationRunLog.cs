using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence
{
    public sealed class SimulationRunLog
    {
        public Guid Id { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public int TotalOpportunitiesFound { get; set; }
        public decimal TotalProfitUSDC { get; set; }
        public decimal TotalLossUSDC { get; set; }
        public decimal NetPnLUSDC { get; set; }

        public ICollection<SimulationTradeLog> Trades { get; set; } = [];
    }
}
