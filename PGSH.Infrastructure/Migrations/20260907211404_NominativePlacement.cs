using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NominativePlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlacementMode",
                schema: "public",
                table: "StageAllowedServices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Rotation");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "public",
                table: "CohortSlotAssignments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Arranged");

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                schema: "public",
                table: "AcademicGroups",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlacementMode",
                schema: "public",
                table: "StageAllowedServices");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "public",
                table: "CohortSlotAssignments");

            migrationBuilder.DropColumn(
                name: "Purpose",
                schema: "public",
                table: "AcademicGroups");
        }
    }
}
