using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServicePeriodIsStarted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStarted",
                schema: "public",
                table: "ServicePeriods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Completed periods were obviously started — keep them visible to the chef worklist
            // (for evaluation history). Not-yet-complete periods stay inactive until an admin starts them.
            migrationBuilder.Sql(
                "UPDATE public.\"ServicePeriods\" SET \"IsStarted\" = true WHERE \"IsComplete\" = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsStarted",
                schema: "public",
                table: "ServicePeriods");
        }
    }
}
