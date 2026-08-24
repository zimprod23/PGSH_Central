using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Progression;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.FinalYear;

/// <summary>One recorded exception to « la dernière année ne commence pas avant que tout soit validé ».</summary>
/// <param name="OutstandingAtGrant">
/// What was owed the day it was granted. Deliberately a snapshot, not a live count: by the time this
/// is read the stage may have been revalidated or dropped by a new text, and a waiver that cannot say
/// what it excused is not a record of anything.
/// </param>
public sealed record FinalYearWaiverResponse(
    Guid Id,
    Guid StudentId,
    string StudentFullName,
    string? Cne,
    int AcademicYearId,
    string AcademicYearLabel,
    string Reason,
    int OutstandingAtGrant,
    string? OutstandingSummary,
    DateTime GrantedOn,
    bool Used);

/// <summary>
/// The waivers on record, optionally for one year or one student. Not paginated: an exception is by
/// definition rare, and a faculty granting enough of them to need a page has a different problem.
/// </summary>
public sealed record GetFinalYearWaiversQuery(
    int? AcademicYearId = null,
    Guid? StudentId = null) : IQuery<IReadOnlyList<FinalYearWaiverResponse>>;

internal sealed class GetFinalYearWaiversQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetFinalYearWaiversQuery, IReadOnlyList<FinalYearWaiverResponse>>
{
    public async Task<Result<IReadOnlyList<FinalYearWaiverResponse>>> Handle(
        GetFinalYearWaiversQuery request, CancellationToken ct)
    {
        var rows = await dbContext.FinalYearEntryWaivers
            .AsNoTracking()
            .Where(w => request.AcademicYearId == null || w.AcademicYearId == request.AcademicYearId)
            .Where(w => request.StudentId == null || w.StudentId == request.StudentId)
            .OrderByDescending(w => w.GrantedOn)
            .Select(w => new FinalYearWaiverResponse(
                w.Id,
                w.StudentId,
                ((w.Student.FirstName ?? "") + " " + (w.Student.LastName ?? "")).Trim(),
                w.Student.CNE,
                w.AcademicYearId,
                w.AcademicYear.Label,
                w.Reason,
                w.OutstandingAtGrant,
                w.OutstandingSummary,
                w.GrantedOn,
                // Whether the registration it permits now exists — which is also what makes it
                // irrevocable, so the screen can disable the control rather than let it fail.
                dbContext.Registrations.Any(r =>
                    r.StudentId == w.StudentId && r.AcademicYearId == w.AcademicYearId)))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<FinalYearWaiverResponse>>(rows);
    }
}

/// <summary>
/// What one student still owes across his whole cursus — what the gate reads, exposed so a screen can
/// show the same list before anyone decides between revalidating and waiving.
/// </summary>
public sealed record GetOutstandingStagesQuery(Guid StudentId)
    : IQuery<IReadOnlyList<OutstandingStageResponse>>;

public sealed record OutstandingStageResponse(int StageId, string StageName, int LevelYear, string LevelLabel);

internal sealed class GetOutstandingStagesQueryHandler(OutstandingStageFinder finder)
    : IQueryHandler<GetOutstandingStagesQuery, IReadOnlyList<OutstandingStageResponse>>
{
    public async Task<Result<IReadOnlyList<OutstandingStageResponse>>> Handle(
        GetOutstandingStagesQuery request, CancellationToken ct)
    {
        var debts = await finder.ForStudentAsync(request.StudentId, ct);

        return Result.Success<IReadOnlyList<OutstandingStageResponse>>(
            debts.Select(d => new OutstandingStageResponse(d.StageId, d.StageName, d.LevelYear, d.LevelLabel))
                 .ToList());
    }
}
