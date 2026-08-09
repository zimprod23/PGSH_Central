using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationYearOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Registrations_AcademicYearId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.AddColumn<DateTime>(
                name: "OutcomeRecordedOn",
                schema: "public",
                table: "Registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutcomeSource",
                schema: "public",
                table: "Registrations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Registration_Year_Level",
                schema: "public",
                table: "Registrations",
                columns: new[] { "AcademicYearId", "LevelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Registration_Year_Level",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "OutcomeRecordedOn",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "OutcomeSource",
                schema: "public",
                table: "Registrations");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_AcademicYearId",
                schema: "public",
                table: "Registrations",
                column: "AcademicYearId");
        }
    }
}
