using Accipiter.Core.Domain.Interfaces;
using Accipiter.Core.Domain.Models;
using Accipiter.Infrastructure.Jito;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Solnet.Rpc;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Models;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;
using System.Text;
using System.Text.Json;

namespace Accipiter.Infrastructure.SmartContract;

public sealed class SmartContractClient : ISmartContractClient
{
    private readonly ISolanaRpcClient _rpcClient;
    private readonly JitoBundleClient _jitoClient;
    private readonly IConfiguration _config;
    private readonly ILogger<SmartContractClient> _logger;

    // USDC mint address (mainnet)
    private const string UsdcMint = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";

    // Anchor instruction discriminator for execute_triangular_arbitrage
    // Generated from: sha256("global:execute_triangular_arbitrage")[..8]
    private static readonly byte[] InstructionDiscriminator =
    [
        0x4e, 0x3a, 0x9c, 0x2f, 0x1b, 0x8e, 0x7d, 0x06
    ];

    public SmartContractClient(
        ISolanaRpcClient rpcClient,
        JitoBundleClient jitoClient,
        IConfiguration config,
        ILogger<SmartContractClient> logger)
    {
        _rpcClient = rpcClient;
        _jitoClient = jitoClient;
        _config = config;
        _logger = logger;
    }

    public async Task<string> ExecuteArbitrageAsync(
        TradeRoute route,
        decimal minOutputAmountUSDC,
        CancellationToken ct = default)
    {
        var programId = _config["Solana:ProgramId"]
            ?? throw new InvalidOperationException("Solana:ProgramId not configured.");
        var keyPath = _config["Solana:WalletKeyPath"]
            ?? throw new InvalidOperationException("Solana:WalletKeyPath not configured.");

        _logger.LogInformation(
            "Building triangular arbitrage instruction | program: {ProgramId}",
            programId);

        // Load wallet keypair
        var wallet = LoadWallet(keyPath);
        var authority = wallet.Account;

        // Get wallet state for USDC token account
        var walletState = await _rpcClient.GetWalletStateAsync(ct);

        // Get USDC token account address
        var usdcTokenAccount = await GetAssociatedTokenAddress(
            authority.PublicKey, UsdcMint);

        // Build the params struct
        var arbParams = BuildParams(route, minOutputAmountUSDC);

        // Serialize instruction data
        var instructionData = SerializeInstructionData(arbParams);

        // Resolve all accounts needed for the three swap legs
        var (allAccounts, leg1Count, leg2Count, leg3Count) =
            await ResolveSwapAccounts(route, authority.PublicKey, ct);

        // Update params with correct account counts
        arbParams = arbParams with
        {
            Leg1AccountCount = (byte)leg1Count,
            Leg2AccountCount = (byte)leg2Count,
            Leg3AccountCount = (byte)leg3Count
        };

        // Re-serialize with updated counts
        instructionData = SerializeInstructionData(arbParams);

        // Get recent blockhash
        var blockhashResponse = await GetRecentBlockhash(ct);

        // Build the arbitrage transaction
        var arbTx = BuildArbitrageTransaction(
            programId,
            authority,
            usdcTokenAccount,
            route,
            allAccounts,
            instructionData,
            blockhashResponse);

        // Build the Jito tip transaction
        var tipAccount = _jitoClient.GetRandomTipAccount();
        var tipLamports = _config.GetValue<long>("Jito:TipLamports", 1_000_000);

        var tipTx = BuildTipTransaction(
            authority,
            tipAccount,
            (ulong)tipLamports,
            blockhashResponse);

        _logger.LogInformation(
            "Submitting Jito bundle | tip account: {Tip} | tip: {Lamports} lamports",
            tipAccount, tipLamports);

        // Submit as Jito bundle
        var bundleResult = await _jitoClient.SubmitBundleAsync(arbTx, tipTx, ct);

        if (!bundleResult.Accepted)
        {
            throw new InvalidOperationException(
                $"Jito bundle rejected: {bundleResult.ErrorMessage}");
        }

        _logger.LogInformation(
            "Jito bundle accepted | bundle id: {BundleId}",
            bundleResult.BundleId);

        return bundleResult.BundleId ?? "unknown";
    }

    // ============================================================
    // Private helpers
    // ============================================================

    private static Wallet LoadWallet(string keyPath)
    {
        var json = File.ReadAllText(keyPath);
        var bytes = JsonSerializer.Deserialize<byte[]>(json)
            ?? throw new InvalidOperationException("Invalid keypair file.");
        var account = new Account(bytes[..32], bytes[32..]);
        return new Wallet(account);
    }

    private TriangularArbParams BuildParams(
        TradeRoute route, decimal minOutputAmountUSDC)
    {
        var steps = route.Steps.OrderBy(s => s.Order).ToList();

        if (steps.Count != 3)
            throw new InvalidOperationException(
                "Triangular arbitrage requires exactly 3 trade steps.");

        // Convert USDC decimal to lamports (6 decimals)
        var inputLamports = (ulong)(steps[0].InAmount * 1_000_000m);
        var leg1MinOut = (ulong)(steps[0].EstimatedOutAmount * 0.995m * 1_000_000m);
        var leg2MinOut = (ulong)(steps[1].EstimatedOutAmount * 0.995m * 1_000_000m);
        var leg3MinOut = (ulong)(minOutputAmountUSDC * 1_000_000m);
        var minProfit = (ulong)(0.5m * 1_000_000m); // 0.5 USDC minimum

        return new TriangularArbParams
        {
            Leg1AmountIn = inputLamports,
            Leg1MinimumAmountOut = leg1MinOut,
            Leg1AccountCount = 0, // filled in after account resolution
            Leg1Dex = steps[0].Dex,
            Leg2AmountIn = (ulong)(steps[1].InAmount * GetTokenDecimals(steps[1].FromToken)),
            Leg2MinimumAmountOut = leg2MinOut,
            Leg2AccountCount = 0,
            Leg2Dex = steps[1].Dex,
            Leg3AmountIn = (ulong)(steps[2].InAmount * GetTokenDecimals(steps[2].FromToken)),
            Leg3MinimumAmountOut = leg3MinOut,
            Leg3AccountCount = 0,
            Leg3Dex = steps[2].Dex,
            TokenBSymbol = steps[0].ToToken,
            TokenCSymbol = steps[1].ToToken,
            MinimumProfitLamports = minProfit
        };
    }

    private static decimal GetTokenDecimals(string token) => token switch
    {
        "USDC" => 1_000_000m,       // 6 decimals
        "SOL" => 1_000_000_000m,   // 9 decimals
        "ETH" => 100_000_000m,     // 8 decimals
        "BONK" => 100_000m,         // 5 decimals
        "JTO" => 1_000_000_000m,   // 9 decimals
        "WIF" => 1_000_000m,       // 6 decimals
        _ => 1_000_000_000m    // default 9 decimals
    };

    private byte[] SerializeInstructionData(TriangularArbParams p)
    {
        // Borsh serialization matching the Rust struct layout
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Discriminator
        bw.Write(InstructionDiscriminator);

        // Leg 1
        bw.Write(p.Leg1AmountIn);
        bw.Write(p.Leg1MinimumAmountOut);
        bw.Write(p.Leg1AccountCount);
        WriteString(bw, p.Leg1Dex);

        // Leg 2
        bw.Write(p.Leg2AmountIn);
        bw.Write(p.Leg2MinimumAmountOut);
        bw.Write(p.Leg2AccountCount);
        WriteString(bw, p.Leg2Dex);

        // Leg 3
        bw.Write(p.Leg3AmountIn);
        bw.Write(p.Leg3MinimumAmountOut);
        bw.Write(p.Leg3AccountCount);
        WriteString(bw, p.Leg3Dex);

        // Token symbols
        WriteString(bw, p.TokenBSymbol);
        WriteString(bw, p.TokenCSymbol);

        // Minimum profit
        bw.Write(p.MinimumProfitLamports);

        return ms.ToArray();
    }

    private static void WriteString(BinaryWriter bw, string value)
    {
        // Borsh string format: u32 length prefix + UTF-8 bytes
        var bytes = Encoding.UTF8.GetBytes(value);
        bw.Write((uint)bytes.Length);
        bw.Write(bytes);
    }

    private async Task<(List<string> accounts, int leg1Count, int leg2Count, int leg3Count)>
        ResolveSwapAccounts(
            TradeRoute route,
            string authorityKey,
            CancellationToken ct)
    {
        // This method resolves the on-chain pool accounts for each swap leg.
        // In a full implementation, this queries Jupiter's /swap endpoint
        // to get the exact account list for each swap instruction.
        //
        // For now we return a structured placeholder that maintains the
        // correct account count per DEX type based on known requirements:
        //   Orca Whirlpool: 11 accounts
        //   Raydium CLMM:   9+ accounts
        //   Raydium AMM v4: 18 accounts
        //
        // TODO: Replace with Jupiter /swap API call to get exact accounts:
        //   POST https://api.jup.ag/swap/v1/swap
        //   { quoteResponse, userPublicKey, wrapAndUnwrapSol: false }
        //   → returns serialized transaction with exact accounts

        var steps = route.Steps.OrderBy(s => s.Order).ToList();
        var allAccounts = new List<string>();

        var leg1Count = GetAccountCountForDex(steps[0].Dex);
        var leg2Count = GetAccountCountForDex(steps[1].Dex);
        var leg3Count = GetAccountCountForDex(steps[2].Dex);

        _logger.LogInformation(
            "Account counts — leg1 ({Dex1}): {C1} | leg2 ({Dex2}): {C2} | leg3 ({Dex3}): {C3}",
            steps[0].Dex, leg1Count,
            steps[1].Dex, leg2Count,
            steps[2].Dex, leg3Count);

        return (allAccounts, leg1Count, leg2Count, leg3Count);
    }

    private static int GetAccountCountForDex(string dex) => dex switch
    {
        "Orca" => 11,
        "Raydium CLMM" => 9,
        "Raydium" => 18,
        "GoonFi V2" => 11, // Orca-compatible
        _ => 11  // default to Orca count
    };

    private async Task<string> GetAssociatedTokenAddress(
        string walletAddress, string mintAddress)
    {
        // Derive the associated token account address
        // In production use Solnet.Programs AssociatedTokenAccountProgram
        // For now return a placeholder — replace with:
        // AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(
        //     new PublicKey(walletAddress), new PublicKey(mintAddress))
        return walletAddress;
    }

    private async Task<string> GetRecentBlockhash(CancellationToken ct)
    {
        // Get recent blockhash from RPC
        // Replace with actual Solnet call:
        // var result = await _solnetRpcClient.GetLatestBlockHashAsync();
        // return result.Result.Value.Blockhash;
        return "placeholder_blockhash";
    }

    private byte[] BuildArbitrageTransaction(
        string programId,
        Account authority,
        string usdcTokenAccount,
        TradeRoute route,
        List<string> additionalAccounts,
        byte[] instructionData,
        string recentBlockhash)
    {
        var steps = route.Steps.OrderBy(s => s.Order).ToList();

        // Build account metas for the instruction
        var keys = new List<AccountMeta>
        {
            AccountMeta.Writable(new PublicKey(authority.PublicKey), true),
            AccountMeta.Writable(new PublicKey(usdcTokenAccount), false),
            AccountMeta.ReadOnly(new PublicKey(steps[0].ProgramId), false), // dex_program_a
            AccountMeta.ReadOnly(new PublicKey(steps[1].ProgramId), false), // dex_program_b
            AccountMeta.ReadOnly(new PublicKey(steps[2].ProgramId), false), // dex_program_c
            AccountMeta.ReadOnly(new PublicKey("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA"), false), // token_program
            AccountMeta.ReadOnly(new PublicKey("11111111111111111111111111111111"), false), // system_program
        };

        // Add remaining pool accounts
        foreach (var account in additionalAccounts)
            keys.Add(AccountMeta.Writable(new PublicKey(account), false));

        var tx = new TransactionBuilder()
            .SetRecentBlockHash(recentBlockhash)
            .SetFeePayer(new PublicKey(authority.PublicKey))
            .AddInstruction(new TransactionInstruction
            {
                ProgramId = new PublicKey(programId),
                Keys = keys,
                Data = instructionData
            })
            .Build(new List<Account> { authority });

        return tx;
    }

    private byte[] BuildTipTransaction(
        Account authority,
        string tipAccount,
        ulong tipLamports,
        string recentBlockhash)
    {
        // Build SOL transfer to Jito tip account
        //
        // TODO: Replace with full Solnet implementation:
        //
        // var tx = new TransactionBuilder()
        //     .SetRecentBlockHash(recentBlockhash)
        //     .SetFeePayer(authority.PublicKey)
        //     .AddInstruction(
        //         SystemProgram.Transfer(
        //             authority.PublicKey,
        //             new PublicKey(tipAccount),
        //             tipLamports))
        //     .Build(new List<Account> { authority });
        //
        // return tx;

        _logger.LogWarning(
            "BuildTipTransaction: using placeholder — " +
            "implement full Solnet transaction building before going live");

        return Array.Empty<byte>();
    }
}

// ============================================================
// Internal params record
// ============================================================

internal sealed record TriangularArbParams
{
    public ulong Leg1AmountIn { get; init; }
    public ulong Leg1MinimumAmountOut { get; init; }
    public byte Leg1AccountCount { get; init; }
    public string Leg1Dex { get; init; } = "";
    public ulong Leg2AmountIn { get; init; }
    public ulong Leg2MinimumAmountOut { get; init; }
    public byte Leg2AccountCount { get; init; }
    public string Leg2Dex { get; init; } = "";
    public ulong Leg3AmountIn { get; init; }
    public ulong Leg3MinimumAmountOut { get; init; }
    public byte Leg3AccountCount { get; init; }
    public string Leg3Dex { get; init; } = "";
    public string TokenBSymbol { get; init; } = "";
    public string TokenCSymbol { get; init; } = "";
    public ulong MinimumProfitLamports { get; init; }
}

// ============================================================
// Wallet wrapper
// ============================================================

internal sealed class Wallet
{
    public Account Account { get; }
    public Wallet(Account account) => Account = account;
}

