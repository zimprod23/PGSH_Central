using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Adding_identity_provider_to_user : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentityProviderId",
                schema: "public",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_IdentityProviderId",
                schema: "public",
                table: "Users",
                column: "IdentityProviderId",
                unique: true,
                filter: "\"IdentityProviderId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_IdentityProviderId",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IdentityProviderId",
                schema: "public",
                table: "Users");
        }
    }
}
