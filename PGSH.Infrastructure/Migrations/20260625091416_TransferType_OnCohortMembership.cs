using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TransferType_OnCohortMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OriginalCohortId",
                schema: "public",
                table: "CohortMembership",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferType",
                schema: "public",
                table: "CohortMembership",
                type: "text",
                nullable: false,
                defaultValue: "Definitive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalCohortId",
                schema: "public",
                table: "CohortMembership");

            migrationBuilder.DropColumn(
                name: "TransferType",
                schema: "public",
                table: "CohortMembership");
        }
    }
}
