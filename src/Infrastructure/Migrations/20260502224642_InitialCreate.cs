using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accipiter.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutedTrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TxSignature = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InputAmountUSDC = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ActualOutputAmountUSDC = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ActualProfitUSDC = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reverted = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutedTrades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Opportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StrategyType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutputToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputAmountUSDC = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    EstimatedOutputAmountUSDC = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    EstimatedProfitUSDC = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    BuyDex = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SellDex = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RouteJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ConfidenceScore = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Opportunities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SimulationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TotalOpportunitiesFound = table.Column<int>(type: "int", nullable: false),
                    TotalProfitUSDC = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalLossUSDC = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetPnLUSDC = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsdcBalance = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    SolBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalValueUSDC = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SimulationTrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SimulationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SimulatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InputAmountUSDC = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OutputAmountUSDC = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ProfitUSDC = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WouldHaveReverted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulationTrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SimulationTrades_SimulationRuns_SimulationRunId",
                        column: x => x.SimulationRunId,
                        principalTable: "SimulationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_DiscoveredAt",
                table: "Opportunities",
                column: "DiscoveredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_Status",
                table: "Opportunities",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SimulationTrades_SimulationRunId",
                table: "SimulationTrades",
                column: "SimulationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletSnapshots_RecordedAt",
                table: "WalletSnapshots",
                column: "RecordedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutedTrades");

            migrationBuilder.DropTable(
                name: "Opportunities");

            migrationBuilder.DropTable(
                name: "SimulationTrades");

            migrationBuilder.DropTable(
                name: "WalletSnapshots");

            migrationBuilder.DropTable(
                name: "SimulationRuns");
        }
    }
}
