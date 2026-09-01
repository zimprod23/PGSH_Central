using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Fiche;

internal sealed class GetFicheDeValidationQueryHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetFicheDeValidationQuery, FicheDeValidationResponse>
{
    public async Task<Result<FicheDeValidationResponse>> Handle(
        GetFicheDeValidationQuery request, CancellationToken cancellationToken)
    {
        // The fiche is an attestation in someone's name — same read scope as the record it prints.
        var access = await authorizer.EnsureCanReadAssignmentAsync(request.AssignmentId, cancellationToken);
        if (access.IsFailure)
            return Result.Failure<FicheDeValidationResponse>(access.Error);

        // Gate on a cheap projection first — no point loading the whole record + evaluation graph
        // only to reject a stage that isn't validated yet.
        var head = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Id == request.AssignmentId)
            .Select(a => new { a.Result, a.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (head is null)
            return Result.Failure<FicheDeValidationResponse>(StageErrors.AssignmentNotFound(request.AssignmentId));

        // The fiche is an official document, so it needs both halves: the marks must say the stage
        // passed (Result) AND the administration must have ratified them (Status). Gating on the
        // marks alone would let a student print an attestation the moment the chef saved a grade,
        // before Scolarité ever saw it — and a ratification can still be refused.
        if (head.Result != StageAssignmentResult.Validé || head.Status != InternshipStatus.Validated)
            return Result.Failure<FicheDeValidationResponse>(StageErrors.FicheNotAvailable);

        var assignment = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Include(a => a.Registration).ThenInclude(r => r.Student)
            .Include(a => a.Cohort).ThenInclude(c => c.Stage).ThenInclude(s => s.Level)
            .Include(a => a.Cohort).ThenInclude(c => c.AcademicGroup)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Service).ThenInclude(s => s.Hospital)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Evaluation!)
                .ThenInclude(e => e.ObjectiveScores).ThenInclude(o => o.StageObjective)
            .FirstAsync(a => a.Id == request.AssignmentId, cancellationToken);

        var periods = assignment.ServicePeriods
            .Where(p => !p.IsInterrupted && p.Evaluation is not null)
            .OrderBy(p => p.StartDate)
            .Select(p => new FichePeriod(
                p.Service.Name,
                p.Service.Hospital.Name,
                p.StartDate,
                p.EndDate,
                StageScoring.PeriodMark(p.Evaluation!),
                p.Evaluation!.ObjectiveScores
                    .OrderBy(o => o.StageObjective.Weight)
                    .Select(o => new FicheObjective(o.StageObjective.Label, ObjectiveMark(o)))
                    .ToList()))
            .ToList();

        var student = assignment.Registration.Student;
        return new FicheDeValidationResponse(
            $"{student.FirstName ?? ""} {student.LastName ?? ""}".Trim(),
            student.Appogee,
            student.CNE,
            assignment.Cohort.StageId,
            assignment.Cohort.Stage.Name,
            assignment.Cohort.Stage.Level?.Label,
            assignment.Cohort.Label,
            assignment.Cohort.AcademicGroup?.Label,
            assignment.FinalScore ?? 0m,
            periods);
    }

    // A numerically graded objective shows its score; a validate-only objective on a passed stage
    // shows the 10 it maps to (0 only if it was individually marked not-validated).
    private static decimal ObjectiveMark(ObjectiveScore o) =>
        o.Score ?? (o.Outcome == EvaluationOutcome.Validated ? 10m : 0m);
}
