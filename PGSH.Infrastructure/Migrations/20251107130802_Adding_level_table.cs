using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Adding_level_table : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Level_AcademicProgram",
                schema: "public",
                table: "Stages");

            migrationBuilder.DropColumn(
                name: "Level_Id",
                schema: "public",
                table: "Stages");

            migrationBuilder.DropColumn(
                name: "Level_Label",
                schema: "public",
                table: "Stages");

            migrationBuilder.DropColumn(
                name: "Level_AcademicProgram",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "Level_Id",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "Level_Label",
                schema: "public",
                table: "Registrations");

            migrationBuilder.RenameColumn(
                name: "Specialty",
                schema: "public",
                table: "Users",
                newName: "WorkPlace");

            migrationBuilder.RenameColumn(
                name: "Level_Year",
                schema: "public",
                table: "Stages",
                newName: "LevelId");

            migrationBuilder.RenameColumn(
                name: "Level_Year",
                schema: "public",
                table: "Registrations",
                newName: "LevelId");

            migrationBuilder.AddColumn<string>(
                name: "AcademicProgram",
                schema: "public",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Level",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    AcademicProgram = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Level", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stages_LevelId",
                schema: "public",
                table: "Stages",
                column: "LevelId");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_LevelId",
                schema: "public",
                table: "Registrations",
                column: "LevelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Registrations_Level_LevelId",
                schema: "public",
                table: "Registrations",
                column: "LevelId",
                principalSchema: "public",
                principalTable: "Level",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stages_Level_LevelId",
                schema: "public",
                table: "Stages",
                column: "LevelId",
                principalSchema: "public",
                principalTable: "Level",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Registrations_Level_LevelId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropForeignKey(
                name: "FK_Stages_Level_LevelId",
                schema: "public",
                table: "Stages");

            migrationBuilder.DropTable(
                name: "Level",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_Stages_LevelId",
                schema: "public",
                table: "Stages");

            migrationBuilder.DropIndex(
                name: "IX_Registrations_LevelId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "AcademicProgram",
                schema: "public",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "WorkPlace",
                schema: "public",
                table: "Users",
                newName: "Specialty");

            migrationBuilder.RenameColumn(
                name: "LevelId",
                schema: "public",
                table: "Stages",
                newName: "Level_Year");

            migrationBuilder.RenameColumn(
                name: "LevelId",
                schema: "public",
                table: "Registrations",
                newName: "Level_Year");

            migrationBuilder.AddColumn<int>(
                name: "Level_AcademicProgram",
                schema: "public",
                table: "Stages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Level_Id",
                schema: "public",
                table: "Stages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Level_Label",
                schema: "public",
                table: "Stages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Level_AcademicProgram",
                schema: "public",
                table: "Registrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Level_Id",
                schema: "public",
                table: "Registrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Level_Label",
                schema: "public",
                table: "Registrations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
