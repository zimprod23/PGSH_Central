using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <summary>
    /// The 3ᵉ année of arrêté 1650.25 — the first promotion the six-year text actually binds.
    ///
    /// <para><b>What the text does.</b> Médecine goes from seven years to six, so the stages of each
    /// year slide down one: the new 3ᵉ année is the old 4ᵉ année plus the old 3ᵉ année, minus
    /// Pédiatrie. The result is <b>six</b> stages, and two of them are pairings the new text puts
    /// together:</para>
    /// <list type="number">
    ///   <item>Chirurgie — <i>already at the 3ᵉ année</i>;</item>
    ///   <item>Médecine — <i>already at the 3ᵉ année</i>;</item>
    ///   <item>Cardiologie;</item>
    ///   <item>Pneumologie;</item>
    ///   <item>Dermatologie - Endocrinologie — <b>one</b> stage, not two;</item>
    ///   <item>Rhumatologie - Radiologie — <b>one</b> stage, not two.</item>
    /// </list>
    ///
    /// <para>So four rows are created, not six: the first two exist and are reused, and the last two
    /// are pairings that exist nowhere yet. ⚠ The two paired labels are the faculty's wording as
    /// dictated; the punctuation is a guess and is editable from the Stages page without
    /// consequence — nothing keys on a stage name.</para>
    ///
    /// <para><b>The year fits comfortably.</b> Six stages of 30 jours ouvrables is 180 j.o. against
    /// roughly 248 available once weekends and the Moroccan calendar are removed — a 36-week axis of
    /// six columns, lighter than Med6's ten columns of 22 j.o. Nothing has to run concurrently, which
    /// matters because a roster in two services at once is refused by
    /// <c>GroupScheduleConflictGuard</c> and the crossover arithmetic (<c>T = Σkₛ</c>) has no way to
    /// express it.</para>
    ///
    /// <para>⚠ <b>They are new rows, not the old rows repointed, and not shared.</b> In 2026-2027
    /// both promotions run this material at once — the 3ᵉ année under 1650.25 and the 4ᵉ année still
    /// under 2174.18, whose Pédiatrie rotation is already published for that year. A single
    /// <c>Stage</c> row cannot serve both, and not merely as a matter of taste: <c>Stage.LevelId</c>
    /// is what a slot, a cohorte, a curriculum and a répartition are all reached through, so sharing
    /// one row is refused in four places —</para>
    /// <list type="number">
    ///   <item><c>SaveCurriculumCommandHandler</c> refuses a stage whose level is not the
    ///   curriculum's (<c>StageNotInLevel</c>), so the requirement could not even be recorded;</item>
    ///   <item><c>CreateCohortCommandHandler</c> / <c>CohortProvisioner</c> refuse a roster paired
    ///   with a stage of another promotion (<c>CohortPromotionMismatch</c>);</item>
    ///   <item><c>SlotOverlapGuard</c> refuses two overlapping windows of one stage in one year, and
    ///   two promotions running the same stage in the same year necessarily overlap;</item>
    ///   <item><c>StageSlot</c> carries no level — it is keyed (Stage, AcademicYear, PeriodNumber) —
    ///   so the répartition and the rotation cycle reach a stage's columns through
    ///   <c>Stage.LevelId</c>. One shared row would show its columns in one promotion's grid and
    ///   silently drop them from the other's.</item>
    /// </list>
    ///
    /// <para>Repointing the existing rows was rejected for a different reason: <c>Stage</c> is the
    /// timeless catalogue identity that historical <c>InternshipAssignment</c>s hang off, and 4MED's
    /// published 2026-2027 history would change meaning under it. The old rows stay, wind down with
    /// the last 2174.18 promotion, and remain the rows a student with an outstanding credit
    /// revalidates against.</para>
    ///
    /// <para><b>Values.</b> Coefficient 1 throughout — a placeholder the faculty will refine, and it
    /// lives on <c>CurriculumStage</c> where the text's own weight belongs. Duration 30, which is
    /// <b>six weeks of jours ouvrables</b> and follows the catalogue's convention (25 of 27 existing
    /// stages are already stated in worked days). <c>SingleService</c> throughout, stated explicitly
    /// because the enum's default is <c>PerPeriod</c>.</para>
    ///
    /// <para><b>Idempotent.</b> Every insert is guarded, so the migration may be replayed against a
    /// base that already carries part of it — which is how it will be applied if the six stages are
    /// entered by hand first.</para>
    /// </summary>
    public partial class Cnpn1650Med3Stages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    med3     integer;
                    cnpn1650 integer;
                    curric   integer;
                    recorded integer;
                BEGIN
                    -- IX_Level_Year_Program is unique on (Year, AcademicProgram), so this is exact.
                    SELECT "Id" INTO med3
                    FROM public."Levels"
                    WHERE "Year" = 3 AND "AcademicProgram" = 'Medecine';

                    IF med3 IS NULL THEN
                        RAISE EXCEPTION 'Aucun niveau « 3ᵉ année Médecine » : le catalogue des niveaux doit exister avant les stages.';
                    END IF;

                    SELECT "Id" INTO cnpn1650
                    FROM public."CnpnVersions"
                    WHERE "Code" = '1650.25';

                    IF cnpn1650 IS NULL THEN
                        RAISE EXCEPTION 'Aucun CNPN de code 1650.25 : le texte doit être enregistré avant ses exigences.';
                    END IF;

                    -- The four the arrêté brings down from the 4ᵉ année. Chirurgie and Médecine are
                    -- already there and are reused. Guarded on (level, name) rather than on name
                    -- alone: the 4ᵉ année keeps its own rows, and carrying a name at two levels is
                    -- the whole point.
                    INSERT INTO public."Stages"
                        ("Name", "Coefficient", "Description", "DurationInDays", "LevelId", "RotationMode")
                    SELECT v.name, 1, 'Arrêté 1650.25 — stage de 3ᵉ année.', 30, med3, 'SingleService'
                    FROM (VALUES
                        ('Cardiologie'),
                        ('Pneumologie'),
                        ('Dermatologie - Endocrinologie'),
                        ('Rhumatologie - Radiologie')
                    ) AS v(name)
                    WHERE NOT EXISTS (
                        SELECT 1 FROM public."Stages" s
                        WHERE s."LevelId" = med3 AND s."Name" = v.name);

                    SELECT "Id" INTO curric
                    FROM public."Curriculums"
                    WHERE "CnpnVersionId" = cnpn1650 AND "LevelId" = med3;

                    IF curric IS NULL THEN
                        INSERT INTO public."Curriculums" ("CnpnVersionId", "LevelId", "Reference")
                        VALUES (cnpn1650, med3, 'Arrêté 1650.25 — 3ᵉ année')
                        RETURNING "Id" INTO curric;
                    END IF;

                    -- Every stage now standing at the 3ᵉ année *is* the new requirement set: the four
                    -- inserted above plus the two the old 3ᵉ année already held. Written this way
                    -- rather than by matching names, because those two were named by the legacy
                    -- import and a migration that guesses at their spelling silently records four
                    -- stages instead of six.
                    INSERT INTO public."CurriculumStages"
                        ("CurriculumId", "StageId", "Coefficient", "DurationInDays")
                    SELECT curric, s."Id", 1, 30
                    FROM public."Stages" s
                    WHERE s."LevelId" = med3
                      AND NOT EXISTS (
                          SELECT 1 FROM public."CurriculumStages" cs
                          WHERE cs."CurriculumId" = curric AND cs."StageId" = s."Id");

                    -- ⚠ …and that inference is checked rather than trusted. « Tous les stages du
                    -- niveau » is only the right set while the 3ᵉ année holds exactly Chirurgie and
                    -- Médecine beside the four inserted here. If the legacy catalogue put anything
                    -- else there, the requirement set would silently gain a stage the arrêté does
                    -- not name — and nothing downstream could tell. Refusing costs an apply; a wrong
                    -- requirement set costs a promotion planned against stages it does not owe.
                    SELECT count(*) INTO recorded
                    FROM public."CurriculumStages"
                    WHERE "CurriculumId" = curric;

                    IF recorded <> 6 THEN
                        RAISE EXCEPTION
                            'Le CNPN 1650.25 exige 6 stages en 3ᵉ année, mais le niveau en porte % : %. Vérifiez le catalogue avant d''appliquer.',
                            recorded,
                            (SELECT string_agg(s."Name", ', ' ORDER BY s."Name")
                             FROM public."Stages" s WHERE s."LevelId" = med3);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ⚠ Removes only what Up created, and only while nothing hangs off it. A stage that has
            // acquired a créneau, a cohorte or an affectation is a stage the faculty has planned
            // against; it is left in place rather than dragged out from under a published year, and
            // the same goes for its requirement row.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    med3     integer;
                    cnpn1650 integer;
                    curric   integer;
                BEGIN
                    SELECT "Id" INTO med3
                    FROM public."Levels"
                    WHERE "Year" = 3 AND "AcademicProgram" = 'Medecine';

                    SELECT "Id" INTO cnpn1650
                    FROM public."CnpnVersions"
                    WHERE "Code" = '1650.25';

                    IF med3 IS NULL OR cnpn1650 IS NULL THEN
                        RETURN;
                    END IF;

                    SELECT "Id" INTO curric
                    FROM public."Curriculums"
                    WHERE "CnpnVersionId" = cnpn1650 AND "LevelId" = med3;

                    DELETE FROM public."Stages" s
                    WHERE s."LevelId" = med3
                      AND s."Name" IN ('Cardiologie', 'Pneumologie',
                                       'Dermatologie - Endocrinologie', 'Rhumatologie - Radiologie')
                      AND NOT EXISTS (SELECT 1 FROM public."StageSlots" sl WHERE sl."StageId" = s."Id")
                      AND NOT EXISTS (SELECT 1 FROM public."Cohorts" c WHERE c."StageId" = s."Id");

                    IF curric IS NOT NULL THEN
                        DELETE FROM public."CurriculumStages" cs
                        WHERE cs."CurriculumId" = curric
                          AND NOT EXISTS (SELECT 1 FROM public."Stages" s WHERE s."Id" = cs."StageId");

                        DELETE FROM public."Curriculums" c
                        WHERE c."Id" = curric
                          AND NOT EXISTS (SELECT 1 FROM public."CurriculumStages" cs
                                          WHERE cs."CurriculumId" = c."Id");
                    END IF;
                END $$;
                """);
        }
    }
}
