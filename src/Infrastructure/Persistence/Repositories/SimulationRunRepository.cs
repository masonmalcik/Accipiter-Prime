using Accipiter.Core.Domain.Interfaces;
using Accipiter.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence.Repoistories
{
    public sealed class SimulationRunRepository : ISimulationRunRepository
    {
        private readonly AccipitersDbContext _db;
        private readonly ILogger<SimulationRunRepository> _logger;

        // Tracks the active run ID for the lifetime of this scoped instance
        private Guid? _activeRunId;

        public SimulationRunRepository(
            AccipitersDbContext db,
            ILogger<SimulationRunRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Guid> StartRunAsync(CancellationToken ct = default)
        {
            var run = new SimulationRunLog
            {
                Id = Guid.NewGuid(),
                StartedAt = DateTimeOffset.UtcNow
            };

            _db.SimulationRuns.Add(run);
            await _db.SaveChangesAsync(ct);

            _activeRunId = run.Id;
            _logger.LogInformation("Simulation run started — id: {RunId}", run.Id);
            return run.Id;
        }

        public async Task RecordTradeAsync(SimulationTradeResult result, CancellationToken ct = default)
        {
            if (_activeRunId is null)
                _activeRunId = await StartRunAsync(ct);

            var trade = new SimulationTradeLog
            {
                Id = Guid.NewGuid(),
                SimulationRunId = _activeRunId.Value,
                OpportunityId = result.OpportunityId,
                SimulatedAt = result.SimulatedAt,
                InputAmountUSDC = result.InputAmountUSDC,
                OutputAmountUSDC = result.AdjustedOutputAmountUSDC,
                ProfitUSDC = result.NetProfitUSDC,
                WouldHaveReverted = result.WouldHaveReverted
            };

            _db.SimulationTrades.Add(trade);

            // Update the run aggregate totals
            var run = await _db.SimulationRuns.FindAsync([_activeRunId.Value], ct);
            if (run is not null)
            {
                run.TotalOpportunitiesFound++;

                if (result.NetProfitUSDC >= 0)
                    run.TotalProfitUSDC += result.NetProfitUSDC;
                else
                    run.TotalLossUSDC += Math.Abs(result.NetProfitUSDC);

                run.NetPnLUSDC = run.TotalProfitUSDC - run.TotalLossUSDC;
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogDebug("Simulation trade recorded — opportunity: {OpId}, P&L: {PnL:C}",
                result.OpportunityId, result.NetProfitUSDC);
        }

        public async Task EndRunAsync(Guid runId, CancellationToken ct = default)
        {
            var run = await _db.SimulationRuns.FindAsync([runId], ct);
            if (run is null)
            {
                _logger.LogWarning("EndRunAsync called for unknown run id: {RunId}", runId);
                return;
            }

            run.EndedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Simulation run ended — id: {RunId} | trades: {Count} | net P&L: {PnL:C}",
                runId, run.TotalOpportunitiesFound, run.NetPnLUSDC);
        }
    }
}
