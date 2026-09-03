using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.AcademicYears;
using PGSH.Application.Calendar;
using PGSH.Application.Exports;
using PGSH.Application.Hospitals.Chefs;
using PGSH.Domain.Calendar;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Export;

internal sealed class GetStageAssignmentsExportQueryHandler(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver,
    ExecutionAuthorizer authorizer,
    WorkingDayProvider workingDayProvider,
    ServiceChefProvider chefProvider,
    IExportWorkbookWriter writer)
    : IQueryHandler<GetStageAssignmentsExportQuery, ExportFile>
{
    /// <summary>
    /// One year across every promotion is roughly 30 000 attempts, which is a file nobody opens
    /// twice. The refusal names the count and the axis that narrows it.
    /// </summary>
    internal const int MaxRows = 25_000;

    public async Task<Result<ExportFile>> Handle(
        GetStageAssignmentsExportQuery request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(ExportErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<ExportFile>(access.Error);

        var year = await yearResolver.ResolveWithLabelAsync(request.AcademicYearId, cancellationToken);
        if (year.IsFailure)
            return Result.Failure<ExportFile>(year.Error);

        (int yearId, string yearLabel) = year.Value;

        string? levelLabel = null;
        if (request.LevelId is { } requestedLevel)
        {
            var level = await dbContext.Levels
                .AsNoTracking()
                .Where(l => l.Id == requestedLevel)
                .Select(l => new { l.Label, l.Year, l.AcademicProgram })
                .FirstOrDefaultAsync(cancellationToken);

            if (level is null)
                return Result.Failure<ExportFile>(RegistrationErrors.MissingLevel);

            levelLabel = ExportLabels.Level(level.Label, level.Year, level.AcademicProgram);
        }

        string? stageName = null;
        if (request.StageId is { } requestedStage)
        {
            stageName = await dbContext.Stages
                .AsNoTracking()
                .Where(s => s.Id == requestedStage)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (stageName is null)
                return Result.Failure<ExportFile>(StageErrors.NotFound(requestedStage));
        }

        var assignmentsQuery = StageAssignmentExportQueries.AssignmentsQuery(
            dbContext, yearId, request.LevelId, request.StageId,
            request.AcademicGroupId, request.OnlyEvaluated);

        int rowCount = await assignmentsQuery.CountAsync(cancellationToken);
        if (rowCount > MaxRows)
            return Result.Failure<ExportFile>(ExportErrors.TooManyRows(
                rowCount, MaxRows, "une promotion, un stage ou un groupe"));

        var assignments = await assignmentsQuery.ToListAsync(cancellationToken);

        var periods = await StageAssignmentExportQueries
            .PeriodsQuery(dbContext, yearId, request.LevelId, request.StageId,
                request.AcademicGroupId, request.OnlyEvaluated)
            .ToListAsync(cancellationToken);

        var objectiveScores = await StageAssignmentExportQueries
            .ObjectiveScoresQuery(dbContext, yearId, request.LevelId, request.StageId,
                request.AcademicGroupId, request.OnlyEvaluated)
            .ToListAsync(cancellationToken);

        var coverage = await StageAssignmentExportQueries
            .SlotCoverageQuery(dbContext, yearId, request.LevelId, request.StageId,
                request.AcademicGroupId, request.OnlyEvaluated)
            .ToListAsync(cancellationToken);

        var calendar = await workingDayProvider.BuildAsync(cancellationToken);

        // ⚠ As of each période's own start, never one date for the file: a document covering a year
        // of rotations spans months, and a chef who took over in January did not lead the students
        // who stood there in October. The directory is built once and asked per row.
        //
        // ⚠ InForce is SourceNoteOnly today, so the as-of date decides nothing here yet — the
        // legacy note is undated. Keep asking per row anyway: the date is what makes the file right
        // the day the policy goes back to Authority, and a per-file date would then be wrong on half
        // of it with nothing on the page saying so.
        var chefs = await chefProvider.BuildAsync(
            periods.Select(p => p.ServiceId).Distinct().ToList(),
            ServiceChefPolicy.InForce,
            cancellationToken);

        var slotsByPeriod = coverage
            .GroupBy(c => c.PeriodId)
            .ToDictionary(
                g => g.Key,
                g => CoveredSlotFolder.Fold(
                    g.Select(c => new CoveredSlot(c.PeriodNumber, c.Label, c.Start, c.End)).ToList(),
                    calendar));

        var marks = BuildMarks(periods, objectiveScores);
        var periodsByAssignment = periods
            .GroupBy(p => p.AssignmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StagePeriodExportRow>)g
                .OrderBy(p => p.Start).ThenBy(p => p.End).ToList());

        var folded = assignments.ToDictionary(
            a => a.AssignmentId,
            a => StagePeriodFolder.Fold(
                periodsByAssignment.TryGetValue(a.AssignmentId, out var own)
                    ? own.Select(ToExportedPeriod).ToList()
                    : [],
                calendar));

        string scope = string.Join(" — ", new[]
        {
            levelLabel ?? "toutes promotions",
            stageName,
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

        string caption = $"Stages — {scope} — {yearLabel} — {ExportLabels.Count(assignments.Count)} affectation(s)"
            + (request.OnlyEvaluated ? " — évaluées uniquement" : "");

        var workbook = new ExportWorkbook(
            ExportFileName.Build("stages", levelLabel, stageName, yearLabel),
            [
                StagesSheet(assignments, folded, slotsByPeriod, chefs, calendar, caption),
                PeriodsSheet(assignments, periodsByAssignment, marks, slotsByPeriod, chefs, calendar, caption),
                SummarySheet(assignments, caption),
            ]);

        return new ExportFile(workbook.FileName, writer.Write(workbook));
    }

    /// <summary>
    /// ⚠ A column blank on every row reads as a column the export forgot — which is how a perfectly
    /// faithful file gets reported as broken. Same note the roll carries, for the same reason.
    ///
    /// <para><paramref name="extra"/> is what only the sheet knows: the chef-source note goes on the
    /// two sheets that print a chef and not on Synthèse, which has no such column and would be
    /// answering a question nobody reading it asked.</para>
    /// </summary>
    private static IReadOnlyList<string> Notes(
        IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<IReadOnlyList<ExportCell>> rows,
        string? extra = null) =>
        new[] { ExportNotes.EmptyColumnsNote(columns, rows), extra }
            .OfType<string>()
            .ToList();

    private static ExportedPeriod ToExportedPeriod(StagePeriodExportRow row) =>
        new(row.PeriodId, row.Start, row.End, row.ServiceId, row.ServiceName);

    /// <summary>
    /// The mark and the verdict of every évaluated période, asked of <see cref="StageScoring"/> — the
    /// single source of truth the domain roll-up and every read handler already share. ⚠ Never
    /// recomputed inline: an export that averages differently from the fiche de validation is a
    /// document that contradicts the system it came from.
    /// </summary>
    private static Dictionary<Guid, PeriodMark> BuildMarks(
        IReadOnlyList<StagePeriodExportRow> periods,
        IReadOnlyList<ObjectiveScoreExportRow> objectiveScores)
    {
        var scoresByEvaluation = objectiveScores
            .GroupBy(o => o.EvaluationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var marks = new Dictionary<Guid, PeriodMark>();

        foreach (var period in periods)
        {
            if (period.EvaluationId is not { } evaluationId || period.EvaluationMode is not { } mode)
                continue;

            var evaluation = new ServiceEvaluation
            {
                Id = evaluationId,
                Mode = mode,
                TotalScore = period.TotalScore,
                Outcome = period.Outcome,
                ObjectiveScores = scoresByEvaluation.TryGetValue(evaluationId, out var scores)
                    ? scores.Select(s => new ObjectiveScore
                    {
                        Score = s.Score,
                        StageObjective = new StageObjective { Weight = s.Weight },
                    }).ToList()
                    : [],
            };

            marks[period.PeriodId] = new PeriodMark(
                StageScoring.PeriodMark(evaluation),
                StageScoring.IsPeriodValidated(evaluation));
        }

        return marks;
    }

    private sealed record PeriodMark(decimal Mark, bool IsValidated);

    private static ExportSheet StagesSheet(
        IReadOnlyList<StageAssignmentExportRow> assignments,
        IReadOnlyDictionary<Guid, StagePeriodSummary> folded,
        IReadOnlyDictionary<Guid, CoveredSlotSummary> slotsByPeriod,
        ServiceChefDirectory chefs,
        WorkingDayCalendar calendar,
        string caption)
    {
        IReadOnlyList<ExportColumn> columns =
        [
            new("Nom", 22),
            new("Prénom", 20),
            new("CNE", 16),
            new("Apogée", 14),
            new("Programme", 14),
            new("Niveau", 22),
            new("Année universitaire", 18),
            new("Groupe", 22),
            new("N° groupe", 10),
            new("Partition", 10),
            new("Stage", 26),
            new("Niveau du stage", 22),
            new("Coefficient", 11),
            new("Mode", 18),
            new("Découpage", 34),
            new("Nb périodes", 11),
            new("Nb créneaux", 11),
            new("Créneaux", 16),
            new("Nb services", 11),
            new("Service(s)", 34),
            new("Chef(s) de service", 30),
            new("Origine du chef", 15),
            new("Période(s)", 30),
            new("Début", 12),
            new("Fin", 12),
            new("Jours ouvrables", 14),
            new("Jours calendaires", 14),
            new("Note", 9),
            new("Résultat", 13),
            new("Statut", 13),
            new("Détail des périodes", 52),
            new("Réf. stage", 38),
        ];

        var rows = assignments.Select(a =>
        {
            var summary = folded[a.AssignmentId];
            var covered = CoveredAcross(summary, slotsByPeriod, calendar);
            var attributions = Attributions(summary, chefs);

            return (IReadOnlyList<ExportCell>)
            [
                ExportCell.Text(a.LastName),
                ExportCell.Text(a.FirstName),
                ExportCell.Text(a.Cne),
                ExportCell.Text(a.Appogee),
                ExportCell.Text(ExportLabels.Program(a.Program)),
                ExportCell.Text(ExportLabels.Level(a.RegistrationLevelLabel, a.RegistrationLevelYear, a.Program)),
                ExportCell.Text(a.YearLabel),
                ExportCell.Text(a.GroupLabel),
                ExportCell.Count(a.GroupNumber),
                ExportCell.Text(a.RotationGroup),
                ExportCell.Text(a.StageName),
                ExportCell.Text(ExportLabels.Level(a.StageLevelLabel, a.StageLevelYear, a.StageProgram)),
                ExportCell.Count(a.Coefficient),
                ExportCell.Text(ExportLabels.RotationMode(a.RotationMode)),
                ExportCell.Text(summary.ShapeText),
                ExportCell.Count(summary.PeriodCount),
                // ⚠ Beside « Nb périodes », not instead of it. Under `SingleService` the two
                // genuinely differ — one période, kₛ créneaux — and « Période unique » next to
                // « 3 créneaux » is the whole answer to how a run of three columns is one row.
                ExportCell.Count(covered.Count == 0 ? null : covered.Count),
                ExportCell.Text(covered.RangeText),
                ExportCell.Count(summary.ServiceCount),
                ExportCell.Text(summary.ServicesText),
                ExportCell.Text(ChefsText(attributions)),
                ExportCell.Text(ExportLabels.ChefOrigin(attributions)),
                ExportCell.Text(summary.PeriodsText),
                ExportCell.Day(summary.Start),
                ExportCell.Day(summary.End),
                ExportCell.Count(summary.WorkingDays),
                ExportCell.Count(summary.CalendarDays),
                ExportCell.Numeric(a.FinalScore),
                ExportCell.Text(ExportLabels.StageResult(a.Result)),
                ExportCell.Text(ExportLabels.InternshipStatus(a.Status)),
                ExportCell.Paragraph(Detail(summary, slotsByPeriod, calendar)),
                ExportCell.Text(a.AssignmentId.ToString()),
            ];
        }).ToList();

        return new ExportSheet("Stages", caption, columns, rows,
            Notes(columns, rows, ExportNotes.ChefSourceNote(ServiceChefPolicy.InForce)));
    }

    /// <summary>
    /// The « Détail des périodes » cell: one line per période, in order. Redundant with the two
    /// summary columns for the ordinary single-période row — and that is the point. It is the cell a
    /// reader falls back on when the summary looks surprising, without leaving the sheet.
    /// </summary>
    private static string Detail(
        StagePeriodSummary summary,
        IReadOnlyDictionary<Guid, CoveredSlotSummary> slotsByPeriod,
        WorkingDayCalendar calendar)
    {
        if (summary.PeriodCount == 0)
            return "";

        return string.Join('\n', summary.Periods.Select((p, index) =>
        {
            var covered = slotsByPeriod.GetValueOrDefault(p.Id, CoveredSlotSummary.None);

            return string.Join(" · ", new[]
            {
                $"P{index + 1}",
                StagePeriodFolder.Span(p.Start, p.End),
                p.ServiceName,
                $"{calendar.Count(p.Start, p.End)} j.o.",
                // The line the fold hides: one période spanning three grid columns says so here
                // rather than leaving the reader to wonder where the axis went.
                covered.Count == 0 ? null : $"créneaux {covered.RangeText}",
            }.Where(part => part is not null));
        }));
    }

    /// <summary>
    /// The créneaux of every période of the attempt, folded once. ⚠ Deduplicated on the créneau
    /// number: two périodes of one attempt covering the same column would otherwise count it twice,
    /// and it is one column of the axis however many rows point at it.
    /// </summary>
    private static CoveredSlotSummary CoveredAcross(
        StagePeriodSummary summary,
        IReadOnlyDictionary<Guid, CoveredSlotSummary> slotsByPeriod,
        WorkingDayCalendar calendar)
    {
        var slots = summary.Periods
            .SelectMany(p => slotsByPeriod.GetValueOrDefault(p.Id, CoveredSlotSummary.None).Slots)
            .ToList();

        return slots.Count == 0
            ? CoveredSlotSummary.None
            : CoveredSlotFolder.Fold(slots, calendar);
    }

    /// <summary>
    /// One chef per stay, resolved as of the stay's <em>own</em> start — so the cell and its
    /// « Service(s) » neighbour correspond position by position, exactly as the services and the
    /// spans do. A single service is named once however many windows it was recorded in, for the
    /// same reason <c>ServicesText</c> does not repeat it either side of an arrow.
    /// </summary>
    private static IReadOnlyList<ServiceChefAttribution> Attributions(
        StagePeriodSummary summary, ServiceChefDirectory chefs) =>
        summary.ServiceCount switch
        {
            0 => [],
            1 => [chefs.For(summary.Stays[0].ServiceId, summary.Stays[0].Start)],
            _ => summary.Stays.Select(s => chefs.For(s.ServiceId, s.Start)).ToList(),
        };

    private static string ChefsText(IReadOnlyList<ServiceChefAttribution> attributions) =>
        attributions.Any(a => a.Name is not null)
            ? string.Join(" → ", attributions.Select(a => a.Name ?? "—"))
            : "";

    /// <summary>
    /// One row per <c>ServicePeriod</c> — the unit that carries a mark — and the créneaux it covers
    /// stated on that row rather than given rows of their own.
    ///
    /// <para>⚠ <b>Not one row per créneau.</b> A <c>SingleService</c> run is marked once; repeated
    /// across its three columns the note would be counted three times by the first pivot anybody
    /// builds. The périodes stay the rows and « Nb créneaux » / « Créneaux » / « Détail des
    /// créneaux » say what the fold collapsed.</para>
    /// </summary>
    private static ExportSheet PeriodsSheet(
        IReadOnlyList<StageAssignmentExportRow> assignments,
        IReadOnlyDictionary<Guid, IReadOnlyList<StagePeriodExportRow>> periodsByAssignment,
        IReadOnlyDictionary<Guid, PeriodMark> marks,
        IReadOnlyDictionary<Guid, CoveredSlotSummary> slotsByPeriod,
        ServiceChefDirectory chefs,
        WorkingDayCalendar calendar,
        string caption)
    {
        IReadOnlyList<ExportColumn> columns =
        [
            new("Nom", 22),
            new("Prénom", 20),
            new("CNE", 16),
            new("Apogée", 14),
            new("Niveau", 22),
            new("Groupe", 22),
            new("Stage", 26),
            new("N° période", 10),
            new("Service", 30),
            new("Hôpital", 26),
            new("Chef de service", 30),
            new("Origine du chef", 15),
            new("Début", 12),
            new("Fin", 12),
            new("Jours ouvrables", 14),
            new("Nb créneaux", 11),
            new("Créneaux", 16),
            new("Détail des créneaux", 46),
            new("État", 14),
            new("Note période", 12),
            new("Validée", 10),
            new("Origine", 14),
            new("Délocalisée", 11),
            new("Interrompue", 11),
            new("Réf. stage", 38),
        ];

        var rows = new List<IReadOnlyList<ExportCell>>();

        foreach (var assignment in assignments)
        {
            if (!periodsByAssignment.TryGetValue(assignment.AssignmentId, out var own))
                continue;

            int ordinal = 0;
            foreach (var period in own)
            {
                ordinal++;
                marks.TryGetValue(period.PeriodId, out var mark);

                var covered = slotsByPeriod.GetValueOrDefault(period.PeriodId, CoveredSlotSummary.None);
                // As of the période's own start: the chef this student actually served under, not
                // whoever leads the service the day the file is downloaded.
                var chef = chefs.For(period.ServiceId, period.Start);

                rows.Add(
                [
                    ExportCell.Text(assignment.LastName),
                    ExportCell.Text(assignment.FirstName),
                    ExportCell.Text(assignment.Cne),
                    ExportCell.Text(assignment.Appogee),
                    ExportCell.Text(ExportLabels.Level(
                        assignment.RegistrationLevelLabel, assignment.RegistrationLevelYear, assignment.Program)),
                    ExportCell.Text(assignment.GroupLabel),
                    ExportCell.Text(assignment.StageName),
                    ExportCell.Count(ordinal),
                    ExportCell.Text(period.ServiceName),
                    ExportCell.Text(period.HospitalName),
                    ExportCell.Text(chef.Name),
                    ExportCell.Text(ExportLabels.ChefOrigin([chef])),
                    ExportCell.Day(period.Start),
                    ExportCell.Day(period.End),
                    ExportCell.Count(calendar.Count(period.Start, period.End)),
                    // 0 is not printed: an ad-hoc période — imported history, a délocalisation, a
                    // revalidation — came from no grid, and « 0 » there reads as a créneau count
                    // that failed rather than as a période that never had one. « Origine » already
                    // says « Hors grille » for exactly those rows.
                    ExportCell.Count(covered.Count == 0 ? null : covered.Count),
                    ExportCell.Text(covered.RangeText),
                    ExportCell.Paragraph(covered.DetailText),
                    ExportCell.Text(ExportLabels.PeriodState(ServicePeriodLifecycle.StateOf(
                        period.IsStarted, period.IsComplete, period.IsInterrupted, period.EvaluationId is not null))),
                    mark is null ? ExportCell.Empty : ExportCell.Numeric(mark.Mark),
                    mark is null ? ExportCell.Empty : ExportCell.YesNo(mark.IsValidated),
                    ExportCell.Text(period.FromGrid ? "Répartition" : "Hors grille"),
                    ExportCell.YesNo(period.IsDelocalized),
                    ExportCell.YesNo(period.IsInterrupted),
                    ExportCell.Text(assignment.AssignmentId.ToString()),
                ]);
            }
        }

        return new ExportSheet("Périodes", caption, columns, rows,
            Notes(columns, rows, ExportNotes.ChefSourceNote(ServiceChefPolicy.InForce)));
    }

    /// <summary>
    /// Verdict counts per stage. ⚠ « Moyenne du stage » is the mean of the students' notes
    /// <em>within one stage</em>, which is a class average and legitimate — it is the mean
    /// <em>across</em> stages that this project does not have and must not invent.
    /// </summary>
    private static ExportSheet SummarySheet(
        IReadOnlyList<StageAssignmentExportRow> assignments, string caption)
    {
        IReadOnlyList<ExportColumn> columns =
        [
            new("Programme", 14),
            new("Niveau du stage", 22),
            new("Stage", 26),
            new("Effectif", 10),
            new("Validés", 10),
            new("Non validés", 12),
            new("Non évalués", 12),
            new("Taux de validation (%)", 18),
            new("Moyenne du stage", 15),
        ];

        var rows = assignments
            .GroupBy(a => new { a.StageProgram, a.StageLevelYear, a.StageLevelLabel, a.StageId, a.StageName })
            .OrderBy(g => g.Key.StageProgram)
            .ThenBy(g => g.Key.StageLevelYear)
            .ThenBy(g => g.Key.StageName)
            .Select(g =>
            {
                int total = g.Count();
                int validated = g.Count(a => a.Result == StageAssignmentResult.Validé);
                int rejected = g.Count(a => a.Result == StageAssignmentResult.NonValidé);
                var scored = g.Where(a => a.FinalScore.HasValue).Select(a => a.FinalScore!.Value).ToList();

                return (IReadOnlyList<ExportCell>)
                [
                    ExportCell.Text(ExportLabels.Program(g.Key.StageProgram)),
                    ExportCell.Text(ExportLabels.Level(g.Key.StageLevelLabel, g.Key.StageLevelYear, g.Key.StageProgram)),
                    ExportCell.Text(g.Key.StageName),
                    ExportCell.Count(total),
                    ExportCell.Count(validated),
                    ExportCell.Count(rejected),
                    ExportCell.Count(total - validated - rejected),
                    // Measured over the whole population, not over the evaluated part: a stage with
                    // one mark entered is not 100 % validated, it is one mark entered.
                    ExportCell.Numeric(total == 0 ? null : Math.Round(100m * validated / total, 1)),
                    ExportCell.Numeric(scored.Count == 0 ? null : Math.Round(scored.Average(), 2)),
                ];
            })
            .ToList();

        return new ExportSheet("Synthèse", caption, columns, rows,
            Notes(columns, rows));
    }
}
