using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StageSlotAcademicYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StageSlot_Stage_Period",
                schema: "public",
                table: "StageSlots");

            migrationBuilder.AddColumn<int>(
                name: "AcademicYearId",
                schema: "public",
                table: "StageSlots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The table is empty on every known environment — the legacy Access import carried no
            // planning grid, only per-student date ranges — but 0 is not a real academic year, so the
            // foreign key below would reject any row that did exist. Attribute those to the current
            // year (the only defensible guess: a slot's dates belong to the year it was authored in)
            // and drop the placeholder default so nothing inherits it later.
            migrationBuilder.Sql("""
                UPDATE public."StageSlots"
                SET "AcademicYearId" = (SELECT "Id" FROM public."AcademicYears" WHERE "IsCurrent" LIMIT 1)
                WHERE "AcademicYearId" = 0;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE public."StageSlots" ALTER COLUMN "AcademicYearId" DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_StageSlot_Stage_Year_Period",
                schema: "public",
                table: "StageSlots",
                columns: new[] { "StageId", "AcademicYearId", "PeriodNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageSlots_AcademicYearId",
                schema: "public",
                table: "StageSlots",
                column: "AcademicYearId");

            migrationBuilder.AddForeignKey(
                name: "FK_StageSlots_AcademicYears_AcademicYearId",
                schema: "public",
                table: "StageSlots",
                column: "AcademicYearId",
                principalSchema: "public",
                principalTable: "AcademicYears",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StageSlots_AcademicYears_AcademicYearId",
                schema: "public",
                table: "StageSlots");

            migrationBuilder.DropIndex(
                name: "IX_StageSlot_Stage_Year_Period",
                schema: "public",
                table: "StageSlots");

            migrationBuilder.DropIndex(
                name: "IX_StageSlots_AcademicYearId",
                schema: "public",
                table: "StageSlots");

            migrationBuilder.DropColumn(
                name: "AcademicYearId",
                schema: "public",
                table: "StageSlots");

            migrationBuilder.CreateIndex(
                name: "IX_StageSlot_Stage_Period",
                schema: "public",
                table: "StageSlots",
                columns: new[] { "StageId", "PeriodNumber" },
                unique: true);
        }
    }
}
