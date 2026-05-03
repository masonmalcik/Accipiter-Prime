using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence
{
    public sealed class SimulationRunLogConfiguration : IEntityTypeConfiguration<SimulationRunLog>
    {
        public void Configure(EntityTypeBuilder<SimulationRunLog> b)
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.NetPnLUSDC).HasPrecision(18, 8);
            b.HasMany(e => e.Trades).WithOne(t => t.SimulationRun)
                .HasForeignKey(t => t.SimulationRunId);
        }
    }
}
