using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Accipiter.Infrastructure.Yellowstone;

/// <summary>
/// Connects to the Yellowstone gRPC endpoint (Helius) and streams
/// DEX pool account updates in real time.
///
/// When a pool account changes on-chain (a swap occurred, liquidity added/removed),
/// this client fires the OnPoolUpdate event. The TriangularArbitrageStrategy
/// listens to this event and re-evaluates cycles immediately rather than
/// waiting for the next polling interval.
/// </summary>
public sealed class YellowstoneGrpcClient : IAsyncDisposable
{
    private readonly string _grpcEndpoint;
    private readonly string _apiKey;
    private readonly int _pingIntervalSeconds;
    private readonly ILogger<YellowstoneGrpcClient> _logger;

    private GrpcChannel? _channel;
    private CancellationTokenSource? _cts;

    // Fired when a subscribed pool account is updated on-chain
    public event Func<PoolAccountUpdate, Task>? OnPoolUpdate;

    // Known DEX pool program IDs to subscribe to
    private static readonly List<string> WatchedPrograms =
    [
        "whirLbMiicVdio4qvUfM5KAg6Ct8VwpYzGff3uctyCc", // Orca Whirlpool
        "675kPX9MHTjS2zt1qfr1NYHuzeLXfQM9H24wFSUt1Mp8", // Raydium AMM v4
        "CAMMCzo5YL8w4VFF8KVHrK22GGUsp5VTaW7grrKgrWqK", // Raydium CLMM
        "LBUZKhRxPF3XUpBCjp4YzTKgLccjZhTSDM9YuVaPwxo"  // Meteora DLMM
    ];

    public YellowstoneGrpcClient(
        IConfiguration config,
        ILogger<YellowstoneGrpcClient> logger)
    {
        _logger = logger;

        _grpcEndpoint = config["Yellowstone:GrpcEndpoint"]
            ?? throw new InvalidOperationException("Yellowstone:GrpcEndpoint is not configured.");

        _apiKey = config["Yellowstone:ApiKey"]
            ?? throw new InvalidOperationException("Yellowstone:ApiKey is not configured.");

        _pingIntervalSeconds = config.GetValue<int>("Yellowstone:PingIntervalSeconds", 10);
    }

    /// <summary>
    /// Opens the gRPC stream and begins receiving account updates.
    /// Call this once from the background worker on startup.
    /// </summary>
    public async Task StartStreamingAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var credentials = CallCredentials.FromInterceptor((context, metadata) =>
        {
            metadata.Add("x-token", _apiKey);
            return Task.CompletedTask;
        });

        _channel = GrpcChannel.ForAddress(_grpcEndpoint, new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Create(
                new SslCredentials(), credentials)
        });

        _logger.LogInformation("Yellowstone gRPC connecting to {Endpoint}", _grpcEndpoint);

        // Keep reconnecting if the stream drops
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await RunStreamAsync(_cts.Token);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                _logger.LogInformation("Yellowstone stream cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Yellowstone stream disconnected — reconnecting in 5s");
                await Task.Delay(5000, _cts.Token);
            }
        }
    }

    private async Task RunStreamAsync(CancellationToken ct)
    {
        // NOTE: The actual Yellowstone protobuf-generated client code requires
        // the .proto files from https://github.com/rpcpool/yellowstone-grpc
        // to be added to the project and compiled via Grpc.Tools.
        //
        // Steps to complete this implementation:
        //
        // 1. Download the proto files:
        //    - geyser.proto
        //    - solana-storage.proto
        //    from https://github.com/rpcpool/yellowstone-grpc/tree/master/yellowstone-grpc-proto/proto
        //
        // 2. Add them to Accipiter.Infrastructure under /Protos/
        //
        // 3. Add to Accipiter.Infrastructure.csproj:
        //    <ItemGroup>
        //      <Protobuf Include="Protos\geyser.proto" GrpcServices="Client" />
        //      <Protobuf Include="Protos\solana-storage.proto" GrpcServices="None" />
        //    </ItemGroup>
        //
        // 4. Replace the stub below with the generated client:
        //
        //    var client = new Geyser.GeyserClient(_channel);
        //    var request = new SubscribeRequest
        //    {
        //        Accounts = {
        //            ["dex-pools"] = new SubscribeRequestFilterAccounts
        //            {
        //                Owner = { WatchedPrograms }
        //            }
        //        },
        //        Commitment = CommitmentLevel.Processed
        //    };
        //
        //    using var stream = client.Subscribe(
        //        headers: new Metadata { { "x-token", _apiKey } });
        //
        //    await stream.RequestStream.WriteAsync(request, ct);
        //
        //    await foreach (var update in stream.ResponseStream.ReadAllAsync(ct))
        //    {
        //        if (update.Account is null) continue;
        //        var poolUpdate = new PoolAccountUpdate
        //        {
        //            AccountAddress = update.Account.Account_.Pubkey.ToBase58(),
        //            ProgramId      = update.Account.Account_.Owner.ToBase58(),
        //            Slot           = update.Account.Slot,
        //            UpdatedAt      = DateTimeOffset.UtcNow
        //        };
        //        if (OnPoolUpdate is not null)
        //            await OnPoolUpdate(poolUpdate);
        //    }

        // ---- TEMPORARY STUB ----
        // Until the proto files are added, this simulates a stream tick
        // at the configured ping interval so the rest of the pipeline works.
        _logger.LogWarning(
            "Yellowstone running in STUB mode — add proto files to enable real streaming. " +
            "See comments in YellowstoneGrpcClient.RunStreamAsync for instructions.");

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_pingIntervalSeconds * 1000, ct);

            if (OnPoolUpdate is not null)
            {
                await OnPoolUpdate(new PoolAccountUpdate
                {
                    AccountAddress = "stub",
                    ProgramId = WatchedPrograms[0],
                    Slot = 0,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_channel is not null)
            await _channel.ShutdownAsync();
    }
}

public sealed class PoolAccountUpdate
{
    public required string AccountAddress { get; init; }
    public required string ProgramId { get; init; }
    public ulong Slot { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

