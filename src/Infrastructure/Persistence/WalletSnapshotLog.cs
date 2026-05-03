using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence
{
    public sealed class WalletSnapshotLog
    {
        public Guid Id { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public decimal UsdcBalance { get; set; }
        public decimal SolBalance { get; set; }
        public decimal TotalValueUSDC { get; set; }
    }
}
