using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AffectationImportJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AffectationImports",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<int>(type: "integer", nullable: false),
                    LevelId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AppliedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AffectationCount = table.Column<int>(type: "integer", nullable: false),
                    ReplacedPeriodCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffectationImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffectationImports_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalSchema: "public",
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AffectationImportEntries",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffectationImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageId = table.Column<int>(type: "integer", nullable: false),
                    InternshipAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    WrittenPeriodCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffectationImportEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffectationImportEntries_AffectationImports_AffectationImpo~",
                        column: x => x.AffectationImportId,
                        principalSchema: "public",
                        principalTable: "AffectationImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReplacedPeriods",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffectationImportEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsStarted = table.Column<bool>(type: "boolean", nullable: false),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    IsInterrupted = table.Column<bool>(type: "boolean", nullable: false),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    IsDelocalized = table.Column<bool>(type: "boolean", nullable: false),
                    CohortSlotAssignmentId = table.Column<int>(type: "integer", nullable: true),
                    DelocalizationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacedPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplacedPeriods_AffectationImportEntries_AffectationImportE~",
                        column: x => x.AffectationImportEntryId,
                        principalSchema: "public",
                        principalTable: "AffectationImportEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffectationImportEntries_AffectationImportId",
                schema: "public",
                table: "AffectationImportEntries",
                column: "AffectationImportId");

            migrationBuilder.CreateIndex(
                name: "IX_AffectationImportEntry_Assignment",
                schema: "public",
                table: "AffectationImportEntries",
                column: "InternshipAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AffectationImport_Year_Level_Applied",
                schema: "public",
                table: "AffectationImports",
                columns: new[] { "AcademicYearId", "LevelId", "AppliedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplacedPeriods_AffectationImportEntryId",
                schema: "public",
                table: "ReplacedPeriods",
                column: "AffectationImportEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplacedPeriods",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AffectationImportEntries",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AffectationImports",
                schema: "public");
        }
    }
}
