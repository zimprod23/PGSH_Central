using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PGSH.Infrastructure.Migrations
{
    /// <summary>
    /// The 1ʳᵉ and 2ᵉ années of arrêté 1650.25 — one immersion stage each, and the first stages
    /// those two promotions have ever had on record.
    ///
    /// <list type="bullet">
    ///   <item>1ʳᵉ année — <b>Stage d'immersion</b>: familiarisation with the hospital.</item>
    ///   <item>2ᵉ année — <b>Stage d'immersion soins infirmiers</b>: the nursing half.</item>
    /// </list>
    ///
    /// <para>Both are 15 jours ouvrables — three weeks, the same order as the seven 14-day rows the
    /// catalogue already holds — coefficient 1, and <c>SingleService</c>: a student spends the
    /// fortnight in one place and is certified once.</para>
    ///
    /// <para><b>Why these promotions have nothing today.</b> The legacy Access base stopped recording
    /// the 1ʳᵉ and 2ᵉ années altogether, so the import carried no stage, no cohorte and no curriculum
    /// for them. Both are already governed by 1650.25 — the effectivity rules for the 1ʳᵉ (from
    /// 2024-2025) and the 2ᵉ (from 2025-2026) were authored and applied in August 2026 — so their
    /// requirement sets were simply empty, and <c>CohortProvisioner</c> stands aside where no set is
    /// recorded. That is why nothing has complained.</para>
    ///
    /// <para>⚠ <b>A stage served outside the grid is expected here, not exceptional.</b> The faculty
    /// lets a student do this fortnight in a small hospital near home against a paper signed by the
    /// chef, while most are répartis normally. That case is already modelled — it is
    /// <c>InternshipAssignment.Delocalize</c>, an ad-hoc placement with no <c>CohortSlotAssignment</c>
    /// behind it, evaluated in <c>EvaluationMode.ValidatePeriod</c> (pass/fail, no mark). The one
    /// prerequisite is that the neighbourhood hospital exists as a <c>Service</c>, or the
    /// délocalisation has nowhere to point.</para>
    ///
    /// <para>Idempotent and guarded exactly as <c>Cnpn1650Med3Stages</c> is, and it raises rather
    /// than half-writing if a niveau or the texte is missing.</para>
    /// </summary>
    public partial class Cnpn1650ImmersionStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    cnpn1650 integer;
                    lvl      integer;
                    stg      integer;
                    curric   integer;
                    spec     record;
                BEGIN
                    SELECT "Id" INTO cnpn1650
                    FROM public."CnpnVersions"
                    WHERE "Code" = '1650.25';

                    IF cnpn1650 IS NULL THEN
                        RAISE EXCEPTION 'Aucun CNPN de code 1650.25 : le texte doit être enregistré avant ses exigences.';
                    END IF;

                    FOR spec IN
                        SELECT * FROM (VALUES
                            (1, 'Stage d''immersion',
                                'Arrêté 1650.25 — 1ʳᵉ année : familiarisation avec le milieu hospitalier.'),
                            (2, 'Stage d''immersion soins infirmiers',
                                'Arrêté 1650.25 — 2ᵉ année : immersion en soins infirmiers.')
                        ) AS v(level_year, stage_name, descr)
                    LOOP
                        -- IX_Level_Year_Program is unique on (Year, AcademicProgram): exact.
                        SELECT "Id" INTO lvl
                        FROM public."Levels"
                        WHERE "Year" = spec.level_year AND "AcademicProgram" = 'Medecine';

                        IF lvl IS NULL THEN
                            RAISE EXCEPTION 'Aucun niveau Médecine de % ᵉ année : le catalogue des niveaux doit exister avant les stages.', spec.level_year;
                        END IF;

                        INSERT INTO public."Stages"
                            ("Name", "Coefficient", "Description", "DurationInDays", "LevelId", "RotationMode")
                        SELECT spec.stage_name, 1, spec.descr, 15, lvl, 'SingleService'
                        WHERE NOT EXISTS (
                            SELECT 1 FROM public."Stages" s
                            WHERE s."LevelId" = lvl AND s."Name" = spec.stage_name);

                        SELECT "Id" INTO stg
                        FROM public."Stages"
                        WHERE "LevelId" = lvl AND "Name" = spec.stage_name;

                        SELECT "Id" INTO curric
                        FROM public."Curriculums"
                        WHERE "CnpnVersionId" = cnpn1650 AND "LevelId" = lvl;

                        IF curric IS NULL THEN
                            INSERT INTO public."Curriculums" ("CnpnVersionId", "LevelId", "Reference")
                            VALUES (cnpn1650, lvl,
                                    'Arrêté 1650.25 — ' || spec.level_year || 'ᵉ année')
                            RETURNING "Id" INTO curric;
                        END IF;

                        -- Named explicitly rather than « every stage of this niveau »: unlike the
                        -- 3ᵉ année, these levels are not known to be empty, and a stray legacy row
                        -- would silently become a requirement of the new text.
                        INSERT INTO public."CurriculumStages"
                            ("CurriculumId", "StageId", "Coefficient", "DurationInDays")
                        SELECT curric, stg, 1, 15
                        WHERE NOT EXISTS (
                            SELECT 1 FROM public."CurriculumStages" cs
                            WHERE cs."CurriculumId" = curric AND cs."StageId" = stg);
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removes only what Up created, and only while nothing hangs off it: a stage that has
            // acquired a créneau or a cohorte has been planned against and is left alone.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    cnpn1650 integer;
                    lvl      integer;
                    spec     record;
                BEGIN
                    SELECT "Id" INTO cnpn1650
                    FROM public."CnpnVersions"
                    WHERE "Code" = '1650.25';

                    IF cnpn1650 IS NULL THEN
                        RETURN;
                    END IF;

                    FOR spec IN
                        SELECT * FROM (VALUES
                            (1, 'Stage d''immersion'),
                            (2, 'Stage d''immersion soins infirmiers')
                        ) AS v(level_year, stage_name)
                    LOOP
                        SELECT "Id" INTO lvl
                        FROM public."Levels"
                        WHERE "Year" = spec.level_year AND "AcademicProgram" = 'Medecine';

                        CONTINUE WHEN lvl IS NULL;

                        DELETE FROM public."Stages" s
                        WHERE s."LevelId" = lvl
                          AND s."Name" = spec.stage_name
                          AND NOT EXISTS (SELECT 1 FROM public."StageSlots" sl WHERE sl."StageId" = s."Id")
                          AND NOT EXISTS (SELECT 1 FROM public."Cohorts" c WHERE c."StageId" = s."Id");

                        DELETE FROM public."CurriculumStages" cs
                        USING public."Curriculums" c
                        WHERE cs."CurriculumId" = c."Id"
                          AND c."CnpnVersionId" = cnpn1650
                          AND c."LevelId" = lvl
                          AND NOT EXISTS (SELECT 1 FROM public."Stages" s WHERE s."Id" = cs."StageId");

                        DELETE FROM public."Curriculums" c
                        WHERE c."CnpnVersionId" = cnpn1650
                          AND c."LevelId" = lvl
                          AND NOT EXISTS (SELECT 1 FROM public."CurriculumStages" cs
                                          WHERE cs."CurriculumId" = c."Id");
                    END LOOP;
                END $$;
                """);
        }
    }
}
