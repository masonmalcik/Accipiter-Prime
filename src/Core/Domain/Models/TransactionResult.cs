using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Models
{
    public sealed class TransactionResult
    {
        public required string Signature { get; init; }
        public bool Confirmed { get; init; }
        public bool Reverted { get; init; }
        public string? ErrorMessage { get; init; }
        public decimal? ActualOutputAmountUSDC { get; init; }
    }
}
