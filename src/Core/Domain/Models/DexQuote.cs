using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Models
{
    public sealed class DexQuote
    {
        public required string Dex { get; init; }
        public required TokenPair Pair { get; init; }
        public decimal InputAmount { get; init; }
        public decimal OutputAmount { get; init; }
        public decimal PriceImpactBps { get; init; }
        public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;
        public bool IsStale(int maxAgeMs = 3000) =>
            (DateTimeOffset.UtcNow - FetchedAt).TotalMilliseconds > maxAgeMs;
    }
}
