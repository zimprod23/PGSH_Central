using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <summary>
    /// « L'année en cours » becomes a singleton the database enforces.
    ///
    /// <para><c>AcademicYearResolver</c> takes the <b>first</b> row flagged <c>IsCurrent</c>, and every
    /// handler that omits a year gets whatever that returns. Two rows flagged at once therefore means
    /// two screens quietly disagreeing about which promotion they are showing, with nothing on either
    /// to say so. <c>CreateAcademicYearCommandHandler</c> demotes the others, but that is one write
    /// path guarding an invariant of the table.</para>
    ///
    /// <para>The <c>UPDATE</c> below is a repair, not a policy: it should touch zero rows, because the
    /// only writer already demotes. It is here so that a base which somehow holds two current years
    /// gets migrated instead of failing at startup, and it keeps the most recently created one —
    /// which is the one <c>CreateAcademicYear</c> would have left standing.</para>
    /// </summary>
    public partial class PartitionScopeAndIndexGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public."AcademicYears"
                SET "IsCurrent" = false
                WHERE "IsCurrent" AND "Id" <> (
                    SELECT MAX("Id") FROM public."AcademicYears" WHERE "IsCurrent");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AcademicYear_IsCurrent",
                schema: "public",
                table: "AcademicYears",
                column: "IsCurrent",
                unique: true,
                filter: "\"IsCurrent\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The demotion is not undone: a year that was wrongly current is not something to restore.
            migrationBuilder.DropIndex(
                name: "IX_AcademicYear_IsCurrent",
                schema: "public",
                table: "AcademicYears");
        }
    }
}
