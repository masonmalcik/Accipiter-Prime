using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence
{
    public sealed class WalletSnapshotLogConfiguration : IEntityTypeConfiguration<WalletSnapshotLog>
    {
        public void Configure(EntityTypeBuilder<WalletSnapshotLog> b)
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.UsdcBalance).HasPrecision(18, 8);
            b.Property(e => e.TotalValueUSDC).HasPrecision(18, 8);
            b.HasIndex(e => e.RecordedAt);
        }
    }
}
