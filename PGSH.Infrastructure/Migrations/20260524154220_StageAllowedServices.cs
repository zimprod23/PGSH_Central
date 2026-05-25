using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StageAllowedServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StageAllowedServices",
                schema: "public",
                columns: table => new
                {
                    StageId = table.Column<int>(type: "integer", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageAllowedServices", x => new { x.StageId, x.ServiceId });
                    table.ForeignKey(
                        name: "FK_StageAllowedServices_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageAllowedServices_Stages_StageId",
                        column: x => x.StageId,
                        principalSchema: "public",
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StageAllowedServices_ServiceId",
                schema: "public",
                table: "StageAllowedServices",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_StageAllowedServices_Stage_Service",
                schema: "public",
                table: "StageAllowedServices",
                columns: new[] { "StageId", "ServiceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageAllowedServices",
                schema: "public");
        }
    }
}
