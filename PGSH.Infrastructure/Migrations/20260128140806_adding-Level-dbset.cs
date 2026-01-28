using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addingLeveldbset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Registrations_Level_LevelId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropForeignKey(
                name: "FK_Stages_Level_LevelId",
                schema: "public",
                table: "Stages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Level",
                schema: "public",
                table: "Level");

            migrationBuilder.RenameTable(
                name: "Level",
                schema: "public",
                newName: "Levels",
                newSchema: "public");

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                schema: "public",
                table: "Levels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Levels",
                schema: "public",
                table: "Levels",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Registrations_Levels_LevelId",
                schema: "public",
                table: "Registrations",
                column: "LevelId",
                principalSchema: "public",
                principalTable: "Levels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stages_Levels_LevelId",
                schema: "public",
                table: "Stages",
                column: "LevelId",
                principalSchema: "public",
                principalTable: "Levels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Registrations_Levels_LevelId",
                schema: "public",
                table: "Registrations");

            migrationBuilder.DropForeignKey(
                name: "FK_Stages_Levels_LevelId",
                schema: "public",
                table: "Stages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Levels",
                schema: "public",
                table: "Levels");

            migrationBuilder.RenameTable(
                name: "Levels",
                schema: "public",
                newName: "Level",
                newSchema: "public");

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                schema: "public",
                table: "Level",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Level",
                schema: "public",
                table: "Level",
                column: "Id");

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
    }
}
