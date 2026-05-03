using Accipiter.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Interfaces
{
    // ============================================================
    // Accipiter.Core / Domain / Interfaces / IDexPriceAggregator.cs
    // ============================================================

    public interface IDexPriceAggregator
    {
        /// <summary>
        /// Returns best quotes for each configured DEX for every requested pair.
        /// </summary>
        Task<IReadOnlyList<DexQuote>> GetQuotesAsync(
            IReadOnlyList<TokenPair> pairs,
            decimal inputAmountUSDC,
            CancellationToken ct = default);
    }
}
