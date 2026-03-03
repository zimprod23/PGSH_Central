using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class removing_cascade_from_center_hospital : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Hospitals_Centers_CenterId",
                schema: "public",
                table: "Hospitals");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_Hospitals_HospitalId",
                schema: "public",
                table: "Services");

            migrationBuilder.AddForeignKey(
                name: "FK_Hospitals_Centers_CenterId",
                schema: "public",
                table: "Hospitals",
                column: "CenterId",
                principalSchema: "public",
                principalTable: "Centers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Hospitals_HospitalId",
                schema: "public",
                table: "Services",
                column: "HospitalId",
                principalSchema: "public",
                principalTable: "Hospitals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Hospitals_Centers_CenterId",
                schema: "public",
                table: "Hospitals");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_Hospitals_HospitalId",
                schema: "public",
                table: "Services");

            migrationBuilder.AddForeignKey(
                name: "FK_Hospitals_Centers_CenterId",
                schema: "public",
                table: "Hospitals",
                column: "CenterId",
                principalSchema: "public",
                principalTable: "Centers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Hospitals_HospitalId",
                schema: "public",
                table: "Services",
                column: "HospitalId",
                principalSchema: "public",
                principalTable: "Hospitals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
