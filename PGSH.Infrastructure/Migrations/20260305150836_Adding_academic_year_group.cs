using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Adding_academic_year_group : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcademicYear",
                schema: "public",
                table: "Registrations");

            migrationBuilder.AddColumn<int>(
                name: "AcademicGroupId",
                schema: "public",
                table: "Registrations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcademicYearId",
                schema: "public",
                table: "Registrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegistrationDate",
                schema: "public",
                table: "Registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GroupNumber",
                schema: "public",
                table: "InternshipAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcademicGroupId",
                schema: "public",
                table: "Cohorts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AcademicYears",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademicYears", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AcademicGroups",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GroupNumber = table.Column<int>(type: "integer", nullable: false),
                    GeographicZone = table.Column<string>(type: "text", nullable: true),
                    AcademicYearId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademicGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcademicGroups_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalSchema: "public",
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_AcademicGroupId",
                schema: "public",
                table: "Registrations",
                column: "AcademicGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_AcademicYearId",
                schema: "public",
                table: "Registrations",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_AcademicGroupId",
                schema: "public",
                table: "Cohorts",
                column: "AcademicGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroups_AcademicYearId",
                schema: "public",
                table: "AcademicGroups",
                column: "AcademicYearId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroups_GroupNumber",
                schema: "public",
                table: "AcademicGroups",
                column: "GroupNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademicYears_Label",
                schema: "public",
                table: "AcademicYears",
                column: "Label",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Cohorts_AcademicGroups_AcademicGroupId",
                schema: "public",
                table: "Cohorts",
                column: "AcademicGroupId",
                principalSchema: "public",
                principalTable: "AcademicGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Registrations_AcademicGroups_AcademicGroupId",
                schema: "public",
                table: "Registrations",
                column: "AcademicGroupId",
                principalSchema: "public",
                principalTable: "AcademicGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Registrations_AcademicYears_AcademicYearId",
                schema: "public",
                table: "Registrations",
                column: "AcademicYearId",
                principalSchema: "public",
                principalTable: "AcademicYears",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cohorts_AcademicGroups_AcademicGroupId",
                schema: "public",
                table: "Cohorts");

            migrationBuilder.DropForeignKey(
                name: "FK_Registrations_AcademicGroups_AcademicGroupId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropForeignKey(
                name: "FK_Registrations_AcademicYears_AcademicYearId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropTable(
                name: "AcademicGroups",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AcademicYears",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_Registrations_AcademicGroupId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropIndex(
                name: "IX_Registrations_AcademicYearId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropIndex(
                name: "IX_Cohorts_AcademicGroupId",
                schema: "public",
                table: "Cohorts");

            migrationBuilder.DropColumn(
                name: "AcademicGroupId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "AcademicYearId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "RegistrationDate",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "GroupNumber",
                schema: "public",
                table: "InternshipAssignments");

            migrationBuilder.DropColumn(
                name: "AcademicGroupId",
                schema: "public",
                table: "Cohorts");

            migrationBuilder.AddColumn<DateOnly>(
                name: "AcademicYear",
                schema: "public",
                table: "Registrations",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));
        }
    }
}
