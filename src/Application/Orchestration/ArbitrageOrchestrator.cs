using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Accipiter.Application.Simulation;
using Accipiter.Core.Domain.Enums;
using Accipiter.Core.Domain.Interfaces;
using Accipiter.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accipiter.Application.Orchestration
{
    // ============================================================
    // Accipiter.Application / Orchestration / ArbitrageOrchestrator.cs
    // ============================================================

    public sealed class ArbitrageOrchestrator
    {
        private readonly IArbitrageStrategy _strategy;
        private readonly IDexPriceAggregator _aggregator;
        private readonly OpportunityScorer _scorer;
        private readonly SimulationEngine _simulation;
        private readonly IOpportunityRepository _opportunityRepo;
        private readonly ILogger<ArbitrageOrchestrator> _logger;
        private readonly OrchestratorOptions _options;

        // Injected via DI — ISmartContractClient is null in simulation mode
        private readonly ISmartContractClient? _contractClient;

        public ArbitrageOrchestrator(
            IArbitrageStrategy strategy,
            IDexPriceAggregator aggregator,
            OpportunityScorer scorer,
            SimulationEngine simulation,
            IOpportunityRepository opportunityRepo,
            IOptions<OrchestratorOptions> options,
            ILogger<ArbitrageOrchestrator> logger,
            ISmartContractClient? contractClient = null)
        {
            _strategy = strategy;
            _aggregator = aggregator;
            _scorer = scorer;
            _simulation = simulation;
            _opportunityRepo = opportunityRepo;
            _logger = logger;
            _options = options.Value;
            _contractClient = contractClient;
        }

        public async Task RunTickAsync(CancellationToken ct)
        {
            _logger.LogDebug("Orchestrator tick — mode: {Mode}", _options.Mode);

            var pairs = _options.WatchedPairs
                .Select(p => new TokenPair(p.Base, p.Quote))
                .ToList();

            var quotes = await _aggregator.GetQuotesAsync(pairs, _options.TradeAmountUSDC, ct);
            var opportunities = await _strategy.ScanAsync(pairs, ct);
            var ranked = _scorer.Rank(opportunities);

            foreach (var opportunity in ranked)
            {
                if (opportunity.EstimatedProfitUSDC < _options.MinProfitThresholdUSDC)
                {
                    _logger.LogDebug("Opportunity {Id} skipped — profit {Profit:C} below threshold",
                        opportunity.Id, opportunity.EstimatedProfitUSDC);
                    continue;
                }

                await _opportunityRepo.SaveAsync(opportunity, ct);

                if (_options.Mode == ExecutionMode.Simulation)
                {
                    await _simulation.RunAsync(opportunity, ct);
                }
                else
                {
                    await ExecuteLiveAsync(opportunity, ct);
                }
            }
        }

        private async Task ExecuteLiveAsync(ArbitrageOpportunity opportunity, CancellationToken ct)
        {
            if (_contractClient is null)
                throw new InvalidOperationException("SmartContractClient is not registered for live mode.");

            _logger.LogInformation("Executing live arb — opportunity {Id}, estimated profit {Profit:C}",
                opportunity.Id, opportunity.EstimatedProfitUSDC);

            await _opportunityRepo.UpdateStatusAsync(opportunity.Id, OpportunityStatus.Executing, ct);

            try
            {
                var signature = await _contractClient.ExecuteArbitrageAsync(
                    opportunity.Route,
                    minOutputAmountUSDC: opportunity.InputAmountUSDC, // revert guard
                    ct);

                _logger.LogInformation("Transaction submitted — sig: {Sig}", signature);
                await _opportunityRepo.UpdateStatusAsync(opportunity.Id, OpportunityStatus.Executed, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Live execution failed for opportunity {Id}", opportunity.Id);
                await _opportunityRepo.UpdateStatusAsync(opportunity.Id, OpportunityStatus.Reverted, ct);
            }
        }
    }

    public sealed class OrchestratorOptions
    {
        public ExecutionMode Mode { get; set; } = ExecutionMode.Simulation;
        public decimal TradeAmountUSDC { get; set; } = 100m;
        public decimal MinProfitThresholdUSDC { get; set; } = 0.5m;
        public int SlippageToleranceBps { get; set; } = 50;
        public List<TokenPairConfig> WatchedPairs { get; set; } = [];
    }

    public sealed class TokenPairConfig
    {
        public required string Base { get; set; }
        public required string Quote { get; set; }
    }
}
