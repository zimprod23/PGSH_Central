using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueRegistrationAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Registration_Student_Year",
                schema: "public",
                table: "Registrations");

            migrationBuilder.AddColumn<DateTime>(
                name: "EvaluatedAt",
                schema: "public",
                table: "ServiceEvaluation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvaluatedByUserId",
                schema: "public",
                table: "ServiceEvaluation",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiceChefAssignment",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceChefAssignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceChefAssignment_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "public",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceChefAssignment_Users_EmployeeId",
                        column: x => x.EmployeeId,
                        principalSchema: "public",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Registration_Student_Year",
                schema: "public",
                table: "Registrations",
                columns: new[] { "StudentId", "AcademicYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceChefAssignment_EmployeeId",
                schema: "public",
                table: "ServiceChefAssignment",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceChefAssignment_ServiceId",
                schema: "public",
                table: "ServiceChefAssignment",
                column: "ServiceId",
                unique: true,
                filter: "\"EndDate\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceChefAssignment",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_Registration_Student_Year",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "EvaluatedAt",
                schema: "public",
                table: "ServiceEvaluation");

            migrationBuilder.DropColumn(
                name: "EvaluatedByUserId",
                schema: "public",
                table: "ServiceEvaluation");

            migrationBuilder.CreateIndex(
                name: "IX_Registration_Student_Year",
                schema: "public",
                table: "Registrations",
                columns: new[] { "StudentId", "AcademicYearId" });
        }
    }
}
