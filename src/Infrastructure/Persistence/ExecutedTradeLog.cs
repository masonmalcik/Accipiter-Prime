using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence
{
    public sealed class ExecutedTradeLog
    {
        public Guid Id { get; set; }
        public Guid OpportunityId { get; set; }
        public string TxSignature { get; set; } = default!;
        public DateTimeOffset ExecutedAt { get; set; }
        public decimal InputAmountUSDC { get; set; }
        public decimal ActualOutputAmountUSDC { get; set; }
        public decimal ActualProfitUSDC { get; set; }
        public bool Reverted { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
