using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HolidayCountsAsWorkingDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Purely additive, and false is the old arithmetic line for line: a closure that neither
            // counts toward a duration nor may bound a window. No date already posed moves, which is what
            // lets this land on a base carrying a published promotion.
            migrationBuilder.AddColumn<bool>(
                name: "CountsAsWorkingDay",
                schema: "public",
                table: "Holidays",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountsAsWorkingDay",
                schema: "public",
                table: "Holidays");
        }
    }
}
