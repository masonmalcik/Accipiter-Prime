using Accipiter.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Interfaces
{
    // ============================================================
    // Accipiter.Core / Domain / Interfaces / ISolanaRpcClient.cs
    // ============================================================
    public interface ISolanaRpcClient
    {
        Task<WalletState> GetWalletStateAsync(CancellationToken ct = default);
        Task<string> SubmitTransactionAsync(byte[] signedTransaction, CancellationToken ct = default);
        Task<TransactionResult> ConfirmTransactionAsync(string signature, CancellationToken ct = default);
    }
}
