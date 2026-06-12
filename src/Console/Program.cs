using Accipiter.Application.Orchestration;
using Accipiter.Application.Simulation;
using Accipiter.Application.Strategies.CrossDex;
using Accipiter.Application.Strategies.Triangular;
using Accipiter.Core.Domain.Enums;
using Accipiter.Core.Domain.Interfaces;
using Accipiter.Infrastructure.Jito;
using Accipiter.Infrastructure.Persistence;
using Accipiter.Infrastructure.Persistence.Repoistories;
using Accipiter.Infrastructure.Persistence.Repositories;
using Accipiter.Infrastructure.Solana.DEX;
using Accipiter.Infrastructure.Solana.RPC;
using Accipiter.Infrastructure.Yellowstone;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Accipiter.Infrastructure.SmartContract;
using Solnet.Wallet;
using Solnet.Wallet.Utilities;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog((ctx, services, cfg) =>
            cfg.ReadFrom.Configuration(ctx.Configuration))
        .ConfigureServices((ctx, services) =>
        {
            var config = ctx.Configuration;

            services.AddSingleton<YellowstoneGrpcClient>();
            services.AddSingleton<YellowstoneStreamHandler>();
            services.AddSingleton<YellowstoneArbitrageWorker>();
            services.AddSingleton<CircuitBreaker>();
            

            // Jito bundle client
            services.AddHttpClient<JitoBundleClient>();
            services.Configure<JitoOptions>(config.GetSection("Jito"));

            // EF Core — SQL Server
            services.AddDbContext<AccipitersDbContext>((serviceProvider, opt) =>
            {
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                var connectionString = configuration.GetConnectionString("Default");

                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException(
                        "Connection string 'Default' is missing from appsettings.json.");

                opt.UseSqlServer(connectionString);
            });

            // Options
            services.Configure<OrchestratorOptions>(config.GetSection("Strategy"));
            services.Configure<CircuitBreakerOptions>(config.GetSection("CircuitBreaker"));

            // Repositories
            services.AddScoped<IOpportunityRepository, OpportunityRepository>();
            services.AddScoped<ISimulationRunRepository, SimulationRunRepository>();

            // Strategy routing — reads "Strategy:Active" from config
            var activeStrategy = config["Strategy:Active"] ?? "Triangular";
            services.AddScoped<IArbitrageStrategy>(provider =>
                activeStrategy switch
                {
                    "Triangular" => ActivatorUtilities.CreateInstance<TriangularArbitrageStrategy>(provider),
                    "CrossDex" => ActivatorUtilities.CreateInstance<CrossDexArbitrageStrategy>(provider),
                    _ => throw new InvalidOperationException($"Unknown strategy: {activeStrategy}")
                });

            // Core services
            services.AddHttpClient<IDexPriceAggregator, JupiterDexAggregator>();
            services.AddScoped<ISolanaRpcClient, SolanaRpcClient>();
            services.AddScoped<SimulationEngine>();
            services.AddScoped<OpportunityScorer>();
            services.AddScoped<ArbitrageOrchestrator>();
            services.AddScoped<CycleDetector>();

            // SmartContractClient is only registered in live mode
            var mode = Enum.Parse<ExecutionMode>(
                config["Strategy:Mode"] ?? "Simulation", ignoreCase: true);
            if (mode == ExecutionMode.Live)
            {
                services.AddScoped<ISmartContractClient, SmartContractClient>();
            }

            // Background polling worker
            services.AddHostedService<YellowstoneArbitrageWorker>();
        })
        .Build();

    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AccipitersDbContext>();
        await db.Database.MigrateAsync();

        // Reset circuit breaker on startup
        var circuitBreaker = scope.ServiceProvider.GetRequiredService<CircuitBreaker>();
        circuitBreaker.Reset();

        Log.Information("Database ready");
    }

    // Ensure DB is created and migrations are applied
    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AccipitersDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database ready");
    }

    Log.Information("Accipiter starting...");
    await host.RunAsync();
}
catch (Exception ex)
{
    //************************************************************************
    Log.Fatal(ex, "Accipiter terminated unexpectedly");
    Console.WriteLine("=== FULL EXCEPTION ===");
    Console.WriteLine(ex.ToString());
    if (ex.InnerException != null)
    {
        Console.WriteLine("=== INNER EXCEPTION ===");
        Console.WriteLine(ex.InnerException.ToString());
    }
    Console.ReadKey(); // pause so you can read the error
    //************************************************************************

    Log.Fatal(ex, "Accipiter terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

// ============================================================
// Background worker — drives the polling loop
// ============================================================
public sealed class YellowstoneArbitrageWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly YellowstoneGrpcClient _yellowstone;
    private readonly IConfiguration _config;
    private readonly ILogger<YellowstoneArbitrageWorker> _logger;

    // Debounce — don't process the same slot twice
    private ulong _lastProcessedSlot = 0;

    public YellowstoneArbitrageWorker(
        IServiceProvider services,
        YellowstoneGrpcClient yellowstone,
        IConfiguration config,
        ILogger<YellowstoneArbitrageWorker> logger)
    {
        _services = services;
        _yellowstone = yellowstone;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Yellowstone arbitrage worker started");

        // Subscribe to pool account updates
        _yellowstone.OnPoolUpdate += async update =>
        {
            // Debounce — skip if we already processed this slot
            if (update.Slot > 0 && update.Slot <= _lastProcessedSlot) return;
            _lastProcessedSlot = update.Slot;

            try
            {
                using var scope = _services.CreateScope();
                var orchestrator = scope.ServiceProvider
                    .GetRequiredService<ArbitrageOrchestrator>();

                await orchestrator.RunTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pool update from slot {Slot}",
                    update.Slot);
            }
        };

        // Start the gRPC stream — this blocks until cancelled
        await _yellowstone.StartStreamingAsync(stoppingToken);

        _logger.LogInformation("Yellowstone arbitrage worker stopped");
    }
}
