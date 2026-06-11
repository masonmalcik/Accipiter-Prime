using Accipiter.Core.Domain.Interfaces;
using Accipiter.Core.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Solana.RPC
{
    public sealed class SolanaRpcClient : ISolanaRpcClient
    {
        private readonly IRpcClient _client;
        private readonly string _walletAddress;
        private readonly ILogger<SolanaRpcClient> _logger;

        // Token mint addresses (Solana mainnet)
        private readonly string _usdcMint;

        public SolanaRpcClient(IConfiguration config, ILogger<SolanaRpcClient> logger)
        {
            var rpcUrl = config["Solana:RpcUrl"]
                ?? throw new InvalidOperationException("Solana:RpcUrl is not configured.");

            _walletAddress = config["Solana:WalletAddress"]
                ?? throw new InvalidOperationException("Solana:WalletAddress is not configured.");

            _usdcMint = config["Solana:UsdcMint"]
                ?? "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";

            _client = ClientFactory.GetClient(rpcUrl);
            _logger = logger;
        }

        public async Task<WalletState> GetWalletStateAsync(CancellationToken ct = default)
        {
            _logger.LogDebug("Fetching wallet state for {Address}", _walletAddress);

            // SOL balance
            var solResponse = await _client.GetBalanceAsync(_walletAddress);
            var solBalance = solResponse.Result?.Value is ulong lamports
                ? lamports / 1_000_000_000m
                : 0m;

            // USDC token account balance
            var tokenResponse = await _client.GetTokenAccountsByOwnerAsync(
                _walletAddress,
                tokenMintPubKey: _usdcMint);

            var usdcBalance = 0m;
            if (tokenResponse.Result?.Value is { Count: > 0 } accounts)
            {
                var raw = accounts[0].Account.Data.Parsed.Info.TokenAmount.AmountDecimal;
                usdcBalance = raw;
            }

            _logger.LogDebug("Wallet — SOL: {Sol}, USDC: {Usdc}", solBalance, usdcBalance);

            return new WalletState
            {
                SolBalance = solBalance,
                UsdcBalance = usdcBalance
            };
        }

        public async Task<string> SubmitTransactionAsync(
            byte[] signedTransaction,
            CancellationToken ct = default)
        {
            _logger.LogInformation("Submitting transaction ({Bytes} bytes)", signedTransaction.Length);

            var response = await _client.SendTransactionAsync(
                Convert.ToBase64String(signedTransaction));

            if (!response.WasSuccessful || response.Result is null)
            {
                var error = response.Reason ?? "Unknown RPC error";
                _logger.LogError("Transaction submission failed: {Error}", error);
                throw new InvalidOperationException($"Transaction submission failed: {error}");
            }

            _logger.LogInformation("Transaction submitted — sig: {Sig}", response.Result);
            return response.Result;
        }

        public async Task<TransactionResult> ConfirmTransactionAsync(
            string signature,
            CancellationToken ct = default)
        {
            _logger.LogDebug("Confirming transaction {Sig}", signature);

            // Poll until confirmed or timeout
            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            while (DateTimeOffset.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                var response = await _client.GetSignatureStatusesAsync(
                    new List<string> { signature });

                var status = response.Result?.Value?[0];
                if (status is not null)
                {
                    var reverted = status.Error is not null;
                    _logger.LogInformation(
                        "Transaction {Sig} — confirmed: true, reverted: {Reverted}",
                        signature, reverted);

                    return new TransactionResult
                    {
                        Signature = signature,
                        Confirmed = true,
                        Reverted = reverted,
                        ErrorMessage = reverted ? status.Error?.ToString() : null
                    };
                }

                await Task.Delay(1500, ct);
            }

            _logger.LogWarning("Transaction {Sig} confirmation timed out", signature);
            return new TransactionResult
            {
                Signature = signature,
                Confirmed = false,
                Reverted = false,
                ErrorMessage = "Confirmation timed out after 60 seconds"
            };
        }
    }
}
