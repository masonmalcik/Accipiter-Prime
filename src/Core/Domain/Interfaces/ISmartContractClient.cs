using Accipiter.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Interfaces
{
    // ============================================================
    // Accipiter.Core / Domain / Interfaces / ISmartContractClient.cs
    // ============================================================
    public interface ISmartContractClient
    {
        Task<string> ExecuteArbitrageAsync(
            TradeRoute route,
            decimal minOutputAmountUSDC,
            CancellationToken ct = default);
    }
}
