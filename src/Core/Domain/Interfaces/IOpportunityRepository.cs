using Accipiter.Core.Domain.Enums;
using Accipiter.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Interfaces
{
    // ============================================================
    // Accipiter.Core / Domain / Interfaces / IOpportunityRepository.cs
    // ============================================================
    public interface IOpportunityRepository
    {
        Task SaveAsync(ArbitrageOpportunity opportunity, CancellationToken ct = default);
        Task UpdateStatusAsync(Guid id, OpportunityStatus status, CancellationToken ct = default);
        Task<IReadOnlyList<ArbitrageOpportunity>> GetTopOpportunitiesAsync(int count, CancellationToken ct = default);
    }
}
