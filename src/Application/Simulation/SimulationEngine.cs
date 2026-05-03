using Accipiter.Core.Domain.Enums;
using Accipiter.Core.Domain.Interfaces;
using Accipiter.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Application.Simulation
{
    /// <summary>
    /// Paper-trades an ArbitrageOpportunity against the fetched quotes.
    /// No Solana RPC calls are made — this is purely local computation.
    /// </summary>
    public sealed class SimulationEngine
    {
        private readonly IOpportunityRepository _opportunityRepo;
        private readonly ISimulationRunRepository _runRepo;
        private readonly ILogger<SimulationEngine> _logger;

        public SimulationEngine(
            IOpportunityRepository opportunityRepo,
            ISimulationRunRepository runRepo,
            ILogger<SimulationEngine> logger)
        {
            _opportunityRepo = opportunityRepo;
            _runRepo = runRepo;
            _logger = logger;
        }

        public async Task<SimulationTradeResult> RunAsync(
            ArbitrageOpportunity opportunity,
            CancellationToken ct = default)
        {
            // Model slippage: reduce estimated output by half the bps
            var slippageFactor = 1m - (opportunity.Route.EstimatedSlippageBps / 2m / 10_000m);
            var adjustedOutput = opportunity.EstimatedOutputAmountUSDC * slippageFactor;
            var netProfit = adjustedOutput - opportunity.InputAmountUSDC - opportunity.Route.TotalFeeEstimateUSDC;

            var result = new SimulationTradeResult
            {
                OpportunityId = opportunity.Id,
                SimulatedAt = DateTimeOffset.UtcNow,
                InputAmountUSDC = opportunity.InputAmountUSDC,
                AdjustedOutputAmountUSDC = adjustedOutput,
                NetProfitUSDC = netProfit,
                WouldHaveReverted = adjustedOutput < opportunity.InputAmountUSDC
            };

            await _runRepo.RecordTradeAsync(result, ct);
            await _opportunityRepo.UpdateStatusAsync(opportunity.Id, OpportunityStatus.Simulated, ct);

            _logger.LogInformation(
                "[SIM] Opportunity {Id} | {BuyDex} → {SellDex} | " +
                "Input: {In:C} | Output: {Out:C} | Net P&L: {PnL:C} | Revert: {Revert}",
                opportunity.Id,
                opportunity.BuyDex,
                opportunity.SellDex,
                opportunity.InputAmountUSDC,
                adjustedOutput,
                netProfit,
                result.WouldHaveReverted);

            return result;
        }
    }

}
