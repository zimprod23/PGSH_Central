using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    [DbContext(typeof(PGSH.Infrastructure.Database.ApplicationDbContext))]
    [Migration("20260521000001_AddServiceSpecialty")]
    public partial class AddServiceSpecialty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Specialty",
                schema: "public",
                table: "Services",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Specialty",
                schema: "public",
                table: "Services");
        }
    }
}
