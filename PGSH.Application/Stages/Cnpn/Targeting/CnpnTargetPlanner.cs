using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.AcademicYears;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.Targeting;

/// <summary>
/// Turns a targeting rule into the exact set of stamps it would write, and reports every student it
/// would leave alone and why.
///
/// <para>Preview and apply both run this and nothing else, so the dry run the faculty confirmed is
/// literally the plan that executes — the same guarantee <see cref="Evaluations.Import"/> makes about
/// marks, and for the same reason: this decides how many years a student owes. It therefore loads
/// tracked entities in both cases; the preview simply never saves.</para>
///
/// <para><b>It refuses to move a confirmed stamp.</b> <see cref="Student.AssignCnpnVersion"/> already
/// guards that per student; doing it in bulk would be exactly the way to defeat the guard, so a
/// student confirmed under another text is reported rather than overwritten. Upgrading an
/// <i>inferred</i> stamp is not a move and is allowed — that is the mechanism by which scolarité
/// confirms the ~2,200 assignments the backfill could only deduce.</para>
/// </summary>
internal sealed class CnpnTargetPlanner(
    IApplicationDbContext dbContext,
    AcademicYearResolver yearResolver)
{
    /// <summary>One student the plan will stamp, with the aggregate loaded so it can do it.</summary>
    internal sealed record PlannedAssignment(Student Student, bool IsEntryContradiction);

    internal sealed record Plan(CnpnTargetPreview Preview, IReadOnlyList<PlannedAssignment> Work);

    private const int AttentionSampleSize = 50;

    public async Task<Result<Plan>> PlanAsync(
        int cnpnVersionId, CnpnTargetCriteria criteria, CancellationToken ct)
    {
        var version = await dbContext.CnpnVersions
            .AsNoTracking()
            .Where(v => v.Id == cnpnVersionId)
            .Select(v => new
            {
                v.Id,
                v.Code,
                v.Label,
                v.AcademicProgram,
                IntakeFrom = v.AppliesToEntrantsFromAcademicYear != null
                    ? (DateOnly?)v.AppliesToEntrantsFromAcademicYear.StartDate
                    : null,
            })
            .FirstOrDefaultAsync(ct);

        if (version is null)
            return Result.Failure<Plan>(CnpnErrors.VersionNotFound(cnpnVersionId));

        if (version.AcademicProgram != criteria.Program)
            return Result.Failure<Plan>(
                CnpnErrors.TargetProgramMismatch(version.Code, version.AcademicProgram, criteria.Program));

        // A text that governs no intake binds nobody, so targeting students at it is meaningless —
        // and would leave them under a citation rather than a rule (arrêté 2175.22 is exactly this).
        if (version.IntakeFrom is null)
            return Result.Failure<Plan>(CnpnErrors.TargetTextGovernsNoIntake(version.Code));

        var year = await yearResolver.ResolveWithLabelAsync(criteria.AsOfAcademicYearId, ct);
        if (year.IsFailure)
            return Result.Failure<Plan>(year.Error);

        (int asOfYearId, string asOfLabel) = year.Value;

        // Year 0 is "Retrait" — withdrawn students. "année ≤ 2" must not sweep them into a new
        // programme they are no longer following.
        var matchedIds = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicYearId == asOfYearId
                     && r.Level.AcademicProgram == criteria.Program
                     && r.Level.Year >= 1
                     && r.Level.Year <= criteria.MaxLevelYear)
            .Select(r => r.StudentId)
            .Distinct()
            .ToListAsync(ct);

        if (matchedIds.Count == 0)
            return new Plan(
                Empty(version.Id, version.Code, version.Label, asOfLabel), []);

        // Earliest recorded registration per matched student — the entry the arrêté keys on.
        var entries = (await dbContext.Registrations
                .AsNoTracking()
                .Where(r => matchedIds.Contains(r.StudentId))
                .Select(r => new { r.StudentId, r.AcademicYear.StartDate, r.AcademicYear.Label })
                .ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.StartDate).First());

        var levelLabels = (await dbContext.Registrations
                .AsNoTracking()
                .Where(r => r.AcademicYearId == asOfYearId && matchedIds.Contains(r.StudentId))
                .Select(r => new { r.StudentId, r.Level.Label })
                .ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.First().Label);

        var versionCodes = await dbContext.CnpnVersions
            .AsNoTracking()
            .ToDictionaryAsync(v => v.Id, v => v.Code, ct);

        // Tracked: apply writes through the aggregate, and the preview must plan against the same
        // objects it would mutate.
        var students = await dbContext.Students
            .Where(s => matchedIds.Contains(s.Id))
            .ToListAsync(ct);

        var work = new List<PlannedAssignment>();
        var attention = new List<CnpnTargetRow>();
        int willAssign = 0, already = 0, contradicted = 0, conflicting = 0;

        foreach (var student in students.OrderBy(s => s.LastName).ThenBy(s => s.FirstName))
        {
            entries.TryGetValue(student.Id, out var entry);
            bool predates = entry is not null && entry.StartDate < version.IntakeFrom.Value;

            bool confirmedElsewhere =
                student.CnpnVersionId is { } current
                && current != cnpnVersionId
                && !student.CnpnAssignmentIsInferred;

            var status =
                confirmedElsewhere                                   ? CnpnTargetRowStatus.ConfirmedOnAnotherText
              : predates && !criteria.IncludeEntryContradictions     ? CnpnTargetRowStatus.EntryPredatesText
              : student.CnpnVersionId == cnpnVersionId
                    && !student.CnpnAssignmentIsInferred             ? CnpnTargetRowStatus.AlreadyOnThisText
              :                                                        CnpnTargetRowStatus.WillAssign;

            switch (status)
            {
                case CnpnTargetRowStatus.WillAssign:
                    work.Add(new PlannedAssignment(student, predates));
                    willAssign++;
                    continue;

                case CnpnTargetRowStatus.AlreadyOnThisText:
                    already++;
                    continue;

                case CnpnTargetRowStatus.EntryPredatesText:
                    contradicted++;
                    break;

                case CnpnTargetRowStatus.ConfirmedOnAnotherText:
                    conflicting++;
                    break;
            }

            if (attention.Count < AttentionSampleSize)
                attention.Add(Row(student, levelLabels, versionCodes, entry?.Label, status,
                    status == CnpnTargetRowStatus.EntryPredatesText
                        ? $"Première inscription en {entry?.Label} — antérieure à l'entrée en vigueur du texte."
                        : $"Déjà rattaché au CNPN {versionCodes.GetValueOrDefault(student.CnpnVersionId ?? 0, "?")} "
                          + "de façon confirmée ; un changement de CNPN se décide étudiant par étudiant."));
        }

        var preview = new CnpnTargetPreview(
            version.Id, version.Code, version.Label, asOfLabel,
            TotalMatched: students.Count,
            WillAssign: willAssign,
            AlreadyOnThisText: already,
            EntryPredatesText: contradicted,
            ConfirmedOnAnotherText: conflicting,
            CanApply: willAssign > 0,
            NeedsAttention: attention,
            NeedsAttentionTotal: contradicted + conflicting);

        return new Plan(preview, work);
    }

    private static CnpnTargetRow Row(
        Student student,
        IReadOnlyDictionary<Guid, string?> levelLabels,
        IReadOnlyDictionary<int, string> versionCodes,
        string? entryYearLabel,
        CnpnTargetRowStatus status,
        string message) =>
        new(student.Id,
            $"{student.FirstName} {student.LastName}".Trim(),
            student.CNE,
            levelLabels.GetValueOrDefault(student.Id),
            student.CnpnVersionId is { } v ? versionCodes.GetValueOrDefault(v) : null,
            entryYearLabel,
            status,
            message);

    private static CnpnTargetPreview Empty(int id, string code, string label, string asOfLabel) =>
        new(id, code, label, asOfLabel, 0, 0, 0, 0, 0, CanApply: false, [], 0);
}
