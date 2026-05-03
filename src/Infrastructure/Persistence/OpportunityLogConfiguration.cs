using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence
{
    public sealed class OpportunityLogConfiguration : IEntityTypeConfiguration<OpportunityLog>
    {
        public void Configure(EntityTypeBuilder<OpportunityLog> b)
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.InputAmountUSDC).HasPrecision(18, 8);
            b.Property(e => e.EstimatedOutputAmountUSDC).HasPrecision(18, 8);
            b.Property(e => e.EstimatedProfitUSDC).HasPrecision(18, 8);
            b.HasIndex(e => e.DiscoveredAt);
            b.HasIndex(e => e.Status);
        }
    }
}
