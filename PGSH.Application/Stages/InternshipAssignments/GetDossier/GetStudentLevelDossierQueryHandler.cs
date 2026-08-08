using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.GetDossier;

internal sealed class GetStudentLevelDossierQueryHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : IQueryHandler<GetStudentLevelDossierQuery, StudentLevelDossierResponse>
{
    public async Task<Result<StudentLevelDossierResponse>> Handle(
        GetStudentLevelDossierQuery request, CancellationToken cancellationToken)
    {
        var access = await authorizer.EnsureCanReadStudentDossierAsync(request.StudentId, cancellationToken);
        if (access.IsFailure)
            return Result.Failure<StudentLevelDossierResponse>(access.Error);

        var student = await dbContext.Students
            .AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Select(s => new { s.Id, s.FirstName, s.LastName, s.CNE, s.Appogee })
            .FirstOrDefaultAsync(cancellationToken);

        if (student is null)
            return Result.Failure<StudentLevelDossierResponse>(StudentErrors.NotFound(request.StudentId));

        var level = await dbContext.Levels
            .AsNoTracking()
            .Where(l => l.Id == request.LevelId)
            .Select(l => new { l.Id, l.Label })
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null)
            return Result.Failure<StudentLevelDossierResponse>(Error.NotFound(
                "Levels.NotFound", $"Level '{request.LevelId}' not found."));

        var registrations = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.StudentId == request.StudentId && r.LevelId == request.LevelId)
            .Select(r => new
            {
                r.Id,
                r.AcademicYearId,
                YearLabel = r.AcademicYear.Label,
                YearStart = r.AcademicYear.StartDate,
                r.Status,
                r.AcademicGroupId,
                GroupLabel = r.AcademicGroup != null ? r.AcademicGroup.Label : null,
            })
            .ToListAsync(cancellationToken);

        // Scoped by the STAGE's level, not the registration's. A retake of an earlier level's stage
        // hangs off the registration the student holds now — a 6th-year one for a 1st-year stage —
        // so filtering on the registration's level hid it from both dossiers at once: absent from
        // level 1 because that registration is 6th-year, and dropped from level 6 because the stage
        // is not in its list. The level-1 stage would then read ToRevalidate forever, even once the
        // retake had passed.
        var attempts = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Registration.StudentId == request.StudentId
                     && a.Cohort.Stage.LevelId == request.LevelId)
            .Select(a => new
            {
                a.Id,
                a.RegistrationId,
                StageId = a.Cohort.StageId,
                a.Status,
                a.FinalScore,
                a.Result,
                // Carried on the attempt: its registration may sit at another level, so it will not
                // be found in this level's registration list.
                a.Registration.AcademicYearId,
                YearLabel = a.Registration.AcademicYear.Label,
                YearStart = a.Registration.AcademicYear.StartDate,
            })
            .ToListAsync(cancellationToken);

        var stages = await dbContext.Stages
            .AsNoTracking()
            .Where(s => s.LevelId == request.LevelId)
            .Select(s => new { s.Id, s.Name, s.Coefficient })
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var stageRows = stages.Select(stage =>
        {
            var stageAttempts = attempts
                .Where(a => a.StageId == stage.Id)
                .OrderByDescending(a => a.YearStart)
                .Select(a => new DossierAttempt(
                    a.Id,
                    a.RegistrationId,
                    a.AcademicYearId,
                    a.YearLabel,
                    a.Status,
                    a.FinalScore,
                    a.Result))
                .ToList();

            return new DossierStage(
                stage.Id,
                stage.Name,
                stage.Coefficient,
                DeriveState(stageAttempts),
                stageAttempts.Count == 0 ? null : stageAttempts.Max(a => a.FinalScore),
                stageAttempts.Count,
                stageAttempts);
        }).ToList();

        var registrationRows = registrations
            .OrderByDescending(r => r.YearStart)
            .Select(r => new DossierRegistration(
                r.Id,
                r.AcademicYearId,
                r.YearLabel,
                r.Status,
                r.AcademicGroupId,
                r.GroupLabel,
                attempts.Count(a => a.RegistrationId == r.Id)))
            .ToList();

        int validated = stageRows.Count(s => s.State == DossierStageState.Validated);

        var response = new StudentLevelDossierResponse(
            student.Id,
            $"{student.FirstName ?? ""} {student.LastName ?? ""}".Trim(),
            student.CNE,
            student.Appogee,
            level.Id,
            level.Label,
            stageRows.Count,
            validated,
            stageRows.Count - validated,
            stageRows.Count > 0 && validated == stageRows.Count,
            registrationRows,
            stageRows);

        return response;
    }

    // A stage is owed until some attempt comes back Validé — one pass at any registration settles it
    // for good. Everything short of a NonValidé verdict is still live: an attempt sitting at
    // NonÉvalué may yet pass, so it must not be re-opened as a retake underneath the running one.
    private static DossierStageState DeriveState(IReadOnlyList<DossierAttempt> attempts)
    {
        if (attempts.Count == 0)
            return DossierStageState.NotAttempted;

        if (attempts.Any(a => a.Result == StageAssignmentResult.Validé))
            return DossierStageState.Validated;

        return attempts.All(a => a.Result == StageAssignmentResult.NonValidé)
            ? DossierStageState.ToRevalidate
            : DossierStageState.InProgress;
    }
}
