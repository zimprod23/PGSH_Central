using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Stages.Levels;
using PGSH.Application.Stages.Progression;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.ReinscriptionSheet;

/// <summary>
/// Turns the faculty's own réinscription roll into the exact set of writes it would perform, and
/// reports every line that cannot be written and why.
///
/// <para>Preview and apply both run this and nothing else, so the dry run the user confirmed is
/// literally the plan that executes — the same guarantee the évaluation import, the déliberation and
/// the CNPN targeting make. It loads the closing year's registrations <b>tracked</b> in both cases;
/// the preview simply never saves.</para>
///
/// <para><b>Why this act exists beside <c>Reinscription/</c>.</b> That one <em>derives</em> the next
/// year from verdicts already recorded: admis → niveau + 1, redoublant → même niveau. This one is
/// handed the answer. The faculty's file states, per student, the étape he was in and the étape he
/// enters, and those two facts carry the verdict with them — so one upload closes the year and opens
/// the next. Deriving the destination a second time would not agree with the file: 804 of its lines
/// are final-year students re-registering in the same year, which the derivation reads as
/// « redoublant » and the faculty does not.</para>
///
/// <para><b>What it writes, in order:</b> the verdict onto the closing year's registration, then the
/// new registration at the level the file names, then <c>RegistrationCnpnStamper</c> over the batch —
/// which is where an effectivity rule authored over the summer actually bites.</para>
///
/// <para>⚠ <b>Silence says exactly one thing, and only in the final year.</b> This file is the roll
/// of who <em>is</em> coming back, so a registration it does not mention belongs to somebody who is
/// not — a graduate, an exclusion, an abandon. In a student's <b>last year</b> that is decidable: he
/// has defended, so the year is recorded « Diplômé ». Anywhere else it is not, and nothing is
/// written. Measured on the 2026-2027 roll: 1 006 of the 1 657 in 7ᵉ année Médecine and 212 of the
/// 356 in 6ᵉ année Pharmacie are absent and in their final year; **47** are absent below one, and
/// those are left untouched and named so somebody decides between abandon and exclusion.</para>
///
/// <para>⚠ <b>The graduation is <c>Inferred</c>, never <c>Declared</c>, and the difference is
/// load-bearing.</b> Nobody named these students on a document — PGSH read an absence. Recording it
/// as inferred is both honest and useful: a real defence roll arriving later is <c>Declared</c>, and
/// <c>Declared</c> overwrites <c>Inferred</c> while the reverse is refused, so the correction lands
/// by itself. It is also why <see cref="FinalYearTest.IsExactlyFinal"/> is stricter than the
/// déliberation's own « Diplômé » check: that one stands aside for a student with no CNPN because
/// the faculty <em>named</em> him, and an absence names nobody.</para>
/// </summary>
internal sealed class ReinscriptionSheetPlanner(
    IApplicationDbContext dbContext,
    FinalYearGuard finalYearGuard)
{
    /// <summary>
    /// The file is whatever the faculty sent — 6 862 lines for 2026-2027 — and the reply is a single
    /// object, exactly the shape that hides an unbounded collection. The counts stay exact; only the
    /// row list is cut, and the rows needing attention are ordered first so the cap never hides one.
    /// </summary>
    public const int MaxReportedRows = 1000;

    public async Task<Result<ReinscriptionSheetPlan>> PlanAsync(
        int fromAcademicYearId,
        int toAcademicYearId,
        IReadOnlyList<ReinscriptionSheetRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return Result.Failure<ReinscriptionSheetPlan>(ReinscriptionSheetErrors.SheetIsEmpty);

        if (fromAcademicYearId == toAcademicYearId)
            return Result.Failure<ReinscriptionSheetPlan>(ReinscriptionSheetErrors.SameYear);

        var years = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == fromAcademicYearId || y.Id == toAcademicYearId)
            .Select(y => new { y.Id, y.Label, y.StartDate })
            .ToListAsync(ct);

        var fromYear = years.FirstOrDefault(y => y.Id == fromAcademicYearId);
        var toYear = years.FirstOrDefault(y => y.Id == toAcademicYearId);

        if (fromYear is null)
            return Result.Failure<ReinscriptionSheetPlan>(StageErrors.AcademicYearNotFound(fromAcademicYearId));
        if (toYear is null)
            return Result.Failure<ReinscriptionSheetPlan>(StageErrors.AcademicYearNotFound(toAcademicYearId));
        if (toYear.StartDate <= fromYear.StartDate)
            return Result.Failure<ReinscriptionSheetPlan>(ReinscriptionSheetErrors.TargetYearNotLater);

        // The whole catalogue: a dozen rows, and both the level a student leaves and the one he
        // enters are read from it. IX_Level_Year_Program is unique, so (year, programme) is exact —
        // which is why the faculty's codes are resolved to that pair rather than matched on a label.
        var levels = (await LevelCatalogueQuery(dbContext).ToListAsync(ct))
            .ToDictionary(l => (l.Year, l.AcademicProgram));

        var codes = rows
            .Select(r => Normalize(r.Code))
            .Where(c => c is not null)
            .Select(c => c!)
            .Distinct()
            .ToList();

        var students = (await StudentsByCodeQuery(dbContext, codes).ToListAsync(ct))
            .ToDictionary(s => s.Appogee!, StringComparer.OrdinalIgnoreCase);

        // Tracked: the apply calls RecordYearOutcome on these very rows — both for the lines the file
        // names and for the final-year absentees it graduates. Level comes with them (each row's own
        // level is what the « Etape » column is checked against, and what says whether an absence is
        // a fin de cursus) and so does the student, to name an absentee in the report.
        var closing = await dbContext.Registrations
            .Include(r => r.Level)
            .Include(r => r.Student)
            // ⚠ Holds, or the roll stops being re-runnable. PlaceOnHold is idempotent per reason by
            // reading this collection, and an un-Included collection is indistinguishable from an
            // empty one: the second upload then raised a *second* absentee flag on all 1 267 of them.
            // Measured on the live base 2026-09-02, and invisible to the in-memory suite — that
            // provider fixes navigations up from the change tracker, so the idempotency test passed
            // throughout. IX_RegistrationHold_Registration_Reason_Active now makes it a constraint
            // violation rather than a silent duplication.
            .Include(r => r.Holds)
            .Where(r => r.AcademicYearId == fromAcademicYearId)
            .ToListAsync(ct);

        var closingByStudent = closing
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.First());

        var studentIds = students.Values.Select(s => s.Id).ToList();

        var alreadyRegistered = (await AlreadyRegisteredQuery(dbContext, toAcademicYearId, studentIds)
                .ToListAsync(ct))
            .ToHashSet();

        var totalYears = await TotalYearsByStudentAsync(fromAcademicYearId, studentIds, ct);
        var earliestFinalYear = await EarliestFinalYearByProgramAsync(ct);

        var context = new RowContext(
            levels, students, closingByStudent, alreadyRegistered, totalYears, earliestFinalYear);

        var resolutions = new List<Resolution>(rows.Count);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
            resolutions.Add(Resolve(row, context, seen));

        // ⚠ The final-year gate runs after the rows are resolved, not per row: it is a store lookup,
        // and asked one student at a time it is four round-trips each — ~27 000 for this file. Asked
        // once per destination level it is a dozen calls whatever the file's length, and it is the
        // same implementation the réinscription and the inscription both go through, so the three
        // cannot come to different answers about the same student.
        resolutions = await ApplyFinalYearGateAsync(resolutions, toAcademicYearId, ct);

        var work = resolutions
            .Where(r => r.Work is not null)
            .Select(r => r.Work!)
            .ToList();

        // ⚠ Allocated here, in the plan, so the preview shows the address that will actually be
        // written — and so the apply invents nothing of its own. Costs one query, and only when the
        // file names somebody PGSH does not hold.
        resolutions = await AllocateEmailsAsync(resolutions, ct);

        var newStudents = resolutions
            .Where(r => r.NewStudent is not null)
            .Select(r => r.NewStudent!)
            .ToList();

        var mentioned = resolutions
            .Where(r => r.SourceRegistrationId is not null)
            .Select(r => r.SourceRegistrationId!.Value)
            .ToHashSet();

        var absence = ReadAbsence(closing, mentioned, totalYears);

        var report = Summarize(fromYear.Label, toYear.Label, resolutions, absence);

        // ⚠ The plan carries the *uncapped* rows and absentees alongside the report, which is capped
        // at MaxReportedRows. A screen has to be bounded; a document must not be, or the export whose
        // whole purpose is « donne-moi la liste » silently stops at a thousand lines. The report is
        // what the browser reads and these are what the workbook is written from.
        return new ReinscriptionSheetPlan(
            report,
            toAcademicYearId,
            work,
            newStudents,
            absence.Graduations,
            absence.Holds,
            resolutions.Select(r => r.Report).ToList(),
            absence.All);
    }

    // -------------------------------------------------------------------------------------------
    // One line
    // -------------------------------------------------------------------------------------------

    private sealed record RowContext(
        IReadOnlyDictionary<(int Year, AcademicProgram Program), LevelRef> Levels,
        IReadOnlyDictionary<string, StudentRef> Students,
        IReadOnlyDictionary<Guid, Registration> ClosingByStudent,
        IReadOnlySet<Guid> AlreadyRegistered,
        IReadOnlyDictionary<Guid, int> TotalYears,
        IReadOnlyDictionary<AcademicProgram, int> EarliestFinalYear);

    private sealed record Resolution(
        ReinscriptionSheetRowReport Report,
        PlannedRollover? Work,
        Guid? SourceRegistrationId,
        PlannedNewStudent? NewStudent = null);

    private static Resolution Resolve(
        ReinscriptionSheetRow row, RowContext context, Dictionary<string, int> seen)
    {
        string? code = Normalize(row.Code);
        string name = FullName(row.LastName, row.FirstName);

        if (code is null)
            return Fail(row, name, null, null, ReinscriptionSheetRowStatus.NoIdentifier,
                "Colonne « Code » vide — la ligne ne désigne aucun étudiant.");

        if (seen.TryGetValue(code, out int firstRow))
            return Fail(row, name, null, null, ReinscriptionSheetRowStatus.DuplicateRow,
                $"Le code {code} figure déjà à la ligne {firstRow}. "
                + "Un étudiant ne peut avoir qu'une inscription par année.");

        seen[code] = row.SheetRow;

        // ⚠ The out-of-scope check comes before everything else about the levels, and before the
        // student is even looked up. A master's row names a programme PGSH holds no level, no stage
        // and no CNPN for; reading it as « code inconnu » would refuse the whole file over 23 lines
        // that are not mistakes.
        if (FacultyLevelCodes.OutsideScope(row.FromLevelCode) is { } fromScope)
            return Skip(row, name, null, null, ReinscriptionSheetRowStatus.OutsideScope, fromScope);

        if (FacultyLevelCodes.OutsideScope(row.ToLevelCode) is { } toScope)
            return Skip(row, name, null, null, ReinscriptionSheetRowStatus.OutsideScope, toScope);

        var fromCode = FacultyLevelCodes.Resolve(row.FromLevelCode);
        var toCode = FacultyLevelCodes.Resolve(row.ToLevelCode);

        if (fromCode is null)
            return Fail(row, name, null, null, ReinscriptionSheetRowStatus.UnknownLevelCode,
                $"Étape « {row.FromLevelCode} » inconnue. Ajoutez-la au référentiel des codes de "
                + "niveau, ou déclarez-la hors périmètre si c'est un programme non géré par PGSH.");

        if (toCode is null)
            return Fail(row, name, fromCode.Label, null, ReinscriptionSheetRowStatus.UnknownLevelCode,
                $"Étape de destination « {row.ToLevelCode} » inconnue. Ajoutez-la au référentiel des "
                + "codes de niveau, ou déclarez-la hors périmètre.");

        // « Retrait » is a withdrawal wearing a level's clothes: no stages, nobody to rotate, and
        // Level.IsPromotion is false everywhere else for exactly this reason.
        if (!fromCode.IsPromotion || !toCode.IsPromotion)
            return Fail(row, name, fromCode.Label, toCode.Label, ReinscriptionSheetRowStatus.NotAPromotion,
                "« Retrait » n'est pas une année d'études : on n'y réinscrit personne.");

        if (fromCode.Program == toCode.Program && toCode.Year < fromCode.Year)
            return Fail(row, name, fromCode.Label, toCode.Label, ReinscriptionSheetRowStatus.LevelRegression,
                $"« {toCode.Label} » est en deçà de « {fromCode.Label} » : aucune décision de jury "
                + "ne produit ce mouvement. Vérifiez les deux colonnes « Etape ».");

        if (!context.Levels.TryGetValue((toCode.Year, toCode.Program), out var toLevel))
            return Fail(row, name, fromCode.Label, toCode.Label, ReinscriptionSheetRowStatus.LevelMissing,
                $"Aucun niveau « {toCode.Label} » n'existe dans le catalogue.");

        // ⚠ Created, not skipped. The roll names people PGSH has never seen — 26 of the 6 862 lines of
        // the 2026-2027 file — and skipping them left the only trace in a downloaded spreadsheet,
        // which is nowhere anybody works. They are created from what the file actually carries (the
        // Apogée and the name, nothing else) and flagged IncompleteStudentFile so the rest of the
        // dossier gets filled in. That flag is deliberately **advisory**: he is cut into a roster and
        // planned like everyone else, because a missing date de naissance is no reason to keep a
        // student out of a rotation.
        if (!context.Students.TryGetValue(code, out var student))
            return NewStudent(row, name, code, fromCode.Label, toLevel);

        string studentName = student.FullName.Length > 0 ? student.FullName : name;

        // ⚠ The closing-year registration is carried even though nothing is written. The roll is
        // re-runnable by design, and on a second pass every student rolled over the first time lands
        // here: without this, all 6 813 of them stop being « couverts par le fichier », ReadAbsence
        // treats them as absentees, and the 791 sitting in a final year are recorded « Diplômé » —
        // students the file names on their own line, re-registered and present. Measured on the live
        // base 2026-09-02: a re-run offered 8 077 gels and 791 soutenances déduites where the first
        // pass had found 1 267 and 1 217.
        if (context.AlreadyRegistered.Contains(student.Id))
            return Skip(row, studentName, fromCode.Label, toLevel.Label,
                ReinscriptionSheetRowStatus.AlreadyRegistered,
                $"Déjà inscrit en « {toLevel.Label} » pour l'année de destination.",
                context.ClosingByStudent.TryGetValue(student.Id, out var rolled) ? rolled.Id : null);

        // No registration in the closing year: the file's statement about where he goes stands, its
        // statement about where he was cannot be checked, and there is nothing to pronounce on.
        if (!context.ClosingByStudent.TryGetValue(student.Id, out var source))
            return Register(row, studentName, fromCode.Label, toLevel, student.Id, null, null,
                ReinscriptionSheetRowStatus.WillRegisterWithoutSource,
                $"Réinscrit en « {toLevel.Label} », mais aucune inscription n'est enregistrée pour "
                + "l'année clôturée : aucune décision n'est portée. Vérifiez son parcours.");

        var sourceLevel = source.Level;
        if (sourceLevel is null
            || sourceLevel.Year != fromCode.Year
            || sourceLevel.AcademicProgram != fromCode.Program)
            return Fail(row, studentName, fromCode.Label, toLevel.Label,
                ReinscriptionSheetRowStatus.LevelMismatch,
                $"Le fichier indique « {fromCode.Label} », mais l'inscription enregistrée est en "
                + $"« {sourceLevel?.Label ?? "niveau inconnu"} ». Une décision portée sur la mauvaise "
                + "inscription ne se rattrape pas : corrigez le fichier ou l'inscription.");

        var (outcome, note) = DeriveOutcome(fromCode, toCode, toLevel, student.Id, context);

        return Register(row, studentName, sourceLevel.Label ?? fromCode.Label, toLevel, student.Id,
            source, outcome, ReinscriptionSheetRowStatus.WillRegister, note);
    }

    /// <summary>
    /// The verdict the two « Etape » columns carry, and the sentence explaining it.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>A same-level line in a final year records nothing, because the final year is not a
    /// year one passes or fails.</b> There is no déliberation for it: the student validates and
    /// revalidates his stages one at a time and is re-registered each September until they are all
    /// done — then re-registered again if he fails the <i>examens cliniques</i>, which open as soon as
    /// the stages are finished. He never redoes the stages he has already validated. 855 of the 1 657
    /// students in 7ᵉ année Médecine had been there before, which is what that process looks like from
    /// the data. Reading it as « redoublant » would be wrong twice over — it is not a failure, and
    /// <c>RegistrationStatus.Failed</c> <b>annuls the year's stages</b>
    /// (<c>RegistrationStatusExtensions.AnnulsItsStages</c>), so 795 lines of the 2026-2027 file would
    /// have wiped a year of stage record for people who did nothing wrong.</para>
    ///
    /// <para>⚠ <b>A programme change records nothing either.</b> Comparing a 3ᵉ année Médecine with a
    /// 1ʳᵉ année Pharmacie is comparing nothing; a réorientation is not a verdict on the year left
    /// behind, and the file does not claim it is.</para>
    ///
    /// <para>The source is <c>Declared</c>, not <c>Inferred</c>: this is the faculty's own document
    /// stating where each student is registered next year, which is a fact it is the authority on —
    /// not PGSH reading an enrolment sequence and guessing. <c>Inferred</c> may never overwrite
    /// <c>Declared</c>, and getting this backwards would make the whole column unreadable.</para>
    /// </remarks>
    private static (RegistrationStatus? Outcome, string Note) DeriveOutcome(
        FacultyLevelCode from, FacultyLevelCode to, LevelRef toLevel, Guid studentId, RowContext context)
    {
        if (from.Program != to.Program)
            return (null, $"Réorientation vers « {toLevel.Label} » : aucune décision n'est portée sur "
                        + "l'année clôturée, le changement de programme n'en est pas une.");

        if (to.Year > from.Year)
            return (RegistrationStatus.Validated,
                $"Admis — réinscrit en « {toLevel.Label} ».");

        int? total = context.TotalYears.TryGetValue(studentId, out int years) ? years : null;
        int? earliest = context.EarliestFinalYear.TryGetValue(from.Program, out int e) ? e : null;

        if (FinalYearTest.MayBeFinal(from.Year, total, earliest))
            return (null, $"Se réinscrit en « {toLevel.Label} », sa dernière année : il lui reste "
                        + "des stages à valider ou à revalider, ou les examens cliniques à passer. "
                        + "Aucune décision n'est portée — la dernière année ne se redouble pas.");

        return (RegistrationStatus.Failed,
            $"Redoublant — réinscrit en « {toLevel.Label} ».");
    }

    // -------------------------------------------------------------------------------------------
    // What the file does not say
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// What becomes of the closing year's registrations that no line of the file mentions.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Only the final year is decidable, and only from the student's own text.</b>
    /// Everything else absent from the roll is left exactly as it is: PGSH cannot tell an abandon
    /// from an exclusion from somebody who simply has not re-registered yet, and guessing would end
    /// a cursus.</para>
    ///
    /// <para>⚠ <b>A verdict already recorded is never touched</b> — not even to replace it with the
    /// same one. <c>RecordYearOutcome</c> refuses <c>Inferred</c> over <c>Declared</c> anyway, so
    /// planning it would produce a refusal mid-apply; and where the existing verdict is itself
    /// inferred, re-deriving it says nothing new. They are counted so the number of untouched
    /// absentees is never mistaken for a number of graduations.</para>
    /// </remarks>
    private static Absence ReadAbsence(
        IReadOnlyList<Registration> closing,
        IReadOnlySet<Guid> mentioned,
        IReadOnlyDictionary<Guid, int> totalYears)
    {
        var graduations = new List<PlannedGraduation>();
        var attention = new List<ReinscriptionSheetAbsentee>();
        var all = new List<ReinscriptionSheetAbsentee>();
        var holds = new List<PlannedAbsenteeHold>();
        int alreadyDecided = 0, notCovered = 0;

        foreach (var registration in closing)
        {
            if (mentioned.Contains(registration.Id)) continue;

            notCovered++;

            string name = FullName(registration.Student?.LastName, registration.Student?.FirstName);
            var level = registration.Level;
            string levelLabel = level?.Label ?? $"Niveau {registration.LevelId}";

            var outcome = ClassifyAbsence(registration, level, totalYears);
            string message = AbsenceMessage(outcome, level, totalYears, registration.StudentId);

            var absentee = new ReinscriptionSheetAbsentee(
                registration.StudentId, name, registration.Student?.Appogee, levelLabel,
                outcome, message);

            all.Add(absentee);

            // ⚠ Every absentee is held, the graduations included, and the reason is that the
            // graduation is *our inference* rather than the faculty's statement — it is read off a
            // blank cell. A partial roll would then end the cursus of people still enrolled with
            // nothing on the row saying a human had looked. Holding costs a genuine graduate nothing
            // (his year is closed and there is no next one to plan) and it catches the case an
            // absence most often really is: a réinscription that has not arrived, where the hold is
            // still standing on the day somebody registers him by hand.
            holds.Add(new PlannedAbsenteeHold(registration, message));

            if (outcome is ReinscriptionSheetAbsenceOutcome.AlreadyDecided)
            {
                alreadyDecided++;
                continue;
            }

            if (outcome is ReinscriptionSheetAbsenceOutcome.Graduating)
            {
                graduations.Add(new PlannedGraduation(registration, name, levelLabel));
                continue;
            }

            attention.Add(absentee);
        }

        return new Absence(notCovered, alreadyDecided, graduations, attention, holds, all);
    }

    /// <summary>
    /// What an absence means for one registration. Split out because three readers must agree about
    /// it: the graduation plan, the hold's evidence sentence, and the export's own column.
    /// </summary>
    private static ReinscriptionSheetAbsenceOutcome ClassifyAbsence(
        Registration registration, Level? level, IReadOnlyDictionary<Guid, int> totalYears)
    {
        // ⚠ A verdict already recorded is never re-derived, not even to the same value:
        // RecordYearOutcome refuses Inferred over Declared, so planning it would produce a refusal
        // mid-apply, and re-deriving an inferred one says nothing new. The registration is still
        // held — the absence is still unexplained, whatever last year's verdict was.
        if (registration.OutcomeSource is not null)
            return ReinscriptionSheetAbsenceOutcome.AlreadyDecided;

        // « Retrait » and its kind: no cursus to end, so nothing to graduate.
        if (level is null || !level.IsPromotion)
            return ReinscriptionSheetAbsenceOutcome.NotAPromotion;

        int? total = totalYears.TryGetValue(registration.StudentId, out int t) ? t : null;

        if (FinalYearTest.IsExactlyFinal(level.Year, total))
            return ReinscriptionSheetAbsenceOutcome.Graduating;

        return total is null
            ? ReinscriptionSheetAbsenceOutcome.NoTextOnRecord
            : ReinscriptionSheetAbsenceOutcome.BelowFinalYear;
    }

    private static string AbsenceMessage(
        ReinscriptionSheetAbsenceOutcome outcome,
        Level? level,
        IReadOnlyDictionary<Guid, int> totalYears,
        Guid studentId)
    {
        int? total = totalYears.TryGetValue(studentId, out int t) ? t : null;

        return outcome switch
        {
            ReinscriptionSheetAbsenceOutcome.AlreadyDecided =>
                "Absent du fichier, et son année porte déjà une décision : elle n'est pas retouchée. "
                + "L'absence reste à expliquer.",

            ReinscriptionSheetAbsenceOutcome.NotAPromotion =>
                "Absent du fichier, mais ce niveau n'est pas une année d'études : rien à prononcer.",

            ReinscriptionSheetAbsenceOutcome.Graduating =>
                $"Absent du fichier en dernière année de son CNPN ({total} ans) : soutenance déduite, "
                + "enregistrée « Diplômé » à titre déduit. À confirmer sur la liste des soutenances.",

            ReinscriptionSheetAbsenceOutcome.NoTextOnRecord =>
                "Absent du fichier, et aucun CNPN enregistré pour lui : impossible de dire si "
                + "c'était sa dernière année. À trancher à la main.",

            _ =>
                $"Absent du fichier en {level?.Year}ᵉ année, alors que son CNPN en compte "
                + $"{total} : ce n'est pas une fin de cursus. Abandon, exclusion ou "
                + "réinscription tardive — le fichier ne le dit pas.",
        };
    }

    private sealed record Absence(
        int NotCovered,
        int AlreadyDecided,
        IReadOnlyList<PlannedGraduation> Graduations,
        IReadOnlyList<ReinscriptionSheetAbsentee> NeedingAttention,
        IReadOnlyList<PlannedAbsenteeHold> Holds,
        IReadOnlyList<ReinscriptionSheetAbsentee> All);

    // -------------------------------------------------------------------------------------------
    // The final-year gate
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Marks the rows that enter a final year owing an earlier stage to be created <b>and held</b>,
    /// naming what they owe.
    /// </summary>
    /// <remarks>
    /// Grouped by destination level because <see cref="FinalYearGuard.EnsureMayEnterManyAsync"/>
    /// answers for one level at a time — and because that is the shape that keeps the cost flat: the
    /// guard reads a student's whole cursus only for the students the level is actually the last year
    /// of, so the promotions below it cost two queries however many students they hold.
    /// </remarks>
    private async Task<List<Resolution>> ApplyFinalYearGateAsync(
        List<Resolution> resolutions, int toAcademicYearId, CancellationToken ct)
    {
        var byLevel = resolutions
            .Where(r => r.Work is not null)
            .GroupBy(r => r.Work!.ToLevelId)
            .ToList();

        var refusals = new Dictionary<Guid, Error>();

        foreach (var group in byLevel)
        {
            var ids = group.Select(r => r.Work!.StudentId).ToList();
            foreach (var (studentId, error) in
                     await finalYearGuard.EnsureMayEnterManyAsync(ids, group.Key, toAcademicYearId, ct))
            {
                refusals[studentId] = error;
            }
        }

        if (refusals.Count == 0) return resolutions;

        // ⚠ The work is *kept*. The faculty named this student as coming back and its roll outranks
        // our stage record, which for most of these rows is simply not keyed in yet; dropping the
        // work here is what silently left 182 of one promotion unregistered. What the gate produces
        // now is a hold carrying the guard's own sentence — the same words the refusal used, because
        // they describe what was seen and that has not changed, only what is done about it.
        //
        // The verdict on the closing year is kept too: « il monte en 7ᵉ » is what the file says, and
        // the debt is a separate fact about stages, not a reason to leave last year unpronounced.
        return resolutions
            .Select(r => r.Work is { } work && refusals.TryGetValue(work.StudentId, out var error)
                ? r with
                {
                    Work = work with
                    {
                        Hold = new PlannedHold(
                            RegistrationHoldReason.OutstandingPriorStages, error.Description),
                    },
                    Report = r.Report with
                    {
                        Status = ReinscriptionSheetRowStatus.WillRegisterHeld,
                        Message = error.Description,
                    },
                }
                : r)
            .ToList();
    }

    /// <summary>
    /// Gives every student about to be created a free address at the faculty's domain.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>An e-mail is a login.</b> <c>SyncUserMiddleware</c> links a Keycloak <c>sub</c> to a
    /// local user by <c>IdentityProviderId</c> and <b>falls back to matching on e-mail</b>, so an
    /// address manufactured onto somebody who already holds it hands a student another person's
    /// account. The taken set is read from the <em>store</em>, and each address allocated here is
    /// added to it so two rows of one file cannot be given the same one.</para>
    ///
    /// <para>The local part comes from <c>StudentIdentifierRules</c>, which is the one rule shared
    /// with <c>LegacyIdentityMapper</c> and <c>InscriptionPlanner</c> — a second copy that kept digits
    /// as well as letters would give one faculty two address namespaces.</para>
    ///
    /// <para>The query runs only when a row will read it: a file naming nobody new costs nothing.</para>
    /// </remarks>
    private async Task<List<Resolution>> AllocateEmailsAsync(
        List<Resolution> resolutions, CancellationToken ct)
    {
        var needing = resolutions.Where(r => r.NewStudent is not null).ToList();
        if (needing.Count == 0) return resolutions;

        var taken = new HashSet<string>(
            await TakenEmailsQuery(dbContext, EmailDomain).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        var allocated = new Dictionary<int, string>();

        foreach (var resolution in needing)
        {
            var planned = resolution.NewStudent!;
            string local = StudentIdentifierRules.EmailLocalPart(planned.FirstName, planned.LastName);
            if (local.Length == 0) local = $"etudiant{planned.Appogee}";

            for (int suffix = 0; suffix < MaxEmailAttempts; suffix++)
            {
                string candidate = StudentIdentifierRules.EmailCandidate(local, suffix, EmailDomain);
                if (taken.Add(candidate))
                {
                    allocated[planned.SheetRow] = candidate;
                    break;
                }
            }
        }

        // ⚠ The address is written onto the row's own message, not merely counted. « N adresses
        // générées » tells nobody *which* address a given student was handed, and that address is his
        // login: it is the one manufactured value somebody has to be able to read, communicate and
        // correct. The same rule InscriptionReport follows.
        return resolutions
            .Select(r => r.NewStudent is { } n && allocated.TryGetValue(n.SheetRow, out var mail)
                ? r with
                {
                    NewStudent = n with { GeneratedEmail = mail },
                    Report = r.Report with
                    {
                        Message = r.Report.Message + $" Adresse générée : {mail}.",
                    },
                }
                : r)
            .ToList();
    }

    /// <summary>
    /// Every address already at the faculty's domain. ⚠ Named, so <c>SqlTranslationTests</c> can
    /// compile it — and scoped to the domain because that is the only namespace this allocates in.
    /// </summary>
    internal static IQueryable<string> TakenEmailsQuery(IApplicationDbContext db, string domain) =>
        db.Users.AsNoTracking()
            .Where(u => u.Email != null && u.Email.EndsWith("@" + domain))
            .Select(u => u.Email!);

    private const string EmailDomain = "um5.ac.ma";

    /// <summary>Enough suffixes for any realistic collision; beyond it the row keeps no address and
    /// is reported rather than guessed at.</summary>
    private const int MaxEmailAttempts = 50;

    // -------------------------------------------------------------------------------------------
    // Reporting
    // -------------------------------------------------------------------------------------------

    private static ReinscriptionSheetReport Summarize(
        string fromYearLabel,
        string toYearLabel,
        IReadOnlyList<Resolution> resolutions,
        Absence absence)
    {
        var rows = resolutions.Select(r => r.Report).ToList();

        int Count(ReinscriptionSheetRowStatus status) => rows.Count(r => r.Status == status);

        // ⚠ A student created from the roll is registered exactly like one it already knew, so he
        // counts here. Left out, « inscriptions créées » would disagree with the number of rows the
        // apply actually writes — and the two are read side by side on the confirmation screen.
        int willRegister = resolutions.Count(r => r.Work is not null || r.NewStudent is not null);
        int errors = rows.Count(r => r.Status.IsError());

        var byTargetLevel = resolutions
            .Where(r => r.Work is not null || r.NewStudent is not null)
            .GroupBy(r => r.Work?.ToLevelLabel ?? r.NewStudent!.ToLevelLabel)
            .ToDictionary(g => g.Key, g => g.Count());

        var byLevel = rows
            .Where(r => r.FromLevelLabel is not null)
            .GroupBy(r => r.FromLevelLabel!)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ReinscriptionSheetLevelBreakdown(
                FromLevelLabel: g.Key,
                Listed: g.Count(),
                WillRegister: g.Count(r => r.Status is ReinscriptionSheetRowStatus.WillRegister
                                                    or ReinscriptionSheetRowStatus.WillRegisterWithoutSource
                                                    or ReinscriptionSheetRowStatus.WillRegisterHeld
                                                    or ReinscriptionSheetRowStatus.WillCreateStudent),
                NeedsAttention: g.Count(r => r.Status.NeedsAttention())))
            .ToList();

        // Attention first, so the cap can never hide a line somebody has to act on.
        var ordered = rows
            .OrderByDescending(r => r.Status.IsError())
            .ThenByDescending(r => r.Status.NeedsAttention())
            .ThenBy(r => r.SheetRow)
            .Take(MaxReportedRows)
            .ToList();

        return new ReinscriptionSheetReport(
            FromYearLabel: fromYearLabel,
            ToYearLabel: toYearLabel,
            TotalRows: rows.Count,
            WillRegister: willRegister,
            WillRecordOutcome: resolutions.Count(r => r.Work is { Outcome: not null }),
            AlreadyRegistered: Count(ReinscriptionSheetRowStatus.AlreadyRegistered),
            OutsideScope: Count(ReinscriptionSheetRowStatus.OutsideScope),
            CreatedStudents: Count(ReinscriptionSheetRowStatus.WillCreateStudent),
            WithoutSourceRegistration: Count(ReinscriptionSheetRowStatus.WillRegisterWithoutSource),
            WillRegisterHeld: Count(ReinscriptionSheetRowStatus.WillRegisterHeld),
            ErrorCount: errors,
            NotCovered: absence.NotCovered,
            WillGraduate: absence.Graduations.Count,
            AbsentNeedingAttention: absence.NeedingAttention.Count,
            AbsentAlreadyDecided: absence.AlreadyDecided,
            AbsenteesHeld: absence.Holds.Count,
            GeneratedEmails: resolutions.Count(r => r.NewStudent?.GeneratedEmail is not null),
            CanApply: errors == 0,
            ByTargetLevel: byTargetLevel,
            ByLevel: byLevel,
            Rows: ordered,
            RowsTruncated: rows.Count > MaxReportedRows,
            // Only the absentees somebody has to act on are named. The graduations are a count and a
            // confirmation — 1 218 names would drown the 47 that need a decision, which is the whole
            // reason the cap exists.
            Absentees: absence.NeedingAttention.Take(MaxReportedRows).ToList(),
            AbsenteesTruncated: absence.NeedingAttention.Count > MaxReportedRows);
    }

    // -------------------------------------------------------------------------------------------
    // Queries — named, so SqlTranslationTests can compile them without a database
    // -------------------------------------------------------------------------------------------

    /// <summary>Every level, with the pair the faculty codes resolve to.</summary>
    internal static IQueryable<LevelRef> LevelCatalogueQuery(IApplicationDbContext db) =>
        db.Levels
            .AsNoTracking()
            .Select(l => new LevelRef(
                l.Id,
                l.Label ?? ("Année " + l.Year),
                l.Year,
                l.AcademicProgram));

    /// <summary>
    /// The students the file names, by numéro Apogée.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>Contains</c> is right here and wrong in the lookups below: this set is <em>listed</em> —
    /// it is exactly the codes somebody typed into a spreadsheet — where the closing year's
    /// registrations are <em>described</em> by a predicate and number 8 077. Npgsql renders it as a
    /// single array parameter, not one parameter per code.
    /// </remarks>
    internal static IQueryable<StudentRef> StudentsByCodeQuery(
        IApplicationDbContext db, IReadOnlyCollection<string> codes) =>
        db.Students
            .AsNoTracking()
            .Where(s => s.Appogee != null && codes.Contains(s.Appogee))
            .Select(s => new StudentRef(
                s.Id,
                s.Appogee,
                ((s.FirstName ?? "") + " " + (s.LastName ?? "")).Trim()));

    /// <summary>Which of these students already hold a registration in the target year.</summary>
    internal static IQueryable<Guid> AlreadyRegisteredQuery(
        IApplicationDbContext db, int toAcademicYearId, IReadOnlyCollection<Guid> studentIds) =>
        db.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == toAcademicYearId && studentIds.Contains(r.StudentId))
            .Select(r => r.StudentId);

    /// <summary>
    /// How long each student's own text runs, read from the closing year's registration first and
    /// from his stamp only as a fallback — the order every CNPN read uses, and for the same reason:
    /// once an effectivity rule can move a student mid-cursus, « combien d'années doit-il ? » stops
    /// being a property of where he stands today.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>The two halves are scoped differently, and it is not an inconsistency.</b> The
    /// closing year's registrations are a set nobody enumerated — 8 077 of them — so they reach the
    /// store as a <em>predicate</em>; shipping their ids down is what made a déliberation preview
    /// take thirty seconds. The student half is scoped by ids because it exists only for the handful
    /// the file names who hold <em>no</em> registration that year (3 on the 2026-2027 roll) — a
    /// listed set, and one the predicate above cannot reach by construction.</para>
    ///
    /// <para>It has to cover the whole closing year rather than only the students the file names,
    /// because the absentees are exactly the ones it does not name — and whether an absence is a fin
    /// de cursus is decided by that number.</para>
    ///
    /// <para>Two flat queries rather than one projection carrying both: the registration's text has
    /// to be found through a filtered navigation, and that is the shape a provider refuses. The
    /// registration wins where both answer.</para>
    /// </remarks>
    private async Task<Dictionary<Guid, int>> TotalYearsByStudentAsync(
        int fromAcademicYearId, IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
    {
        var fromStamp = await StudentTextQuery(dbContext, studentIds).ToListAsync(ct);
        var fromRegistration = await ClosingYearTextQuery(dbContext, fromAcademicYearId).ToListAsync(ct);

        var map = new Dictionary<Guid, int>(fromRegistration.Count + fromStamp.Count);
        foreach (var row in fromStamp) map[row.StudentId] = row.TotalYears;
        foreach (var row in fromRegistration) map[row.StudentId] = row.TotalYears;
        return map;
    }

    internal static IQueryable<StudentText> StudentTextQuery(
        IApplicationDbContext db, IReadOnlyCollection<Guid> studentIds) =>
        db.Students
            .AsNoTracking()
            .Where(s => studentIds.Contains(s.Id) && s.CnpnVersionId != null)
            .Select(s => new StudentText(s.Id, s.CnpnVersion!.TotalYears));

    /// <summary>
    /// Every student of the closing year who carries a text, read the standard way:
    /// <c>r.CnpnVersionId ?? r.Student.CnpnVersionId</c>. Scoped by the predicate that selects the
    /// year, never by its 8 077 ids.
    /// </summary>
    internal static IQueryable<StudentText> ClosingYearTextQuery(
        IApplicationDbContext db, int fromAcademicYearId) =>
        db.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == fromAcademicYearId
                     && (r.CnpnVersionId != null || r.Student.CnpnVersionId != null))
            .Select(r => new StudentText(
                r.StudentId,
                r.CnpnVersionId != null
                    ? r.CnpnVersion!.TotalYears
                    : r.Student.CnpnVersion!.TotalYears));

    /// <summary>
    /// The shortest text on record per programme — what answers « peut-être sa dernière année ? » for
    /// a student nobody has stamped. Carries the same known bias as the déliberation's copy, in the
    /// same safe direction: too low leaves a repeat unrecorded, too high would record a redoublement
    /// against a thesis year and annul its stages.
    /// </summary>
    private async Task<Dictionary<AcademicProgram, int>> EarliestFinalYearByProgramAsync(
        CancellationToken ct)
    {
        var versions = await dbContext.CnpnVersions
            .AsNoTracking()
            .Select(v => new { v.AcademicProgram, v.TotalYears })
            .ToListAsync(ct);

        return versions
            .GroupBy(v => v.AcademicProgram)
            .ToDictionary(g => g.Key, g => g.Min(v => v.TotalYears));
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    private static Resolution Register(
        ReinscriptionSheetRow row, string name, string? fromLabel, LevelRef toLevel, Guid studentId,
        Registration? source, RegistrationStatus? outcome,
        ReinscriptionSheetRowStatus status, string message) =>
        new(new ReinscriptionSheetRowReport(
                row.SheetRow, Normalize(row.Code), name, fromLabel, toLevel.Label, status, outcome,
                message),
            new PlannedRollover(studentId, toLevel.Id, toLevel.Label, source, outcome),
            source?.Id);

    /// <summary>
    /// A line that writes nothing — but may still have <em>named</em> a registration.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><paramref name="sourceRegistrationId"/> is what stops a skipped line being read as an
    /// absence.</b> « Couvert par le fichier » means the file mentioned him, not that it produced a
    /// write: a student already rolled over is named on his own line and is emphatically not somebody
    /// who failed to re-register. Passing null here for a row that resolved to a closing-year
    /// registration hands that registration to <c>ReadAbsence</c>, which then infers a soutenance from
    /// it. See the re-run case in <c>ReinscriptionSheetTests</c>.
    /// </remarks>
    private static Resolution Skip(
        ReinscriptionSheetRow row, string name, string? fromLabel, string? toLabel,
        ReinscriptionSheetRowStatus status, string message,
        Guid? sourceRegistrationId = null) =>
        new(new ReinscriptionSheetRowReport(
                row.SheetRow, Normalize(row.Code), name, fromLabel, toLabel, status, null, message),
            null,
            sourceRegistrationId);

    private static Resolution Fail(
        ReinscriptionSheetRow row, string name, string? fromLabel, string? toLabel,
        ReinscriptionSheetRowStatus status, string message) =>
        Skip(row, name, fromLabel, toLabel, status, message);

    /// <summary>
    /// A line naming somebody PGSH does not hold: create him, and flag the thin dossier.
    /// </summary>
    /// <remarks>
    /// <para>The roll gives one « NOM PRENOM » string, so the split is a guess and is recorded as one
    /// — the operator corrects it on the student's own file, which is what the flag sends him to.</para>
    ///
    /// <para>⚠ <b>No CNE is manufactured.</b> The row carries an Apogée, and <c>Student.CNE</c> is
    /// optional since the <c>LEGACY-</c> placeholders were cleared: a row with an Apogée and no CNE is
    /// stored with none, exactly as <c>InscriptionPlanner</c> does. The e-mail is the one value that
    /// has to be invented, because <c>Users.Email</c> is NOT NULL UNIQUE — and it is allocated against
    /// the addresses already in the store, never merely against the batch.</para>
    /// </remarks>
    private static Resolution NewStudent(
        ReinscriptionSheetRow row, string name, string code, string? fromLabel, LevelRef toLevel)
    {
        var (lastName, firstName) = SplitName(row.LastName, row.FirstName, name);

        return new Resolution(
            new ReinscriptionSheetRowReport(
                row.SheetRow, code, name, fromLabel, toLevel.Label,
                ReinscriptionSheetRowStatus.WillCreateStudent, null,
                $"Aucun étudiant ne porte le numéro Apogée {code} : il est créé à partir du fichier "
                + "et signalé « dossier à compléter ». Il participe à la planification ; complétez "
                + "sa fiche (CNE, e-mail, date de naissance) depuis sa page étudiant."),
            Work: null,
            SourceRegistrationId: null,
            NewStudent: new PlannedNewStudent(
                row.SheetRow, code, lastName, firstName, toLevel.Id, toLevel.Label,
                toLevel.AcademicProgram,
                $"Créé depuis « Réinscriptions » : le fichier ne donne que le numéro Apogée {code} et "
                + $"le nom « {name} ». CNE, e-mail réel, date et lieu de naissance restent à saisir."));
    }

    /// <summary>
    /// The roll's two name columns, or a split of whatever single string it put in them.
    /// </summary>
    private static (string LastName, string FirstName) SplitName(
        string? lastName, string? firstName, string fallback)
    {
        string last = (lastName ?? "").Trim();
        string first = (firstName ?? "").Trim();

        if (last.Length > 0 && first.Length > 0) return (last, first);

        var parts = fallback.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("", ""),
            1 => (parts[0], ""),
            _ => (parts[0], string.Join(' ', parts[1..])),
        };
    }

    private static string FullName(string? lastName, string? firstName) =>
        $"{(firstName ?? "").Trim()} {(lastName ?? "").Trim()}".Trim();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal sealed record LevelRef(int Id, string Label, int Year, AcademicProgram AcademicProgram);

    internal sealed record StudentRef(Guid Id, string? Appogee, string FullName);

    internal sealed record StudentText(Guid StudentId, int TotalYears);
}

/// <summary>
/// One student's rollover: the verdict to record on the year that is closing, and the registration to
/// create for the year that is opening.
/// </summary>
/// <param name="Source">
/// The closing year's registration, tracked. Null when the student holds none — the file's word for
/// where he goes still stands, there is simply nothing to pronounce on.
/// </param>
/// <param name="Outcome">
/// Null where the level movement carries no verdict: a final-year repeat, a réorientation, or a
/// student with no closing-year registration.
/// </param>
/// <param name="Hold">
/// Raised on the registration the moment it is created, or null when nothing is wrong with it. The
/// only value it takes today is <c>OutstandingPriorStages</c> — the faculty's roll names a student
/// our own stage record says is not ready, and the roll wins while the disagreement is recorded.
/// </param>
internal sealed record PlannedRollover(
    Guid StudentId,
    int ToLevelId,
    string ToLevelLabel,
    Registration? Source,
    RegistrationStatus? Outcome,
    PlannedHold? Hold = null);

/// <summary>
/// A student the roll names and PGSH does not hold — to be created, registered, and flagged.
/// </summary>
/// <param name="GeneratedEmail">
/// Allocated by the planner, never by the applier, so the dry run shows the exact address that will
/// be written. ⚠ <c>Users.Email</c> is NOT NULL UNIQUE <em>and</em> <c>SyncUserMiddleware</c> falls
/// back to matching a Keycloak <c>sub</c> on it — an address that collides with a real one hands a
/// student somebody else's account. Allocated against the addresses in the store, not just the batch.
/// </param>
internal sealed record PlannedNewStudent(
    int SheetRow,
    string Appogee,
    string LastName,
    string FirstName,
    int ToLevelId,
    string ToLevelLabel,
    AcademicProgram Programme,
    string Evidence,
    string? GeneratedEmail = null);

/// <summary>A hold to raise, with the sentence that was true when the plan was built.</summary>
internal sealed record PlannedHold(RegistrationHoldReason Reason, string Evidence);

/// <summary>
/// One closing-year registration the roll does not name, to be withdrawn from planning.
/// </summary>
/// <param name="Registration">Tracked — the apply calls <c>PlaceOnHold</c> on this very row.</param>
internal sealed record PlannedAbsenteeHold(Registration Registration, string Evidence);

/// <summary>
/// One final-year registration the file does not mention, to be recorded « Diplômé ».
/// </summary>
/// <param name="Registration">Tracked — the apply calls <c>RecordYearOutcome</c> on this very row.</param>
internal sealed record PlannedGraduation(
    Registration Registration,
    string StudentFullName,
    string LevelLabel);

internal sealed record ReinscriptionSheetPlan(
    ReinscriptionSheetReport Report,
    int ToAcademicYearId,
    IReadOnlyList<PlannedRollover> Work,
    IReadOnlyList<PlannedNewStudent> NewStudents,
    IReadOnlyList<PlannedGraduation> Graduations,
    IReadOnlyList<PlannedAbsenteeHold> AbsenteeHolds,
    IReadOnlyList<ReinscriptionSheetRowReport> AllRows,
    IReadOnlyList<ReinscriptionSheetAbsentee> AllAbsentees);
