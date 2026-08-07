using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Delocalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDelocalized",
                schema: "public",
                table: "ServicePeriods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FicheReference",
                schema: "public",
                table: "ServiceEvaluation",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Delocalization",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServicePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DemandeId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delocalization", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Delocalization_ServicePeriods_ServicePeriodId",
                        column: x => x.ServicePeriodId,
                        principalSchema: "public",
                        principalTable: "ServicePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Delocalization_ServicePeriodId",
                schema: "public",
                table: "Delocalization",
                column: "ServicePeriodId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Delocalization",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "IsDelocalized",
                schema: "public",
                table: "ServicePeriods");

            migrationBuilder.DropColumn(
                name: "FicheReference",
                schema: "public",
                table: "ServiceEvaluation");
        }
    }
}
