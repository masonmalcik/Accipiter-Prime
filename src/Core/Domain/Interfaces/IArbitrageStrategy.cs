using Accipiter.Core.Domain.Enums;
using Accipiter.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Interfaces
{
    /// <summary>
    /// Pluggable arbitrage strategy. Add new strategies by implementing this interface
    /// and registering them in DI — no orchestration code changes required.
    /// </summary>
    public interface IArbitrageStrategy
    {
        StrategyType StrategyType { get; }

        /// <summary>Scan current market prices and return ranked opportunities.</summary>
        Task<IReadOnlyList<ArbitrageOpportunity>> ScanAsync(
            IReadOnlyList<TokenPair> pairs,
            CancellationToken ct = default);

        /// <summary>Build the concrete swap route for a given opportunity.</summary>
        Task<TradeRoute> BuildRouteAsync(
            ArbitrageOpportunity opportunity,
            CancellationToken ct = default);
    }
}
