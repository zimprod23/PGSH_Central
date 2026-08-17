using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <summary>
    /// Gives every roster back the promotion it counts within.
    ///
    /// <para>The Access base numbers groups <b>per promotion</b> — the 3rd year runs 1-80, the 5th
    /// year 1-60, the 6th year 1-100, concurrently — but <c>LegacyImportPlanner</c> keyed them on
    /// <c>(ANNEE_UNIV, GROUPE_STG)</c> alone, so all three numberings were folded into one set of
    /// rows. Measured before this migration: <b>80 of the 100 numbered groups of 2025-2026 carried
    /// registrations from four or five different promotions at once</b>, and <c>LevelId</c> was null
    /// on all 1,003 rows in the base.</para>
    ///
    /// <para>That is not a cosmetic mix-up. <c>GroupScheduleConflictGuard</c> forbids one group from
    /// sitting in two services at the same time — correctly, on the premise that a group is one set
    /// of students. With the rows shared, the 3rd year's April–July placements <em>were</em> the 5th
    /// year's, so planning the 5th year was refused on seven of its nine columns and produced a
    /// répartition with two. <c>RotationGroup</c> was shared the same way: one global partitioning
    /// per year, which cutting any one promotion silently re-cut for every other.</para>
    ///
    /// <para>⚠ <b>Partition labels are carried over, not cleared.</b> A label that was right for the
    /// promotion whose cut it was stays right; the others are left exactly as they already were, so
    /// this migration destroys nothing. But one global cut cannot be correct for several promotions
    /// at once, so each level's partitioning has to be re-authored — that is
    /// <c>AssignRotationGroupsCommand</c>'s job, not a migration's.</para>
    ///
    /// <para>« Non réparti » (<c>GroupNumber = 0</c>) is deliberately left alone. It is the bucket for
    /// registrations that belong to no roster, spans every promotion by nature, and carries no
    /// cohorts — splitting it would invent nine rosters nobody is in.</para>
    /// </summary>
    public partial class SplitAcademicGroupsPerLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Must go first: the split deliberately creates several rows per (year, number).
            migrationBuilder.DropIndex(
                name: "IX_AcademicGroup_Year_Number",
                schema: "public",
                table: "AcademicGroups");

            migrationBuilder.Sql("""
                -- Every (roster, promotion) pair that actually exists, read from the registrations
                -- themselves: they are the only record of which promotion a student was in.
                CREATE TEMP TABLE pgsh_group_levels AS
                WITH pairs AS (
                    SELECT DISTINCT r."AcademicGroupId" AS old_group_id, r."LevelId" AS level_id
                    FROM "Registrations" r
                    JOIN "AcademicGroups" g ON g."Id" = r."AcademicGroupId"
                    WHERE g."GroupNumber" > 0
                )
                SELECT old_group_id, level_id,
                       ROW_NUMBER() OVER (PARTITION BY old_group_id ORDER BY level_id) AS rn
                FROM pairs;

                -- The lowest promotion keeps the existing row, so the ids most of the base already
                -- points at stay valid and only the surplus is created.
                UPDATE "AcademicGroups" g
                SET "LevelId" = gl.level_id
                FROM pgsh_group_levels gl
                WHERE gl.old_group_id = g."Id" AND gl.rn = 1;

                -- One new roster per remaining promotion, keeping the number: that is the point —
                -- the 3rd year's Groupe 1 and the 5th year's are both Groupe 1 from now on.
                INSERT INTO "AcademicGroups"
                    ("Label", "GroupNumber", "GeographicZone", "AcademicYearId", "RotationGroup", "LevelId")
                SELECT 'Groupe ' || g."GroupNumber" || ' — ' || COALESCE(l."Label", 'Niveau ' || l."Id"),
                       g."GroupNumber", g."GeographicZone", g."AcademicYearId", g."RotationGroup", gl.level_id
                FROM pgsh_group_levels gl
                JOIN "AcademicGroups" g ON g."Id" = gl.old_group_id
                JOIN "Levels" l ON l."Id" = gl.level_id
                WHERE gl.rn > 1;

                -- Recovered by (year, number, promotion) rather than by RETURNING, which cannot give
                -- back the row each insert came from.
                CREATE TEMP TABLE pgsh_group_map AS
                SELECT gl.old_group_id, gl.level_id, ng."Id" AS new_group_id
                FROM pgsh_group_levels gl
                JOIN "AcademicGroups" og ON og."Id" = gl.old_group_id
                JOIN "AcademicGroups" ng
                  ON ng."AcademicYearId" = og."AcademicYearId"
                 AND ng."GroupNumber"    = og."GroupNumber"
                 AND ng."LevelId"        = gl.level_id
                WHERE gl.rn > 1 AND ng."Id" <> og."Id";

                UPDATE "Registrations" r
                SET "AcademicGroupId" = m.new_group_id
                FROM pgsh_group_map m
                WHERE r."AcademicGroupId" = m.old_group_id AND r."LevelId" = m.level_id;

                -- A cohort is (roster × stage) and a stage belongs to one promotion, so a cohort
                -- follows the promotion of its stage. Its cells and memberships hang off it and need
                -- no move of their own.
                UPDATE "Cohorts" c
                SET "AcademicGroupId" = m.new_group_id
                FROM pgsh_group_map m, "Stages" s
                WHERE c."AcademicGroupId" = m.old_group_id
                  AND s."Id" = c."StageId"
                  AND s."LevelId" = m.level_id;

                -- Only the importer's bare « Groupe 5 » is rewritten: it no longer identifies anything
                -- on its own now that five promotions have one. Labels authored elsewhere are left
                -- alone — auto-arrange puts the CNPN code in some of them, and that is not ours to drop.
                UPDATE "AcademicGroups" g
                SET "Label" = 'Groupe ' || g."GroupNumber" || ' — ' || COALESCE(l."Label", 'Niveau ' || l."Id")
                FROM "Levels" l
                WHERE l."Id" = g."LevelId"
                  AND g."GroupNumber" > 0
                  AND g."Label" = 'Groupe ' || g."GroupNumber";

                DROP TABLE pgsh_group_levels;
                DROP TABLE pgsh_group_map;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroup_Year_Level_Number",
                schema: "public",
                table: "AcademicGroups",
                columns: new[] { "AcademicYearId", "LevelId", "GroupNumber" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademicGroup_Year_Level_Number",
                schema: "public",
                table: "AcademicGroups");

            // Merging back is what the old index requires, and it is faithful to the structure if not
            // to the labels: everything sharing (year, number) collapses onto the lowest id, which is
            // the row that was there before the split.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE pgsh_unsplit AS
                SELECT g."Id" AS old_id, k.keep_id
                FROM "AcademicGroups" g
                JOIN (SELECT "AcademicYearId", "GroupNumber", MIN("Id") AS keep_id
                      FROM "AcademicGroups"
                      GROUP BY "AcademicYearId", "GroupNumber") k
                  ON k."AcademicYearId" = g."AcademicYearId" AND k."GroupNumber" = g."GroupNumber"
                WHERE g."Id" <> k.keep_id;

                UPDATE "Registrations" r SET "AcademicGroupId" = u.keep_id
                FROM pgsh_unsplit u WHERE r."AcademicGroupId" = u.old_id;

                UPDATE "Cohorts" c SET "AcademicGroupId" = u.keep_id
                FROM pgsh_unsplit u WHERE c."AcademicGroupId" = u.old_id;

                DELETE FROM "AcademicGroups" g USING pgsh_unsplit u WHERE g."Id" = u.old_id;

                DROP TABLE pgsh_unsplit;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AcademicGroup_Year_Number",
                schema: "public",
                table: "AcademicGroups",
                columns: new[] { "AcademicYearId", "GroupNumber" },
                unique: true);
        }
    }
}
