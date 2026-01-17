using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Adding_Metadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                schema: "public",
                table: "History",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Metadata",
                schema: "public",
                table: "History");
        }
    }
}
