using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Extensions;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Hospitals.Services.Occupancy;

/// <summary>
/// The students physically in a service between two dates, named.
///
/// <para>Reached by drilling into one segment of the occupancy timeline, so the window comes from
/// the caller rather than from a period: a segment is cut at window boundaries and generally does
/// not coincide with any single <c>StageSlot</c>.</para>
///
/// <para>⚠ <b>Paginated, and not optional.</b> A saturated segment on the current data holds 85
/// students, and the whole reason to look at one is that it is over capacity — the rows are most
/// numerous exactly where someone will want to read them.</para>
/// </summary>
public sealed record GetServiceOccupantsQuery(
    int       ServiceId,
    DateOnly  StartDate,
    DateOnly  EndDate,
    int?      LevelId    = null,
    int?      StageId    = null,
    int       PageNumber = 1,
    int       PageSize   = 25,
    string?   SearchTerm = null) : IQuery<PaginatedResponse<ServiceOccupantResponse>>;

public sealed record ServiceOccupantResponse(
    Guid   StudentId,
    string FirstName,
    string LastName,
    string? Cne,
    int    StageId,
    string StageName,
    string LevelLabel,
    int    GroupNumber,
    string GroupLabel,
    int    PeriodNumber,
    DateOnly StartDate,
    DateOnly EndDate);

internal sealed class GetServiceOccupantsQueryHandler(IApplicationDbContext dbContext)
    : IQueryHandler<GetServiceOccupantsQuery, PaginatedResponse<ServiceOccupantResponse>>
{
    public async Task<Result<PaginatedResponse<ServiceOccupantResponse>>> Handle(
        GetServiceOccupantsQuery request, CancellationToken cancellationToken)
    {
        bool serviceExists = await dbContext.Services
            .AnyAsync(s => s.Id == request.ServiceId, cancellationToken);

        if (!serviceExists)
            return Result.Failure<PaginatedResponse<ServiceOccupantResponse>>(
                ServiceErrors.NotFound(request.ServiceId));

        // Read off the planned cells rather than off ServicePeriods: a plan is worth inspecting
        // before it is published, and an unpublished cell has no ServicePeriod yet. A student counts
        // as present when his cohort's cell covers any day of the window — the same overlap test the
        // timeline and the capacity guard use.
        var query = dbContext.CohortSlotAssignments
            .AsNoTracking()
            .Where(a => a.ServiceId == request.ServiceId
                     && a.StageSlot.StartDate <= request.EndDate
                     && request.StartDate <= a.StageSlot.EndDate)
            .SelectMany(a => a.Cohort.Assignments
                .Where(x => x.Registration.Status != RegistrationStatus.Withdrawn)
                .Select(x => new
                {
                    x.Registration.Student,
                    a.Cohort.StageId,
                    StageName  = a.Cohort.Stage.Name,
                    LevelLabel = a.Cohort.Stage.Level.Label ?? ("niveau " + a.Cohort.Stage.LevelId),
                    LevelId    = a.Cohort.Stage.LevelId,
                    a.Cohort.AcademicGroup.GroupNumber,
                    GroupLabel = a.Cohort.AcademicGroup.Label,
                    a.StageSlot.PeriodNumber,
                    a.StageSlot.StartDate,
                    a.StageSlot.EndDate,
                }));

        if (request.LevelId is { } levelId)
            query = query.Where(x => x.LevelId == levelId);

        if (request.StageId is { } stageId)
            query = query.Where(x => x.StageId == stageId);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(x =>
                x.Student.FirstName.ToLower().Contains(term)
                || x.Student.LastName.ToLower().Contains(term)
                || (x.Student.CNE != null && x.Student.CNE.ToLower().Contains(term)));
        }

        var response = await query
            .OrderBy(x => x.LevelLabel)
            .ThenBy(x => x.GroupNumber)
            .ThenBy(x => x.Student.LastName)
            .ThenBy(x => x.Student.FirstName)
            .ToPaginatedResponseAsync(
                request.PageNumber,
                request.PageSize,
                x => new ServiceOccupantResponse(
                    x.Student.Id,
                    x.Student.FirstName,
                    x.Student.LastName,
                    x.Student.CNE,
                    x.StageId,
                    x.StageName,
                    x.LevelLabel,
                    x.GroupNumber,
                    x.GroupLabel,
                    x.PeriodNumber,
                    x.StartDate,
                    x.EndDate),
                cancellationToken);

        return Result.Success(response);
    }
}
