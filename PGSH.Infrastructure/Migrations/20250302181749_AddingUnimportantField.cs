using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddingUnimportantField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NonImportantField",
                schema: "public",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NonImportantField",
                schema: "public",
                table: "Users");
        }
    }
}
