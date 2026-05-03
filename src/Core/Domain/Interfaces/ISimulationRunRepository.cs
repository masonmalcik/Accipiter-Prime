using Accipiter.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Core.Domain.Interfaces
{
    public interface ISimulationRunRepository
    {
        Task<Guid> StartRunAsync(CancellationToken ct = default);
        Task RecordTradeAsync(SimulationTradeResult result, CancellationToken ct = default);
        Task EndRunAsync(Guid runId, CancellationToken ct = default);
    }
}
