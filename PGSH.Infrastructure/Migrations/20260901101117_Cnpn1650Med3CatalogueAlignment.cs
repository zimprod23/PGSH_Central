using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <summary>
    /// Brings Chirurgie and Médecine into line with what arrêté 1650.25 says of them — <b>after</b>
    /// making sure the previous text's figures survive somewhere that can still state them.
    ///
    /// <para><b>What was wrong.</b> <c>Cnpn1650Med3Stages</c> created the four stages the arrêté
    /// brings down from the 4ᵉ année at 30 jours ouvrables and <c>SingleService</c>, and reused the
    /// two the 3ᵉ année already had. Reusing them was right — duplicating them would break the
    /// revalidation path for 4ᵉ/5ᵉ/6ᵉ année students still carrying a credit — but their own
    /// catalogue row was left saying <b>66 days</b> and <b>PerPeriod</b>, which is what the Stages
    /// page shows and what the rotation-cycle preview measures a column against.</para>
    ///
    /// <para>The duration is duplicated and the mode is not, so the two halves fail differently:</para>
    /// <list type="bullet">
    ///   <item><b>Duration</b> lives on the catalogue <i>and</i> on every text's
    ///   <c>CurriculumStage</c>. 1650.25 already records 30. The stale 66 is therefore a display and
    ///   reporting fault — <c>PreviewRotationCycleQuery.DurationChecks</c> would measure a six-week
    ///   column against 66 and call Chirurgie badly under-served. It reports, it never guards, so
    ///   nothing was blocked; it was simply wrong.</item>
    ///   <item><b><c>RotationMode</c> exists only on <c>Stage</c></b> — there is no per-text mode —
    ///   so <c>PerPeriod</c> is live and authoritative. It would make <c>RotationArranger</c> advance
    ///   the service between columns and <c>SchedulePublisher</c> write one <c>ServicePeriod</c> per
    ///   column instead of collapsing the run, giving the chef one evaluation per column. Latent
    ///   only while each stage holds a single column, where the two modes are indistinguishable.</item>
    /// </list>
    ///
    /// <para>⚠ <b>The old text's numbers are recorded before the catalogue is overwritten.</b> Once
    /// the catalogue says 30, the only place 66 can still be read is 2174.18's own requirement set —
    /// and students in the 4ᵉ, 5ᵉ and 6ᵉ années are still governed by that text and can still carry a
    /// 3ᵉ année credit under it. So the pre-existing stages are written into the 2174.18 / 3ᵉ année
    /// curriculum at their <i>current</i> catalogue figures first, and only rows that are missing are
    /// added — an authored value is never overwritten. Where that curriculum does not exist it is
    /// created, which is the same reconstruction-from-execution <c>CurriculumHistoryReconstructor</c>
    /// performed for the six imported years.</para>
    ///
    /// <para>⚠ <b>It will not do by SQL what the application refuses through the UI.</b>
    /// <c>UpdateStageCommandHandler</c> refuses a mode change on a stage carrying published grid
    /// coverage (<c>Stages.RotationModeLockedByPublication</c>) because the périodes on disk were
    /// shaped by the old mode. This migration raises on exactly the same condition, naming the stage,
    /// rather than quietly stepping around a guard somebody wrote on purpose. Unpublish first — that
    /// path states what it costs.</para>
    /// </summary>
    public partial class Cnpn1650Med3CatalogueAlignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    med3      integer;
                    cnpn2174  integer;
                    oldCurric integer;
                    locked    text;
                    unsaved   text;
                BEGIN
                    SELECT "Id" INTO med3
                    FROM public."Levels"
                    WHERE "Year" = 3 AND "AcademicProgram" = 'Medecine';

                    IF med3 IS NULL THEN
                        RAISE EXCEPTION 'Aucun niveau « 3ᵉ année Médecine ».';
                    END IF;

                    -- The stages the 3ᵉ année already held: everything at that level except the four
                    -- Cnpn1650Med3Stages brought down. Named the same way there, so the two agree.
                    CREATE TEMP TABLE _carried ON COMMIT DROP AS
                    SELECT s."Id", s."Name", s."Coefficient", s."DurationInDays", s."RotationMode"
                    FROM public."Stages" s
                    WHERE s."LevelId" = med3
                      AND s."Name" NOT IN ('Cardiologie', 'Pneumologie',
                                           'Dermatologie - Endocrinologie', 'Rhumatologie - Radiologie');

                    -- 1. Refuse before writing anything, on the application's own condition — and
                    --    only for the half that is guarded. A duration change is never locked; the
                    --    mode is, and only when it is actually changing.
                    SELECT string_agg(c."Name", ', ' ORDER BY c."Name") INTO locked
                    FROM _carried c
                    WHERE c."RotationMode" <> 'SingleService'
                      AND EXISTS (
                          SELECT 1
                          FROM public."ServicePeriodSlotCoverage" cov
                          JOIN public."CohortSlotAssignments" csa ON csa."Id" = cov."CohortSlotAssignmentId"
                          JOIN public."StageSlots" sl ON sl."Id" = csa."StageSlotId"
                          WHERE sl."StageId" = c."Id");

                    IF locked IS NOT NULL THEN
                        RAISE EXCEPTION
                            'Le mode de rotation de % est verrouillé : des périodes issues de la grille sont déjà publiées sur ce stage. Dépubliez-les avant d''appliquer (cette action annonce ce qu''elle coûte).',
                            locked;
                    END IF;

                    -- 2. Preserve the previous text's figures. Only where 2174.18 governs this level.
                    SELECT "Id" INTO cnpn2174
                    FROM public."CnpnVersions"
                    WHERE "Code" = '2174.18';

                    IF cnpn2174 IS NOT NULL THEN
                        SELECT "Id" INTO oldCurric
                        FROM public."Curriculums"
                        WHERE "CnpnVersionId" = cnpn2174 AND "LevelId" = med3;

                        IF oldCurric IS NULL THEN
                            INSERT INTO public."Curriculums" ("CnpnVersionId", "LevelId", "Reference")
                            VALUES (cnpn2174, med3,
                                    'Arrêté 2174.18 — 3ᵉ année (reconstruit du catalogue avant l''alignement sur 1650.25)')
                            RETURNING "Id" INTO oldCurric;
                        END IF;

                        -- Missing rows only: a figure somebody authored is never overwritten by the
                        -- catalogue's.
                        INSERT INTO public."CurriculumStages"
                            ("CurriculumId", "StageId", "Coefficient", "DurationInDays")
                        SELECT oldCurric, c."Id", c."Coefficient", c."DurationInDays"
                        FROM _carried c
                        WHERE NOT EXISTS (
                            SELECT 1 FROM public."CurriculumStages" cs
                            WHERE cs."CurriculumId" = oldCurric AND cs."StageId" = c."Id");

                        SELECT string_agg(c."Name", ', ' ORDER BY c."Name") INTO unsaved
                        FROM _carried c
                        WHERE NOT EXISTS (
                            SELECT 1 FROM public."CurriculumStages" cs
                            WHERE cs."CurriculumId" = oldCurric AND cs."StageId" = c."Id");

                        IF unsaved IS NOT NULL THEN
                            RAISE EXCEPTION
                                'La durée de % n''a pas pu être conservée dans le CNPN 2174.18 ; le catalogue n''est pas écrasé.',
                                unsaved;
                        END IF;
                    END IF;

                    -- 3. Only now, align the catalogue on what 1650.25 says: six semaines de jours
                    --    ouvrables, un seul service pour toute la durée.
                    UPDATE public."Stages" s
                    SET "DurationInDays" = 30,
                        "RotationMode"   = 'SingleService'
                    FROM _carried c
                    WHERE s."Id" = c."Id";
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the catalogue from the figures preserved above — the record of what the
            // previous text required is exactly what makes this reversible. ⚠ The *mode* is not
            // recoverable that way: no text carries one, so it can only be put back to the value
            // these two rows held (PerPeriod). Exact for the case this migration exists for, and
            // stated rather than pretended.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    med3     integer;
                    cnpn2174 integer;
                BEGIN
                    SELECT "Id" INTO med3
                    FROM public."Levels"
                    WHERE "Year" = 3 AND "AcademicProgram" = 'Medecine';

                    SELECT "Id" INTO cnpn2174
                    FROM public."CnpnVersions"
                    WHERE "Code" = '2174.18';

                    IF med3 IS NULL OR cnpn2174 IS NULL THEN
                        RETURN;
                    END IF;

                    UPDATE public."Stages" s
                    SET "DurationInDays" = cs."DurationInDays",
                        "Coefficient"    = cs."Coefficient",
                        "RotationMode"   = 'PerPeriod'
                    FROM public."CurriculumStages" cs
                    JOIN public."Curriculums" c ON c."Id" = cs."CurriculumId"
                    WHERE c."CnpnVersionId" = cnpn2174
                      AND c."LevelId" = med3
                      AND s."Id" = cs."StageId"
                      AND s."LevelId" = med3
                      AND s."Name" NOT IN ('Cardiologie', 'Pneumologie',
                                           'Dermatologie - Endocrinologie', 'Rhumatologie - Radiologie');
                END $$;
                """);
        }
    }
}
