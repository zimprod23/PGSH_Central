using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.Fiche;

internal sealed class GetFicheDeValidationQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetFicheDeValidationQuery, FicheDeValidationResponse>
{
    public async Task<Result<FicheDeValidationResponse>> Handle(
        GetFicheDeValidationQuery request, CancellationToken cancellationToken)
    {
        // Gate on a cheap projection first — no point loading the whole record + evaluation graph
        // only to reject a stage that isn't validated yet.
        var head = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Id == request.AssignmentId)
            .Select(a => new { a.Result })
            .FirstOrDefaultAsync(cancellationToken);

        if (head is null)
            return Result.Failure<FicheDeValidationResponse>(StageErrors.AssignmentNotFound(request.AssignmentId));

        // The fiche certifies a passed stage — refuse it until the whole stage is validated.
        if (head.Result != StageAssignmentResult.Validé)
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
