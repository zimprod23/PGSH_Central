using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;

namespace PGSH.Application.Stages.Planning;

/// <summary>
/// What a set of <see cref="InternshipAssignment"/>s is actually carrying — the number a destructive
/// act has to name before it is allowed to happen.
/// </summary>
/// <remarks>
/// <para>⚠ <b>An affectation does not hang off the roster pointer.</b> An
/// <see cref="InternshipAssignment"/> is (inscription × cohorte), and a <see cref="ServicePeriod"/>
/// hangs off the affectation — so unhooking <c>Registration.AcademicGroupId</c> leaves every one of
/// them in place, still on the chef's worklist, still counted against the service's occupancy, while
/// the roster reads empty. Two screens disagreeing with nothing on either to say so is exactly the
/// state this record exists to make visible.</para>
///
/// <para><b>The four period counts are the four the unpublish path already names</b>
/// (<c>StageErrors.ScheduleUnderway</c>), deliberately: a refusal that says « déjà engagé » without
/// saying how much is one nobody can act on, and two acts that destroy the same rows must not
/// describe them differently.</para>
/// </remarks>
/// <param name="Assignments">Affectations in scope, whatever their state.</param>
/// <param name="Engaged">
/// Affectations past <see cref="InternshipStatus.Planned"/>. Kept beside the period counts rather
/// than derived from them: a status can be terminal (<c>Validated</c>, <c>Rejected</c>) over périodes
/// that were since removed, and that is still a verdict nobody may delete by a side effect.
/// </param>
/// <param name="Periods">Périodes de service on those affectations, grid-linked and ad-hoc alike.</param>
/// <param name="Started">Of those, the ones a student is actually standing in.</param>
/// <param name="Evaluated">Of those, the ones carrying a chef's mark.</param>
/// <param name="AttendanceDays">Journées de présence recorded against them.</param>
internal sealed record AffectationToll(
    int Assignments,
    int Engaged,
    int Periods,
    int Started,
    int Evaluated,
    int AttendanceDays)
{
    public static readonly AffectationToll None = new(0, 0, 0, 0, 0, 0);

    public bool IsEmpty => Assignments == 0;

    /// <summary>
    /// Something happened here. Past this line the rows are no longer a plan that planning again
    /// could rebuild — they are the record of what students did, and <c>ServiceEvaluation</c>,
    /// <c>AttendanceRecord</c>, <c>PeriodPause</c> and <c>Delocalization</c> all cascade away with the
    /// périodes that carry them.
    /// </summary>
    public bool IsUnderway => Engaged > 0 || Started > 0 || Evaluated > 0 || AttendanceDays > 0;
}

/// <summary>
/// Reads an <see cref="AffectationToll"/> over the three scopes that can strand or destroy
/// affectations: some cohortes, one roster, a year's rosters. Shared so the roster-side acts and the
/// cohorte-side acts cannot disagree about what a refusal is counting.
/// </summary>
internal sealed class AffectationTollReader(IApplicationDbContext dbContext)
{
    internal sealed record AssignmentCounts(int Total, int Engaged);

    internal sealed record PeriodCounts(int Periods, int Started, int Evaluated, int AttendanceDays);

    public Task<AffectationToll> ForCohortsAsync(IReadOnlyCollection<int> cohortIds, CancellationToken ct) =>
        cohortIds.Count == 0
            ? Task.FromResult(AffectationToll.None)
            : ReadAsync(AssignmentsOfCohortsQuery(dbContext, cohortIds), ct);

    public Task<AffectationToll> ForRosterAsync(int academicGroupId, CancellationToken ct) =>
        ReadAsync(AssignmentsOfRosterQuery(dbContext, academicGroupId), ct);

    public Task<AffectationToll> ForYearRostersAsync(int academicYearId, CancellationToken ct) =>
        ReadAsync(AssignmentsOfYearRostersQuery(dbContext, academicYearId), ct);

    /// <remarks>
    /// Reached through the <b>cohorte</b>, never through <c>Registration.AcademicGroupId</c>: the
    /// pointer is the thing being removed, so counting through it would report zero at exactly the
    /// moment the count matters. The cohorte is the plan-side fact and it survives the act.
    /// </remarks>
    internal static IQueryable<InternshipAssignment> AssignmentsOfRosterQuery(
        IApplicationDbContext dbContext, int academicGroupId) =>
        dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Cohort.AcademicGroupId == academicGroupId);

    internal static IQueryable<InternshipAssignment> AssignmentsOfYearRostersQuery(
        IApplicationDbContext dbContext, int academicYearId) =>
        dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => a.Cohort.AcademicGroup.AcademicYearId == academicYearId);

    internal static IQueryable<InternshipAssignment> AssignmentsOfCohortsQuery(
        IApplicationDbContext dbContext, IReadOnlyCollection<int> cohortIds) =>
        dbContext.InternshipAssignments
            .AsNoTracking()
            .Where(a => cohortIds.Contains(a.CurrentCohortId));

    internal static IQueryable<AssignmentCounts> AssignmentCountsQuery(
        IQueryable<InternshipAssignment> assignments) =>
        assignments
            .GroupBy(_ => 1)
            .Select(g => new AssignmentCounts(
                g.Count(),
                g.Count(a => a.Status != InternshipStatus.Planned)));

    /// <remarks>
    /// ⚠ Its own round trip rather than more columns on the one above. These counts fold an aggregate
    /// over a collection navigation, and nesting that inside a second aggregate over the affectations
    /// is the shape Npgsql refuses. <c>SelectMany</c> onto the périodes keeps both queries flat — the
    /// shape <c>UnpublishCohortScheduleCommandHandler</c> has run on the real base.
    /// </remarks>
    internal static IQueryable<PeriodCounts> PeriodCountsQuery(
        IQueryable<InternshipAssignment> assignments) =>
        assignments
            .SelectMany(a => a.ServicePeriods)
            .GroupBy(_ => 1)
            .Select(g => new PeriodCounts(
                g.Count(),
                g.Count(p => p.IsStarted),
                g.Count(p => p.Evaluation != null),
                g.Sum(p => p.Attendance.Count)));

    private static async Task<AffectationToll> ReadAsync(
        IQueryable<InternshipAssignment> assignments, CancellationToken ct)
    {
        var counts = await AssignmentCountsQuery(assignments).FirstOrDefaultAsync(ct);

        if (counts is null)
            return AffectationToll.None;

        var periods = await PeriodCountsQuery(assignments).FirstOrDefaultAsync(ct);

        return new AffectationToll(
            counts.Total,
            counts.Engaged,
            periods?.Periods ?? 0,
            periods?.Started ?? 0,
            periods?.Evaluated ?? 0,
            periods?.AttendanceDays ?? 0);
    }
}
