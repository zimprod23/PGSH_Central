using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <summary>
    /// A student may have no CNE, and the ~4 700 imported students who have none stop pretending to.
    ///
    /// <para><b>What was wrong.</b> <c>Student.CNE</c> was required, and the Access base records a
    /// national code for only 5 510 of its 10 203 students — so <c>LegacyIdentityMapper</c>
    /// manufactured <c>LEGACY-{NO_ORDRE}</c> for the other 4 695. That value is not marked in the
    /// schema, not marked in any response, and reads in every list, every export, every déliberation
    /// canvas and every évaluation-import match exactly like a code somebody holds. <b>46% of the
    /// roll carried one.</b></para>
    ///
    /// <para>⚠ <b>The column itself was already nullable</b> — <c>Users</c> is a TPH table and an
    /// <c>Employee</c> has no CNE — so the requirement lived only in EF's model and in the
    /// validators. The one thing the database really enforced was <c>IX_Student_CNE</c>, unique and
    /// <em>unfiltered</em>. Postgres treats NULLs as distinct, so that index already tolerated any
    /// number of students without a code; the filter is added so the index says what it means and
    /// stops carrying 4 700 rows it can never match on.</para>
    ///
    /// <para><b>The placeholders are cleared here rather than by the re-import</b>, because the two
    /// are independent: a base that is not being rebuilt from the .mdb would otherwise keep them
    /// forever, and a base that is loses nothing — <c>Appogee</c> carries <c>NO_ORDRE</c> verbatim
    /// for every one of those rows, so no student becomes unidentifiable. Guarded on the prefix, so
    /// a real code that merely begins with a word is untouched: the manufactured form is
    /// <c>LEGACY-</c> followed by digits and nothing else.</para>
    /// </summary>
    public partial class StudentCneOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Student_CNE",
                schema: "public",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Student_CNE",
                schema: "public",
                table: "Users",
                column: "CNE",
                unique: true,
                filter: "\"CNE\" IS NOT NULL");

            // The index is created before this runs, which is the safe order: clearing first would
            // leave the old unfiltered unique index momentarily holding 4 695 NULLs, and while
            // Postgres accepts that, an ordering that only works by accident is not one to rely on.
            migrationBuilder.Sql("""
                UPDATE public."Users"
                SET    "CNE" = NULL
                WHERE  "UserType" = 'Student'
                  AND  "CNE" ~ '^LEGACY-[0-9]+$';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ⚠ Down restores the shape, not the data. The placeholders were derived from NO_ORDRE,
            // which is Appogee, so they can be rebuilt — and they have to be, or the unfiltered
            // unique index below would be created over thousands of NULLs and the model's
            // required-ness would no longer describe the rows.
            migrationBuilder.Sql("""
                UPDATE public."Users"
                SET    "CNE" = 'LEGACY-' || "Appogee"
                WHERE  "UserType" = 'Student'
                  AND  "CNE" IS NULL
                  AND  "Appogee" ~ '^[0-9]+$';
                """);

            migrationBuilder.DropIndex(
                name: "IX_Student_CNE",
                schema: "public",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Student_CNE",
                schema: "public",
                table: "Users",
                column: "CNE",
                unique: true);
        }
    }
}
