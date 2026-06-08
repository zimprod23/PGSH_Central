using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EvaluationModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TotalScore",
                schema: "public",
                table: "ServiceEvaluation",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                schema: "public",
                table: "ServiceEvaluation",
                type: "text",
                nullable: false,
                defaultValue: "Numeric");

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                schema: "public",
                table: "ServiceEvaluation",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Score",
                schema: "public",
                table: "ObjectiveScores",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                schema: "public",
                table: "ObjectiveScores",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                schema: "public",
                table: "ServiceEvaluation");

            migrationBuilder.DropColumn(
                name: "Outcome",
                schema: "public",
                table: "ServiceEvaluation");

            migrationBuilder.DropColumn(
                name: "Outcome",
                schema: "public",
                table: "ObjectiveScores");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalScore",
                schema: "public",
                table: "ServiceEvaluation",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Score",
                schema: "public",
                table: "ObjectiveScores",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
