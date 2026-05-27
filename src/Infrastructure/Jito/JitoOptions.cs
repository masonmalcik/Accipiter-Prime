namespace Accipiter.Infrastructure.Jito;

/// <summary>
/// Configuration options for Jito bundle submission.
/// Bound from the "Jito" section of appsettings.json.
/// </summary>
public sealed class JitoOptions
{
    /// <summary>
    /// Jito block engine endpoint. Use the one geographically
    /// closest to your server for lowest latency.
    ///
    /// Available endpoints:
    ///   NY:          https://ny.mainnet.block-engine.jito.wtf
    ///   Amsterdam:   https://amsterdam.mainnet.block-engine.jito.wtf
    ///   Frankfurt:   https://frankfurt.mainnet.block-engine.jito.wtf
    ///   Tokyo:       https://tokyo.mainnet.block-engine.jito.wtf
    ///   Salt Lake:   https://slc.mainnet.block-engine.jito.wtf
    /// </summary>
    public string BlockEngineUrl { get; set; } =
        "https://slc.mainnet.block-engine.jito.wtf";

    /// <summary>
    /// Tip amount in lamports paid to the Jito tip account.
    /// 1_000_000 lamports = 0.001 SOL
    /// Increase during high congestion to improve bundle landing rate.
    /// </summary>
    public long TipLamports { get; set; } = 1_000_000;

    /// <summary>
    /// Jito tip accounts — one is selected randomly per bundle submission.
    /// These are the official Jito tip accounts as of 2025.
    /// </summary>
    public List<string> TipAccounts { get; set; } =
    [
        "96gYZGLnJYVFmbjzopPSU6QiEV5fGqZNyN9nmNhvrZU5",
        "HFqU5x63VTqvQss8hp11i4wVV8bD44PvwucfZ2bU7gRe",
        "Cw8CFyM9FkoMi7K7Crf6HNQqf4uEMzpKw6QNghXLvLkY",
        "ADaUMid9yfUytqMBgopwjb2DTLSokTSzL1uw6nqDevaJ",
        "DfXygSm4jCyNCybVYYK6DwvWqjKee8pbDmJGcLWNDXjh",
        "ADuUkR4vqLUMWXxW9gh6D6L8pMSawimctcNZ5pGwDcEt",
        "DttWaMuVvTiduZRnguLF7jNxTgiMBZ1hyAumKUiL2KRL",
        "3AVi9Tg9Uo68tJfuvoKvqKNWKkC5wPdSSdeBnizKZ6jT"
    ];

    /// <summary>
    /// Maximum time in milliseconds to wait for bundle confirmation.
    /// Bundles not confirmed within this window are considered dropped.
    /// </summary>
    public int ConfirmationTimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// Whether to simulate the bundle before submitting.
    /// Useful during testing — set to true on devnet.
    /// </summary>
    public bool SimulateBeforeSubmit { get; set; } = true;
}

