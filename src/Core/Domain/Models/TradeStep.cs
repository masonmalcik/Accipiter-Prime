using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Models
{
    public sealed class TradeStep
    {
        public int Order { get; init; }
        public required string Dex { get; init; }
        public required string FromToken { get; init; }
        public required string ToToken { get; init; }
        public decimal InAmount { get; init; }
        public decimal EstimatedOutAmount { get; init; }
        public required string ProgramId { get; init; }  // On-chain DEX program address
    }

}
