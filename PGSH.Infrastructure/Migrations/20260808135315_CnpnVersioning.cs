using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <summary>
    /// Moves the CNPN off the academic year and onto the text that issued it.
    ///
    /// <para>Arrêté 1650.25 (BO 7422, 17 July 2025) took the Médecine doctorate from seven years to
    /// six with effect from 2024-2025, while article 2 leaves everyone registered before that year
    /// under the previous text. From 2026-2027 a single (level, year) therefore holds students of two
    /// texts, which the old unique index on (LevelId, AcademicYearId) cannot represent.</para>
    ///
    /// <para>The data move is not a rename. Every recorded curriculum is attributed to the text that
    /// governed the intake which reached its level — several years collapse onto one version — and
    /// their stage sets are <b>unioned</b>, because the reconstruction they came from under-reports:
    /// a stage the text required but which no group ran that year left no trace. Students are then
    /// stamped from their earliest recorded registration, falling back to an entry deduced from the
    /// level they sit in now, with the fallback flagged for scolarité.</para>
    /// </summary>
    public partial class CnpnVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CnpnVersions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AcademicProgram = table.Column<string>(type: "text", nullable: false),
                    TotalYears = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AppliesToEntrantsFromAcademicYearId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CnpnVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CnpnVersions_AcademicYears_AppliesToEntrantsFromAcademicYea~",
                        column: x => x.AppliesToEntrantsFromAcademicYearId,
                        principalSchema: "public",
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CnpnVersion_Program_Code",
                schema: "public",
                table: "CnpnVersions",
                columns: new[] { "AcademicProgram", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CnpnVersions_AppliesToEntrantsFromAcademicYearId",
                schema: "public",
                table: "CnpnVersions",
                column: "AppliesToEntrantsFromAcademicYearId");

            // ── The texts ────────────────────────────────────────────────────────────────────────
            // 2175.22 is recorded with no intake year on purpose: 1650.25 art. 2 sends pre-2024-2025
            // students back to 2174.18 in its *pre-amendment* form, so the 2022 amendment governs
            // nobody going forward. It is kept so the citation resolves, never selected.
            migrationBuilder.Sql("""
                INSERT INTO public."CnpnVersions"
                    ("Code", "Label", "AcademicProgram", "TotalYears", "Reference",
                     "AppliesToEntrantsFromAcademicYearId")
                VALUES
                    ('2174.18', 'CNPN 2019 — Docteur en Médecine (7 ans)', 'Medecine', 7,
                     'Arrêté 2174.18 du 2 joumada I 1440 (9 janvier 2019), dans sa version antérieure à l''arrêté 2175.22',
                     (SELECT "Id" FROM public."AcademicYears" ORDER BY "StartDate" LIMIT 1)),
                    ('2175.22', 'CNPN 2022 — amendement (écarté par l''arrêté 1650.25)', 'Medecine', 7,
                     'Arrêté 2175.22 du 6 moharrem 1444 (4 août 2022)',
                     NULL),
                    ('1650.25', 'CNPN 2025 — Docteur en Médecine (6 ans)', 'Medecine', 6,
                     'Arrêté 1650.25 du 29 hija 1446 (26 juin 2025), BO 7422 du 17 juillet 2025',
                     (SELECT "Id" FROM public."AcademicYears" WHERE "Label" = '2024-2025')),
                    ('PHARM-LEGACY', 'CNPN Pharmacie (texte en vigueur)', 'Pharmacie', 6,
                     'À renseigner — texte ministériel non encore saisi',
                     (SELECT "Id" FROM public."AcademicYears" ORDER BY "StartDate" LIMIT 1));
                """);

            // ── Curricula: attribute, merge, then re-point ───────────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "CnpnVersionId",
                schema: "public",
                table: "Curriculums",
                type: "integer",
                nullable: true);

            // The intake that reached (level, year) entered (level - 1) years earlier; the governing
            // text is the latest one whose AppliesToEntrantsFrom is at or before that entry.
            migrationBuilder.Sql("""
                WITH ordered AS (
                    SELECT "Id", ROW_NUMBER() OVER (ORDER BY "StartDate") AS pos
                    FROM public."AcademicYears"
                ),
                entry AS (
                    SELECT c."Id" AS curriculum_id,
                           l."AcademicProgram" AS program,
                           COALESCE(
                               (SELECT o2."Id" FROM ordered o2
                                WHERE o2.pos = GREATEST(1, o.pos - GREATEST(0, l."Year" - 1))),
                               c."AcademicYearId") AS entry_year_id
                    FROM public."Curriculums" c
                    JOIN public."Levels" l ON l."Id" = c."LevelId"
                    JOIN ordered o ON o."Id" = c."AcademicYearId"
                )
                UPDATE public."Curriculums" c
                SET "CnpnVersionId" = (
                    SELECT v."Id"
                    FROM public."CnpnVersions" v
                    JOIN public."AcademicYears" vy ON vy."Id" = v."AppliesToEntrantsFromAcademicYearId"
                    JOIN public."AcademicYears" ey ON ey."Id" = e.entry_year_id
                    WHERE v."AcademicProgram" = e.program
                      AND vy."StartDate" <= ey."StartDate"
                    ORDER BY vy."StartDate" DESC
                    LIMIT 1)
                FROM entry e
                WHERE e.curriculum_id = c."Id";
                """);

            // Several years now share a (version, level). Keep the lowest Id as the survivor, move
            // every stage entry onto it, and take the heaviest coefficient/duration where they
            // disagree — a text that reweighted a stage upward is better represented by the larger
            // figure than by whichever row happened to sort first.
            // Rebuilt rather than shuffled: moving entries onto the survivor one row at a time trips
            // the unique index the moment two years both required the same stage, which is the normal
            // case. Compute the merged set first, clear the group, lay it back down.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE cnpn_survivor AS
                SELECT "CnpnVersionId", "LevelId", MIN("Id") AS keep_id
                FROM public."Curriculums"
                WHERE "CnpnVersionId" IS NOT NULL
                GROUP BY "CnpnVersionId", "LevelId";

                CREATE TEMP TABLE cnpn_merged AS
                SELECT s.keep_id                    AS curriculum_id,
                       cs."StageId"                 AS stage_id,
                       MAX(cs."Coefficient")        AS coefficient,
                       MAX(cs."DurationInDays")     AS duration_in_days
                FROM public."CurriculumStages" cs
                JOIN public."Curriculums" c ON c."Id" = cs."CurriculumId"
                JOIN cnpn_survivor s
                  ON s."CnpnVersionId" = c."CnpnVersionId" AND s."LevelId" = c."LevelId"
                GROUP BY s.keep_id, cs."StageId";

                DELETE FROM public."CurriculumStages" cs
                USING public."Curriculums" c
                WHERE cs."CurriculumId" = c."Id" AND c."CnpnVersionId" IS NOT NULL;

                INSERT INTO public."CurriculumStages"
                    ("CurriculumId", "StageId", "Coefficient", "DurationInDays")
                SELECT curriculum_id, stage_id, coefficient, duration_in_days FROM cnpn_merged;

                DELETE FROM public."Curriculums" c
                USING cnpn_survivor s
                WHERE c."CnpnVersionId" = s."CnpnVersionId"
                  AND c."LevelId" = s."LevelId"
                  AND c."Id" <> s.keep_id;

                DROP TABLE cnpn_merged;
                DROP TABLE cnpn_survivor;

                -- Anything still unattributed predates every recorded text and cannot be placed.
                DELETE FROM public."Curriculums" WHERE "CnpnVersionId" IS NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Curriculums_AcademicYears_AcademicYearId",
                schema: "public",
                table: "Curriculums");

            migrationBuilder.DropIndex(
                name: "IX_Curriculum_Level_Year",
                schema: "public",
                table: "Curriculums");

            migrationBuilder.DropIndex(
                name: "IX_Curriculums_AcademicYearId",
                schema: "public",
                table: "Curriculums");

            migrationBuilder.DropColumn(
                name: "AcademicYearId",
                schema: "public",
                table: "Curriculums");

            migrationBuilder.AlterColumn<int>(
                name: "CnpnVersionId",
                schema: "public",
                table: "Curriculums",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Curriculum_Version_Level",
                schema: "public",
                table: "Curriculums",
                columns: new[] { "CnpnVersionId", "LevelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Curriculums_LevelId",
                schema: "public",
                table: "Curriculums",
                column: "LevelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Curriculums_CnpnVersions_CnpnVersionId",
                schema: "public",
                table: "Curriculums",
                column: "CnpnVersionId",
                principalSchema: "public",
                principalTable: "CnpnVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // ── Students: stamp from entry ───────────────────────────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "CnpnVersionId",
                schema: "public",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CnpnAssignmentIsInferred",
                schema: "public",
                table: "Users",
                type: "boolean",
                nullable: true);

            // A first registration at level 1 is a genuine entry; at a higher level it is only the
            // earliest year on record, so the real entry is (level - 1) years before it and the
            // assignment is flagged inferred.
            migrationBuilder.Sql("""
                WITH ordered AS (
                    SELECT "Id", "StartDate", ROW_NUMBER() OVER (ORDER BY "StartDate") AS pos
                    FROM public."AcademicYears"
                ),
                first_reg AS (
                    SELECT DISTINCT ON (r."StudentId")
                           r."StudentId", o.pos, l."Year" AS first_level, l."AcademicProgram" AS program
                    FROM public."Registrations" r
                    JOIN ordered o ON o."Id" = r."AcademicYearId"
                    JOIN public."Levels" l ON l."Id" = r."LevelId"
                    ORDER BY r."StudentId", o.pos
                ),
                entry AS (
                    SELECT f."StudentId", f.program, f.first_level <= 1 AS recorded,
                           (SELECT o2."StartDate" FROM ordered o2
                            WHERE o2.pos = GREATEST(1, f.pos - GREATEST(0, f.first_level - 1))) AS entry_start
                    FROM first_reg f
                )
                UPDATE public."Users" u
                SET "CnpnVersionId" = (
                        SELECT v."Id"
                        FROM public."CnpnVersions" v
                        JOIN public."AcademicYears" vy ON vy."Id" = v."AppliesToEntrantsFromAcademicYearId"
                        WHERE v."AcademicProgram" = e.program
                          AND vy."StartDate" <= e.entry_start
                        ORDER BY vy."StartDate" DESC
                        LIMIT 1),
                    "CnpnAssignmentIsInferred" = NOT e.recorded
                FROM entry e
                WHERE u."Id" = e."StudentId" AND u."UserType" = 'Student';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_CnpnVersionId",
                schema: "public",
                table: "Users",
                column: "CnpnVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_CnpnVersions_CnpnVersionId",
                schema: "public",
                table: "Users",
                column: "CnpnVersionId",
                principalSchema: "public",
                principalTable: "CnpnVersions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down restores the shape, not the data: the forward merge collapsed several years onto
            // one text and that cannot be undone. Re-run the history reconstruction after reverting.
            migrationBuilder.DropForeignKey(
                name: "FK_Curriculums_CnpnVersions_CnpnVersionId",
                schema: "public",
                table: "Curriculums");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_CnpnVersions_CnpnVersionId",
                schema: "public",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_CnpnVersionId",
                schema: "public",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Curriculum_Version_Level",
                schema: "public",
                table: "Curriculums");

            migrationBuilder.DropIndex(
                name: "IX_Curriculums_LevelId",
                schema: "public",
                table: "Curriculums");

            migrationBuilder.DropColumn(
                name: "CnpnAssignmentIsInferred",
                schema: "public",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CnpnVersionId",
                schema: "public",
                table: "Users");

            migrationBuilder.DropTable(
                name: "CnpnVersions",
                schema: "public");

            migrationBuilder.RenameColumn(
                name: "CnpnVersionId",
                schema: "public",
                table: "Curriculums",
                newName: "AcademicYearId");

            migrationBuilder.Sql("""
                UPDATE public."Curriculums"
                SET "AcademicYearId" = (SELECT "Id" FROM public."AcademicYears" WHERE "IsCurrent" LIMIT 1);
                """);

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

            migrationBuilder.AddForeignKey(
                name: "FK_Curriculums_AcademicYears_AcademicYearId",
                schema: "public",
                table: "Curriculums",
                column: "AcademicYearId",
                principalSchema: "public",
                principalTable: "AcademicYears",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
