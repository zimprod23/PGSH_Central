using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CurriculumCnpn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Curriculums",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LevelId = table.Column<int>(type: "integer", nullable: false),
                    AcademicYearId = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Curriculums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Curriculums_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalSchema: "public",
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Curriculums_Levels_LevelId",
                        column: x => x.LevelId,
                        principalSchema: "public",
                        principalTable: "Levels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumStages",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CurriculumId = table.Column<int>(type: "integer", nullable: false),
                    StageId = table.Column<int>(type: "integer", nullable: false),
                    Coefficient = table.Column<int>(type: "integer", nullable: false),
                    DurationInDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumStages_Curriculums_CurriculumId",
                        column: x => x.CurriculumId,
                        principalSchema: "public",
                        principalTable: "Curriculums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CurriculumStages_Stages_StageId",
                        column: x => x.StageId,
                        principalSchema: "public",
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Curriculum_Level_Year",
                schema: "public",
                table: "Curriculums",
                columns: new[] { "LevelId", "AcademicYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Curriculums_AcademicYearId",
                schema: "public",
                table: "Curriculums",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumStage_Curriculum_Stage",
                schema: "public",
                table: "CurriculumStages",
                columns: new[] { "CurriculumId", "StageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumStages_StageId",
                schema: "public",
                table: "CurriculumStages",
                column: "StageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurriculumStages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Curriculums",
                schema: "public");
        }
    }
}
