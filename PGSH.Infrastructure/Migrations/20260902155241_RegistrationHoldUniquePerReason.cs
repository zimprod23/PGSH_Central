using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationHoldUniquePerReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RegistrationHold_Registration_Active",
                schema: "public",
                table: "RegistrationHolds");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationHold_Registration_Reason_Active",
                schema: "public",
                table: "RegistrationHolds",
                columns: new[] { "RegistrationId", "Reason" },
                unique: true,
                filter: "\"ReleasedOn\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RegistrationHold_Registration_Reason_Active",
                schema: "public",
                table: "RegistrationHolds");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationHold_Registration_Active",
                schema: "public",
                table: "RegistrationHolds",
                column: "RegistrationId",
                filter: "\"ReleasedOn\" IS NULL");
        }
    }
}
