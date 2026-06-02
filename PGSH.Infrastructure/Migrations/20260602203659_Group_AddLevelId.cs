using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Group_AddLevelId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LevelId",
                schema: "public",
                table: "AcademicGroups",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroups_LevelId",
                schema: "public",
                table: "AcademicGroups",
                column: "LevelId");

            migrationBuilder.AddForeignKey(
                name: "FK_AcademicGroups_Levels_LevelId",
                schema: "public",
                table: "AcademicGroups",
                column: "LevelId",
                principalSchema: "public",
                principalTable: "Levels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AcademicGroups_Levels_LevelId",
                schema: "public",
                table: "AcademicGroups");

            migrationBuilder.DropIndex(
                name: "IX_AcademicGroups_LevelId",
                schema: "public",
                table: "AcademicGroups");

            migrationBuilder.DropColumn(
                name: "LevelId",
                schema: "public",
                table: "AcademicGroups");
        }
    }
}
