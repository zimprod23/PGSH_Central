using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegistrationHolds",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Evidence = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RaisedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RaisedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleasedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleaseNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistrationHolds_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalSchema: "public",
                        principalTable: "Registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationHold_Reason_RaisedOn",
                schema: "public",
                table: "RegistrationHolds",
                columns: new[] { "Reason", "RaisedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationHold_Registration_Active",
                schema: "public",
                table: "RegistrationHolds",
                column: "RegistrationId",
                filter: "\"ReleasedOn\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistrationHolds",
                schema: "public");
        }
    }
}
