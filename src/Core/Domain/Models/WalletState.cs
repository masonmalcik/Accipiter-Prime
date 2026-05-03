using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Models
{
    public sealed class WalletState
    {
        public decimal UsdcBalance { get; init; }
        public decimal SolBalance { get; init; }
        public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
    }
}
