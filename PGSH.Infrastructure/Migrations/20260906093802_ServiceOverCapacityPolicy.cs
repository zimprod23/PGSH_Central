using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <summary>
    /// « Ce service refuse le dépassement d'effectif » — the chef's own statement, and the only way
    /// an occupancy ceiling becomes binding: « autoriser le dépassement » is ticked as a matter of
    /// routine on a base where 233 of 353 planned cells are over capacity.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Default true, and the default is the whole safety of the migration.</b> It lands on 148
    /// existing services none of whose chefs has been asked anything; false would make every one of
    /// them strict overnight and refuse the next publication of every promotion, on a restriction
    /// nobody authored. Nothing else changes: a service that allows the override behaves exactly as
    /// it did before this column existed.
    /// </remarks>
    public partial class ServiceOverCapacityPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowsOverCapacity",
                schema: "public",
                table: "Services",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowsOverCapacity",
                schema: "public",
                table: "Services");
        }
    }
}
