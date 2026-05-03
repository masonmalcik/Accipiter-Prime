using Accipiter.Core.Domain.Enums;
using Accipiter.Core.Domain.Interfaces;
using Accipiter.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence.Repositories
{
    public sealed class OpportunityRepository : IOpportunityRepository
    {
        private readonly AccipitersDbContext _db;
        private readonly ILogger<OpportunityRepository> _logger;

        public OpportunityRepository(
            AccipitersDbContext db,
            ILogger<OpportunityRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task SaveAsync(ArbitrageOpportunity opportunity, CancellationToken ct = default)
        {
            var log = new OpportunityLog
            {
                Id = opportunity.Id,
                DiscoveredAt = opportunity.DiscoveredAt,
                StrategyType = opportunity.StrategyType.ToString(),
                InputToken = opportunity.InputToken,
                OutputToken = opportunity.OutputToken,
                InputAmountUSDC = opportunity.InputAmountUSDC,
                EstimatedOutputAmountUSDC = opportunity.EstimatedOutputAmountUSDC,
                EstimatedProfitUSDC = opportunity.EstimatedProfitUSDC,
                BuyDex = opportunity.BuyDex,
                SellDex = opportunity.SellDex,
                RouteJson = JsonSerializer.Serialize(opportunity.Route),
                Status = opportunity.Status.ToString(),
                ConfidenceScore = opportunity.ConfidenceScore
            };

            _db.Opportunities.Add(log);
            await _db.SaveChangesAsync(ct);

            _logger.LogDebug("Opportunity saved — id: {Id}, profit: {Profit:C}",
                opportunity.Id, opportunity.EstimatedProfitUSDC);
        }

        public async Task UpdateStatusAsync(Guid id, OpportunityStatus status, CancellationToken ct = default)
        {
            var log = await _db.Opportunities.FindAsync([id], ct);
            if (log is null)
            {
                _logger.LogWarning("UpdateStatusAsync — opportunity {Id} not found", id);
                return;
            }

            log.Status = status.ToString();
            await _db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<ArbitrageOpportunity>> GetTopOpportunitiesAsync(
            int count,
            CancellationToken ct = default)
        {
            var logs = await _db.Opportunities
                .OrderByDescending(o => o.EstimatedProfitUSDC)
                .Take(count)
                .ToListAsync(ct);

            return logs.Select(log => new ArbitrageOpportunity
            {
                Id = log.Id,
                DiscoveredAt = log.DiscoveredAt,
                InputToken = log.InputToken,
                OutputToken = log.OutputToken,
                InputAmountUSDC = log.InputAmountUSDC,
                EstimatedOutputAmountUSDC = log.EstimatedOutputAmountUSDC,
                BuyDex = log.BuyDex,
                SellDex = log.SellDex,
                ConfidenceScore = log.ConfidenceScore,
                Status = Enum.Parse<OpportunityStatus>(log.Status),
                StrategyType = Enum.Parse<StrategyType>(log.StrategyType),
                Route = JsonSerializer.Deserialize<TradeRoute>(log.RouteJson)!
            }).ToList();
        }
    }
}
