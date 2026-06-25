using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServicePeriodPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPaused",
                schema: "public",
                table: "ServicePeriods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PeriodPause",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServicePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ResumeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodPause", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeriodPause_ServicePeriods_ServicePeriodId",
                        column: x => x.ServicePeriodId,
                        principalSchema: "public",
                        principalTable: "ServicePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodPause_ServicePeriodId",
                schema: "public",
                table: "PeriodPause",
                column: "ServicePeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PeriodPause",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "IsPaused",
                schema: "public",
                table: "ServicePeriods");
        }
    }
}
