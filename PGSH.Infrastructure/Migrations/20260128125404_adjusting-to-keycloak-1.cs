using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class adjustingtokeycloak1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordHash",
                schema: "public",
                table: "Users");

            migrationBuilder.AddColumn<DateTime>(
                name: "IdentityLinkedAt",
                schema: "public",
                table: "Users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdentityLinkedAt",
                schema: "public",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                schema: "public",
                table: "Users",
                type: "text",
                nullable: true);
        }
    }
}
