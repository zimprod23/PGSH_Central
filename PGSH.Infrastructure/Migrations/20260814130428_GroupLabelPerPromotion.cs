using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GroupLabelPerPromotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademicGroup_Year_Label",
                schema: "public",
                table: "AcademicGroups");

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroup_Year_Level_Label",
                schema: "public",
                table: "AcademicGroups",
                columns: new[] { "AcademicYearId", "LevelId", "Label" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademicGroup_Year_Level_Label",
                schema: "public",
                table: "AcademicGroups");

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroup_Year_Label",
                schema: "public",
                table: "AcademicGroups",
                columns: new[] { "AcademicYearId", "Label" },
                unique: true);
        }
    }
}
