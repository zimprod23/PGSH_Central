using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinalYearEntryWaiver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinalYearEntryWaivers",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicYearId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OutstandingAtGrant = table.Column<int>(type: "integer", nullable: false),
                    OutstandingSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalYearEntryWaivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinalYearEntryWaivers_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalSchema: "public",
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinalYearEntryWaivers_Users_StudentId",
                        column: x => x.StudentId,
                        principalSchema: "public",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinalYearEntryWaiver_Student_Year",
                schema: "public",
                table: "FinalYearEntryWaivers",
                columns: new[] { "StudentId", "AcademicYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinalYearEntryWaivers_AcademicYearId",
                schema: "public",
                table: "FinalYearEntryWaivers",
                column: "AcademicYearId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinalYearEntryWaivers",
                schema: "public");
        }
    }
}
