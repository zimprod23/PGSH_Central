using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Fix_academicGroup_indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademicGroups_AcademicYearId",
                schema: "public",
                table: "AcademicGroups");

            migrationBuilder.DropIndex(
                name: "IX_AcademicGroups_GroupNumber",
                schema: "public",
                table: "AcademicGroups");

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroup_Year_Label",
                schema: "public",
                table: "AcademicGroups",
                columns: new[] { "AcademicYearId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroup_Year_Number",
                schema: "public",
                table: "AcademicGroups",
                columns: new[] { "AcademicYearId", "GroupNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademicGroup_Year_Label",
                schema: "public",
                table: "AcademicGroups");

            migrationBuilder.DropIndex(
                name: "IX_AcademicGroup_Year_Number",
                schema: "public",
                table: "AcademicGroups");

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroups_AcademicYearId",
                schema: "public",
                table: "AcademicGroups",
                column: "AcademicYearId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroups_GroupNumber",
                schema: "public",
                table: "AcademicGroups",
                column: "GroupNumber",
                unique: true);
        }
    }
}
