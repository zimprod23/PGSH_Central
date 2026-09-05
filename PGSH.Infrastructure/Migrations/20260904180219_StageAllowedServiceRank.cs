using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StageAllowedServiceRank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Rank",
                schema: "public",
                table: "StageAllowedServices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // ⚠ Backfilled in the order the arranger already walked — ORDER BY "ServiceId", i.e.
            // RotationArranger's OrderBy(s => s.Id) — so applying this migration changes no plan.
            // The rank starts life describing what the code already did; only an explicit reorder
            // moves it. Without this the unique index below fails outright: every one of the 146
            // authored rows would carry the default 0, several per stage.
            migrationBuilder.Sql("""
                UPDATE public."StageAllowedServices" AS t
                SET    "Rank" = r.rn
                FROM (
                    SELECT "StageId",
                           "ServiceId",
                           ROW_NUMBER() OVER (PARTITION BY "StageId" ORDER BY "ServiceId") AS rn
                    FROM   public."StageAllowedServices"
                ) AS r
                WHERE  t."StageId"   = r."StageId"
                  AND  t."ServiceId" = r."ServiceId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_StageAllowedServices_Stage_Rank",
                schema: "public",
                table: "StageAllowedServices",
                columns: new[] { "StageId", "Rank" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StageAllowedServices_Stage_Rank",
                schema: "public",
                table: "StageAllowedServices");

            migrationBuilder.DropColumn(
                name: "Rank",
                schema: "public",
                table: "StageAllowedServices");
        }
    }
}
