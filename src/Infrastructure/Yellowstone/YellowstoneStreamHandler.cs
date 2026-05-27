using Microsoft.Extensions.Logging;

namespace Accipiter.Infrastructure.Yellowstone;

/// <summary>
/// Sits between the raw YellowstoneGrpcClient stream and the rest of the
/// application. Responsibilities:
///   - Filters incoming PoolAccountUpdates to only relevant DEX programs
///   - Deduplicates rapid-fire updates from the same pool in the same slot
///   - Tracks stream health and exposes a IsConnected property
///   - Routes updates to registered handlers
/// </summary>
public sealed class YellowstoneStreamHandler : IAsyncDisposable
{
    private readonly YellowstoneGrpcClient _grpcClient;
    private readonly ILogger<YellowstoneStreamHandler> _logger;

    // Dedupe cache — slot:account → last seen timestamp
    private readonly Dictionary<string, DateTimeOffset> _recentUpdates = new();
    private readonly TimeSpan _dedupeWindow = TimeSpan.FromMilliseconds(200);

    // Registered downstream handlers
    private readonly List<Func<PoolAccountUpdate, Task>> _handlers = [];

    public bool IsConnected { get; private set; }
    public DateTimeOffset? LastUpdateReceivedAt { get; private set; }
    public ulong LastSlot { get; private set; }

    // Known DEX program IDs — filter out unrelated account updates
    private static readonly HashSet<string> WatchedProgramIds =
    [
        "whirLbMiicVdio4qvUfM5KAg6Ct8VwpYzGff3uctyCc", // Orca Whirlpool
        "675kPX9MHTjS2zt1qfr1NYHuzeLXfQM9H24wFSUt1Mp8", // Raydium AMM v4
        "CAMMCzo5YL8w4VFF8KVHrK22GGUsp5VTaW7grrKgrWqK", // Raydium CLMM
        "LBUZKhRxPF3XUpBCjp4YzTKgLccjZhTSDM9YuVaPwxo"  // Meteora DLMM
    ];

    public YellowstoneStreamHandler(
        YellowstoneGrpcClient grpcClient,
        ILogger<YellowstoneStreamHandler> logger)
    {
        _grpcClient = grpcClient;
        _logger = logger;

        // Wire up to the raw client events
        _grpcClient.OnPoolUpdate += HandleRawUpdateAsync;
    }

    /// <summary>
    /// Register a handler to receive filtered, deduplicated pool updates.
    /// </summary>
    public void AddHandler(Func<PoolAccountUpdate, Task> handler)
        => _handlers.Add(handler);

    /// <summary>
    /// Start the underlying gRPC stream. Call once from the background worker.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("YellowstoneStreamHandler starting");
        IsConnected = true;

        try
        {
            await _grpcClient.StartStreamingAsync(ct);
        }
        finally
        {
            IsConnected = false;
            _logger.LogInformation("YellowstoneStreamHandler stopped");
        }
    }

    // ============================================================
    // Private helpers
    // ============================================================

    private async Task HandleRawUpdateAsync(PoolAccountUpdate update)
    {
        // Filter — only process updates from watched DEX programs
        if (!WatchedProgramIds.Contains(update.ProgramId) &&
            update.ProgramId != "stub")
        {
            return;
        }

        // Deduplicate — skip if we saw this account very recently
        var dedupeKey = $"{update.Slot}:{update.AccountAddress}";
        var now = DateTimeOffset.UtcNow;

        if (_recentUpdates.TryGetValue(dedupeKey, out var lastSeen) &&
            now - lastSeen < _dedupeWindow)
        {
            return;
        }

        _recentUpdates[dedupeKey] = now;

        // Clean up old dedupe entries to prevent memory growth
        if (_recentUpdates.Count > 1000)
        {
            var cutoff = now - TimeSpan.FromSeconds(5);
            var staleKeys = _recentUpdates
                .Where(kvp => kvp.Value < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
                _recentUpdates.Remove(key);
        }

        LastUpdateReceivedAt = now;
        LastSlot = update.Slot;

        _logger.LogDebug(
            "Pool update received — program: {Program} | slot: {Slot}",
            update.ProgramId, update.Slot);

        // Dispatch to all registered handlers concurrently
        var tasks = _handlers.Select(h => h(update));
        await Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
        => await _grpcClient.DisposeAsync();
}
