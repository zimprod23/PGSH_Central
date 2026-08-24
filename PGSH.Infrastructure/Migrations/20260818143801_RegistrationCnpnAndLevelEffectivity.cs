using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationCnpnAndLevelEffectivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CnpnSource",
                schema: "public",
                table: "Registrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CnpnVersionId",
                schema: "public",
                table: "Registrations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CnpnLevelEffectivities",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CnpnVersionId = table.Column<int>(type: "integer", nullable: false),
                    LevelId = table.Column<int>(type: "integer", nullable: false),
                    FromAcademicYearId = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CnpnLevelEffectivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CnpnLevelEffectivities_AcademicYears_FromAcademicYearId",
                        column: x => x.FromAcademicYearId,
                        principalSchema: "public",
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CnpnLevelEffectivities_CnpnVersions_CnpnVersionId",
                        column: x => x.CnpnVersionId,
                        principalSchema: "public",
                        principalTable: "CnpnVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CnpnLevelEffectivities_Levels_LevelId",
                        column: x => x.LevelId,
                        principalSchema: "public",
                        principalTable: "Levels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_CnpnVersionId",
                schema: "public",
                table: "Registrations",
                column: "CnpnVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CnpnLevelEffectivities_FromAcademicYearId",
                schema: "public",
                table: "CnpnLevelEffectivities",
                column: "FromAcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_CnpnLevelEffectivity_Level_FromYear",
                schema: "public",
                table: "CnpnLevelEffectivities",
                columns: new[] { "LevelId", "FromAcademicYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CnpnLevelEffectivity_Version_Level",
                schema: "public",
                table: "CnpnLevelEffectivities",
                columns: new[] { "CnpnVersionId", "LevelId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Registrations_CnpnVersions_CnpnVersionId",
                schema: "public",
                table: "Registrations",
                column: "CnpnVersionId",
                principalSchema: "public",
                principalTable: "CnpnVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── Backfill ──────────────────────────────────────────────────────────────────────────
            //
            // Every registration on record predates the question this column asks, so the only honest
            // answer is the student's own stamp — that is literally what the application read for
            // these years before the column existed, so the backfill changes no behaviour, it only
            // freezes what was already being computed.
            //
            // Marked `Backfilled` rather than `StudentStamp` precisely because it is *not* evidence of
            // what the faculty decided at the time: no effectivity rule existed, nobody was asked, and
            // a year re-read tomorrow must not be mistaken for one that was resolved when it ran.
            //
            // Students with no stamp — ~2,200 of them, the ones the legacy import caught mid-cursus —
            // stay null, and every reader falls back to the student. Stamping them with a guess here
            // would put the guess beyond reach of the correction path.
            migrationBuilder.Sql("""
                UPDATE public."Registrations" AS r
                SET    "CnpnVersionId" = u."CnpnVersionId",
                       "CnpnSource"    = 'Backfilled'
                FROM   public."Users" AS u
                WHERE  u."Id" = r."StudentId"
                  AND  u."CnpnVersionId" IS NOT NULL
                  AND  r."CnpnVersionId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Registrations_CnpnVersions_CnpnVersionId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropTable(
                name: "CnpnLevelEffectivities",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_Registrations_CnpnVersionId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "CnpnSource",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "CnpnVersionId",
                schema: "public",
                table: "Registrations");
        }
    }
}
