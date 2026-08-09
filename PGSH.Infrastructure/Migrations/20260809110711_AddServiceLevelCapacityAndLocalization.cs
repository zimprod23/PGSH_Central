using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceLevelCapacityAndLocalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "X",
                schema: "public",
                table: "Services",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Y",
                schema: "public",
                table: "Services",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Z",
                schema: "public",
                table: "Services",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiceLevelCapacities",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    LevelId = table.Column<int>(type: "integer", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLevelCapacities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceLevelCapacities_Levels_LevelId",
                        column: x => x.LevelId,
                        principalSchema: "public",
                        principalTable: "Levels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceLevelCapacities_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLevelCapacities_LevelId",
                schema: "public",
                table: "ServiceLevelCapacities",
                column: "LevelId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLevelCapacities_ServiceId_LevelId",
                schema: "public",
                table: "ServiceLevelCapacities",
                columns: new[] { "ServiceId", "LevelId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceLevelCapacities",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "X",
                schema: "public",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "Y",
                schema: "public",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "Z",
                schema: "public",
                table: "Services");
        }
    }
}
