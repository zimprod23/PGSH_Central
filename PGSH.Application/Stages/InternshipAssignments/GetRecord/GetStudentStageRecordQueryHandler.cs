using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Attendance;
using PGSH.Application.Stages.Evaluations;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.InternshipAssignments.GetRecord;

internal sealed class GetStudentStageRecordQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetStudentStageRecordQuery, StudentStageRecordResponse>
{
    public async Task<Result<StudentStageRecordResponse>> Handle(
        GetStudentStageRecordQuery request, CancellationToken cancellationToken)
    {
        // Marks/verdicts are computed in memory via StageScoring (the same helper the domain uses), so
        // the graph is loaded rather than projected in SQL.
        var assignment = await dbContext.InternshipAssignments
            .AsNoTracking()
            .Include(a => a.Registration).ThenInclude(r => r.Student)
            .Include(a => a.Cohort).ThenInclude(c => c.Stage).ThenInclude(s => s.Level)
            .Include(a => a.Cohort).ThenInclude(c => c.AcademicGroup)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Service).ThenInclude(s => s.Hospital)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Evaluation!)
                .ThenInclude(e => e.ObjectiveScores).ThenInclude(o => o.StageObjective)
            .Include(a => a.ServicePeriods).ThenInclude(p => p.Attendance)
            .FirstOrDefaultAsync(a => a.Id == request.AssignmentId, cancellationToken);

        if (assignment is null)
            return Result.Failure<StudentStageRecordResponse>(StageErrors.AssignmentNotFound(request.AssignmentId));

        var gradable = assignment.ServicePeriods.Where(p => !p.IsInterrupted).ToList();
        bool allEvaluated = gradable.Count > 0 && gradable.All(p => p.Evaluation is not null);

        var periods = assignment.ServicePeriods
            .OrderBy(p => p.StartDate)
            .Select(ToPeriodRecord)
            .ToList();

        var student = assignment.Registration.Student;
        var response = new StudentStageRecordResponse(
            assignment.Id,
            assignment.RegistrationId,
            $"{student.FirstName ?? ""} {student.LastName ?? ""}".Trim(),
            student.Appogee,
            student.CNE,
            assignment.Cohort.StageId,
            assignment.Cohort.Stage.Name,
            assignment.Cohort.Stage.Level?.Label,
            assignment.CurrentCohortId,
            assignment.Cohort.Label,
            assignment.Cohort.AcademicGroup?.Label,
            assignment.Status,
            assignment.FinalScore,
            assignment.Result,
            allEvaluated,
            periods);

        return response;
    }

    private static StagePeriodRecord ToPeriodRecord(ServicePeriod p)
    {
        var attendance = p.Attendance
            .OrderBy(x => x.Date)
            .Select(x => new AttendanceResponse(x.Id, x.Date, x.Status.ToString()))
            .ToList();

        return new StagePeriodRecord(
            p.Id,
            p.ServiceId,
            p.Service.Name,
            p.Service.Hospital.Name,
            p.StartDate,
            p.EndDate,
            p.IsStarted,
            p.IsComplete,
            p.IsInterrupted,
            p.IsDelocalized,
            p.Evaluation is null ? null : StageScoring.PeriodMark(p.Evaluation),
            p.Evaluation is null ? null : StageScoring.IsPeriodValidated(p.Evaluation),
            p.Evaluation is null ? null : ToEvaluationResponse(p.Evaluation),
            attendance.Count(x => x.Status == nameof(AttendanceStatus.Present)),
            attendance.Count(x => x.Status == nameof(AttendanceStatus.Absent)),
            attendance.Count(x => x.Status == nameof(AttendanceStatus.JustifiedAbsent)),
            attendance.Count(x => x.Status == nameof(AttendanceStatus.Late)),
            attendance);
    }

    private static ServiceEvaluationResponse ToEvaluationResponse(ServiceEvaluation e) =>
        new(
            e.Id,
            e.ServicePeriodId,
            e.Mode,
            e.TotalScore,
            e.Outcome,
            e.SupervisorComment,
            e.ObjectiveScores
                .OrderBy(o => o.StageObjective.Weight)
                .Select(o => new ObjectiveScoreResponse(
                    o.Id,
                    o.StageObjectiveId,
                    o.StageObjective.Label,
                    o.StageObjective.Weight,
                    o.StageObjective.IsMandatory,
                    o.Score,
                    o.Outcome,
                    o.Note))
                .ToList(),
            e.FicheReference);
}
