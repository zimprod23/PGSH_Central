using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StageRotationModeAndSlotCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RotationMode",
                schema: "public",
                table: "Stages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PerPeriod");

            migrationBuilder.CreateTable(
                name: "ServicePeriodSlotCoverage",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServicePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    CohortSlotAssignmentId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePeriodSlotCoverage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServicePeriodSlotCoverage_CohortSlotAssignments_CohortSlotA~",
                        column: x => x.CohortSlotAssignmentId,
                        principalSchema: "public",
                        principalTable: "CohortSlotAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServicePeriodSlotCoverage_ServicePeriods_ServicePeriodId",
                        column: x => x.ServicePeriodId,
                        principalSchema: "public",
                        principalTable: "ServicePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicePeriodSlotCoverage_Cell_Period",
                schema: "public",
                table: "ServicePeriodSlotCoverage",
                columns: new[] { "CohortSlotAssignmentId", "ServicePeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePeriodSlotCoverage_ServicePeriodId",
                schema: "public",
                table: "ServicePeriodSlotCoverage",
                column: "ServicePeriodId");

            // Every period published before this migration came from exactly one cell — SingleService
            // is what introduces runs, and no stage can have been in that mode yet. So the existing
            // link is the complete coverage, and copying it is what keeps the four "is this cell
            // published?" guards answering the same thing after the switch as before it. Without this
            // the guards would read an empty table and cheerfully rewrite a published répartition.
            migrationBuilder.Sql("""
                INSERT INTO public."ServicePeriodSlotCoverage" ("ServicePeriodId", "CohortSlotAssignmentId")
                SELECT p."Id", p."CohortSlotAssignmentId"
                FROM public."ServicePeriods" p
                WHERE p."CohortSlotAssignmentId" IS NOT NULL
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServicePeriodSlotCoverage",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "RotationMode",
                schema: "public",
                table: "Stages");
        }
    }
}
