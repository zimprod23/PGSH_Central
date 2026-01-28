using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class History_Level_Config : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_History_Users_StudentId",
                schema: "public",
                table: "History");

            migrationBuilder.DropPrimaryKey(
                name: "PK_History",
                schema: "public",
                table: "History");

            migrationBuilder.RenameTable(
                name: "History",
                schema: "public",
                newName: "Histories",
                newSchema: "public");

            migrationBuilder.RenameIndex(
                name: "IX_History_StudentId",
                schema: "public",
                table: "Histories",
                newName: "IX_Histories_StudentId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Histories",
                schema: "public",
                table: "Histories",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Histories_Users_StudentId",
                schema: "public",
                table: "Histories",
                column: "StudentId",
                principalSchema: "public",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Histories_Users_StudentId",
                schema: "public",
                table: "Histories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Histories",
                schema: "public",
                table: "Histories");

            migrationBuilder.RenameTable(
                name: "Histories",
                schema: "public",
                newName: "History",
                newSchema: "public");

            migrationBuilder.RenameIndex(
                name: "IX_Histories_StudentId",
                schema: "public",
                table: "History",
                newName: "IX_History_StudentId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_History",
                schema: "public",
                table: "History",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_History_Users_StudentId",
                schema: "public",
                table: "History",
                column: "StudentId",
                principalSchema: "public",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
