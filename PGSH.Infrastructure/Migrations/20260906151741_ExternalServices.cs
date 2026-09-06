using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <summary>
    /// « Ce service est hors faculté » — a CHU in another region, a private clinic, a hospital
    /// abroad. It exists in the catalogue only so a délocalisation has something to name.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Default false, on all 148 existing rows.</b> Every service already in the base is one the
    /// faculty runs; an external one is created deliberately and never by a migration. False is also
    /// exactly the behaviour that existed before the column, so nothing changes for any of them: the
    /// flag only ever removes a service from the rotation, the capacity maths and the chef worklists.
    /// </remarks>
    public partial class ExternalServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExternal",
                schema: "public",
                table: "Services",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsExternal",
                schema: "public",
                table: "Services");
        }
    }
}
