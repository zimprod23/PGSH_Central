using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Removing_Duplicate_serviceChef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Services_Users_ServiceChefId",
                schema: "public",
                table: "Services");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_Users__serviceChefId",
                schema: "public",
                table: "Services");

            migrationBuilder.DropIndex(
                name: "IX_Services__serviceChefId",
                schema: "public",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "_serviceChefId",
                schema: "public",
                table: "Services");

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Users_ServiceChefId",
                schema: "public",
                table: "Services",
                column: "ServiceChefId",
                principalSchema: "public",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Services_Users_ServiceChefId",
                schema: "public",
                table: "Services");

            migrationBuilder.AddColumn<Guid>(
                name: "_serviceChefId",
                schema: "public",
                table: "Services",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services__serviceChefId",
                schema: "public",
                table: "Services",
                column: "_serviceChefId");

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Users_ServiceChefId",
                schema: "public",
                table: "Services",
                column: "ServiceChefId",
                principalSchema: "public",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Users__serviceChefId",
                schema: "public",
                table: "Services",
                column: "_serviceChefId",
                principalSchema: "public",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
