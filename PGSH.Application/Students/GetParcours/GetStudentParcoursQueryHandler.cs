using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.GetParcours;

internal sealed class GetStudentParcoursQueryHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetStudentParcoursQuery, StudentParcoursResponse>
{
    public async Task<Result<StudentParcoursResponse>> Handle(
        GetStudentParcoursQuery request, CancellationToken cancellationToken)
    {
        // Same scope as the level dossier: a parcours is years of marks, failures and retakes —
        // scolarité business, and the student's own. Not a chef's, even for a stage he supervised.
        var access = await authorizer.EnsureCanReadStudentDossierAsync(request.StudentId, cancellationToken);
        if (access.IsFailure)
            return Result.Failure<StudentParcoursResponse>(access.Error);

        var student = await dbContext.Students
            .AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Select(s => new { s.Id, s.FirstName, s.LastName })
            .FirstOrDefaultAsync(cancellationToken);

        if (student is null)
            return Result.Failure<StudentParcoursResponse>(StudentErrors.NotFound(request.StudentId));

        var registrations = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.StudentId == request.StudentId)
            .Select(r => new
            {
                r.Id,
                r.AcademicYearId,
                YearLabel = r.AcademicYear.Label,
                YearStart = r.AcademicYear.StartDate,
                r.AcademicYear.IsCurrent,
                r.LevelId,
                LevelLabel = r.Level.Label,
                LevelYear = r.Level.Year,
                r.Status,
                r.AcademicGroupId,
                GroupLabel = r.AcademicGroup != null ? r.AcademicGroup.Label : null,
            })
            .ToListAsync(cancellationToken);

        // Every attempt the student has ever made, found through the registration that carries it, so
        // a retake sitting under a later year's registration is still returned — with its own stage's
        // level, not the registration's.
        var attempts = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Registration.StudentId == request.StudentId)
            .Select(a => new
            {
                a.Id,
                a.RegistrationId,
                YearStart = a.Registration.AcademicYear.StartDate,
                StageId = a.Cohort.StageId,
                StageName = a.Cohort.Stage.Name,
                a.Cohort.Stage.Coefficient,
                StageLevelId = a.Cohort.Stage.LevelId,
                StageLevelLabel = a.Cohort.Stage.Level.Label,
                CohortId = a.CurrentCohortId,
                CohortLabel = a.Cohort.Label,
                a.Status,
                a.FinalScore,
                a.Result,
                // Interrupted rotations are excluded exactly as the domain excludes them from the
                // lifecycle: counting a rotation cut short by a transfer would pin the progress bar
                // one short of complete for good.
                StartDate = a.ServicePeriods.Where(p => !p.IsInterrupted).Min(p => (DateOnly?)p.StartDate),
                EndDate = a.ServicePeriods.Where(p => !p.IsInterrupted).Max(p => (DateOnly?)p.EndDate),
                PeriodsTotal = a.ServicePeriods.Count(p => !p.IsInterrupted),
                PeriodsComplete = a.ServicePeriods.Count(p => !p.IsInterrupted && p.IsComplete),
                AllPeriodsEvaluated = a.ServicePeriods.Any(p => !p.IsInterrupted)
                                   && a.ServicePeriods.All(p => p.IsInterrupted || p.Evaluation != null),
            })
            .ToListAsync(cancellationToken);

        // A retake is only recognisable across registrations: within one year the student sits a stage
        // once. Numbering by academic year makes "2ème tentative" mean the same thing on every screen.
        var attemptNumbers = attempts
            .GroupBy(a => a.StageId)
            .SelectMany(g => g
                .OrderBy(a => a.YearStart)
                .Select((a, index) => (a.Id, Number: index + 1)))
            .ToDictionary(x => x.Id, x => x.Number);

        var years = registrations
            .OrderByDescending(r => r.YearStart)
            .Select(r =>
            {
                var stages = attempts
                    .Where(a => a.RegistrationId == r.Id)
                    .OrderBy(a => a.StartDate ?? DateOnly.MaxValue)
                    .ThenBy(a => a.StageName)
                    .Select(a => new ParcoursStage(
                        a.Id,
                        a.StageId,
                        a.StageName,
                        a.Coefficient,
                        a.StageLevelId,
                        a.StageLevelLabel,
                        attemptNumbers[a.Id],
                        a.CohortId,
                        a.CohortLabel,
                        a.Status,
                        a.FinalScore,
                        a.Result,
                        a.StartDate,
                        a.EndDate,
                        a.PeriodsTotal,
                        a.PeriodsComplete,
                        a.AllPeriodsEvaluated))
                    .ToList();

                return new ParcoursYear(
                    r.Id,
                    r.AcademicYearId,
                    r.YearLabel,
                    r.LevelId,
                    r.LevelLabel,
                    r.LevelYear,
                    r.Status,
                    r.AcademicGroupId,
                    r.GroupLabel,
                    r.IsCurrent,
                    Tally(stages),
                    stages);
            })
            .ToList();

        var response = new StudentParcoursResponse(
            student.Id,
            $"{student.FirstName ?? ""} {student.LastName ?? ""}".Trim(),
            Tally(years.SelectMany(y => y.Stages).ToList()),
            years);

        return response;
    }

    // The verdict outranks the workflow status: an assignment the administration has not yet ratified
    // is already passed or failed the moment its last mark lands, and one whose rotations are over but
    // whose marks are incomplete is neither — it is awaiting its verdict, and must not keep counting
    // as "planned" the way the dashboard used to show it.
    private static ParcoursTotals Tally(IReadOnlyList<ParcoursStage> stages)
    {
        int validated = stages.Count(s => s.Result == StageAssignmentResult.Validé);
        int failed = stages.Count(s => s.Result == StageAssignmentResult.NonValidé);

        var undecided = stages
            .Where(s => s.Result is null or StageAssignmentResult.NonÉvalué)
            .ToList();

        int planned = undecided.Count(s => s.Status == InternshipStatus.Planned);
        int ongoing = undecided.Count(s => s.Status == InternshipStatus.Ongoing);

        return new ParcoursTotals(
            planned,
            ongoing,
            undecided.Count - planned - ongoing,
            validated,
            failed);
    }
}
