using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <summary>
    /// Gives the CNPN texts back the intake year they are selected by — the fourth thing a rebuild
    /// from the Access file silently loses, and the worst of them.
    ///
    /// <para><b>What goes wrong.</b> <c>CnpnVersioning</c> inserts the four texts with their
    /// <c>AppliesToEntrantsFromAcademicYearId</c> read out of <c>AcademicYears</c>:
    /// <c>(SELECT "Id" FROM "AcademicYears" ORDER BY "StartDate" LIMIT 1)</c> for 2174.18 and
    /// PHARM-LEGACY, and the row labelled <c>2024-2025</c> for 1650.25. On a database rebuilt from
    /// the .mdb that migration runs <em>before</em> the import, so the table is empty, every subselect
    /// yields NULL, and all four texts are stored with no intake year at all.</para>
    ///
    /// <para>⚠ <b>And a text with no intake year is not broken — it is <i>citation-only</i>.</b> That
    /// is a real, deliberate state: arrêté 2175.22 is exactly this, kept so the reference resolves and
    /// never selected. So nothing is malformed, nothing throws, and
    /// <c>CnpnAssignment.SelectVersionAsync</c> simply finds no candidate for anybody. Measured on the
    /// 2026-09-01 rebuild: <b>10 185 of 10 185 students unresolved, 0 stamped</b> — reported as a
    /// count, in a pass that returned success.</para>
    ///
    /// <para><b>Idempotent, and keyed on the code.</b> Only a text whose intake year is <c>NULL</c> is
    /// touched, and only the three that are meant to have one. ⚠ <b>2175.22 is deliberately left
    /// alone</b> — filling it in would make the amendment selectable, which is precisely what arrêté
    /// 1650.25 art. 2 sends pre-2024-2025 students away from.</para>
    ///
    /// <para>Where <c>AcademicYears</c> is still empty this does nothing, which is right: it is then
    /// running in the same position <c>CnpnVersioning</c> was, and the import has yet to create the
    /// years. The rebuild runs it after the import (step 4 of <c>PHASES.md</c> §16.5), and
    /// <c>CnpnHistoryAttributor</c> now refuses rather than reporting a total no-op if it is skipped.</para>
    /// </summary>
    public partial class CnpnIntakeYearsBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- The two texts governing everyone who entered before the six-year reform: the
                -- earliest year on record, which is what CnpnVersioning meant by « from the start ».
                UPDATE public."CnpnVersions"
                SET    "AppliesToEntrantsFromAcademicYearId" =
                           (SELECT "Id" FROM public."AcademicYears" ORDER BY "StartDate" LIMIT 1)
                WHERE  "Code" IN ('2174.18', 'PHARM-LEGACY')
                  AND  "AppliesToEntrantsFromAcademicYearId" IS NULL
                  AND  EXISTS (SELECT 1 FROM public."AcademicYears");

                -- Arrêté 1650.25 takes Médecine from seven years to six « with effect from
                -- 2024-2025 », and art. 2 leaves everyone registered before that year under the
                -- previous text. The year is named, not positional.
                UPDATE public."CnpnVersions"
                SET    "AppliesToEntrantsFromAcademicYearId" =
                           (SELECT "Id" FROM public."AcademicYears" WHERE "Label" = '2024-2025')
                WHERE  "Code" = '1650.25'
                  AND  "AppliesToEntrantsFromAcademicYearId" IS NULL
                  AND  EXISTS (SELECT 1 FROM public."AcademicYears" WHERE "Label" = '2024-2025');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ⚠ Deliberately empty. Down would have to clear an intake year — and it cannot tell one
            // this migration filled from one CnpnVersioning got right on a base that was not rebuilt,
            // nor from one scolarité has since corrected by hand. Clearing the wrong one turns a
            // governing text into a citation and unstamps a faculty, silently. There is nothing to
            // undo that is worth that risk.
        }
    }
}
