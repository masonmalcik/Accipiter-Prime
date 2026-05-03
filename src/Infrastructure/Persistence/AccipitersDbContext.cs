using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Accipiter.Infrastructure.Persistence
{
    public sealed class AccipitersDbContext : DbContext
    {
        public AccipitersDbContext(DbContextOptions<AccipitersDbContext> options)
            : base(options) { }

        public DbSet<OpportunityLog> Opportunities => Set<OpportunityLog>();
        public DbSet<SimulationRunLog> SimulationRuns => Set<SimulationRunLog>();
        public DbSet<SimulationTradeLog> SimulationTrades => Set<SimulationTradeLog>();
        public DbSet<ExecutedTradeLog> ExecutedTrades => Set<ExecutedTradeLog>();
        public DbSet<WalletSnapshotLog> WalletSnapshots => Set<WalletSnapshotLog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccipitersDbContext).Assembly);
            base.OnModelCreating(modelBuilder);
        }
    }
}
