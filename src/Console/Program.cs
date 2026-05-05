using Accipiter.Application.Orchestration;
using Accipiter.Application.Simulation;
using Accipiter.Application.Strategies.CrossDex;
using Accipiter.Core.Domain.Enums;
using Accipiter.Core.Domain.Interfaces;
using Accipiter.Infrastructure.Persistence;
using Accipiter.Infrastructure.SmartContracts;
using Accipiter.Infrastructure.Solana.DEX;
using Accipiter.Infrastructure.Solana.RPC;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Accipiter.Infrastructure.Persistence.Repoistories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Accipiter.Infrastructure.Persistence.Repositories;

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

            // Repositories
            services.AddScoped<IOpportunityRepository, OpportunityRepository>();
            services.AddScoped<ISimulationRunRepository, SimulationRunRepository>();

            // Strategy routing — reads "Strategy:Active" from config
            var activeStrategy = config["Strategy:Active"] ?? "CrossDex";
            services.AddScoped<IArbitrageStrategy>(_ =>
                activeStrategy switch
                {
                    "Triangular" => throw new NotImplementedException(
                        "TriangularArbitrageStrategy is not yet implemented."),
                    _ => ActivatorUtilities.CreateInstance<CrossDexArbitrageStrategy>(_)
                });

            // Core services
            services.AddHttpClient<IDexPriceAggregator, JupiterDexAggregator>();
            services.AddScoped<ISolanaRpcClient, SolanaRpcClient>();
            services.AddScoped<SimulationEngine>();
            services.AddScoped<OpportunityScorer>();
            services.AddScoped<ArbitrageOrchestrator>();

            // SmartContractClient is only registered in live mode
            var mode = Enum.Parse<ExecutionMode>(
                config["Strategy:Mode"] ?? "Simulation", ignoreCase: true);
            if (mode == ExecutionMode.Live)
            {
                services.AddScoped<ISmartContractClient, SmartContractClient>();
            }

            // Background polling worker
            services.AddHostedService<ArbitrageWorker>();
        })
        .Build();

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
public sealed class ArbitrageWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly ILogger<ArbitrageWorker> _logger;

    public ArbitrageWorker(
        IServiceProvider services,
        IConfiguration config,
        ILogger<ArbitrageWorker> logger)
    {
        _services = services;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMs = _config.GetValue<int>("Strategy:PollingIntervalMs", 2000);
        _logger.LogInformation("Arbitrage worker started — polling every {Interval}ms", intervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var orchestrator = scope.ServiceProvider
                    .GetRequiredService<ArbitrageOrchestrator>();

                await orchestrator.RunTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during arbitrage tick — continuing");
            }

            await Task.Delay(intervalMs, stoppingToken);
        }

        _logger.LogInformation("Arbitrage worker stopped");
    }
}
