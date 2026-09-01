using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Empty;

/// <summary>
/// Empties one roster — and, if asked, takes its affectations with it.
/// </summary>
/// <remarks>
/// <para>⚠ <b>The pointer is not the plan.</b> Clearing <c>Registration.AcademicGroupId</c> leaves
/// every <c>InternshipAssignment</c> where it was, because an affectation hangs off the cohorte, not
/// off the roster pointer. So the unguarded version of this handler produced a roster reading 0
/// étudiants whose cohortes still held every one of them — on the chefs' worklists, in the services'
/// occupancy, in the printed répartition — with nothing anywhere saying so.</para>
///
/// <para><b>Three outcomes, and the middle one is the point:</b> nothing planned → empties silently;
/// affectations merely planned → refused until the caller says to drop them, and the refusal names
/// how many; anything underway → refused outright. That last refusal is not forceable here. The act
/// that destroys marks and attendance is « Dépublier », which names what it costs and asks twice, and
/// a roster-side button must not become the way round it.</para>
/// </remarks>
internal sealed class EmptyGroupCommandHandler(
    IApplicationDbContext dbContext,
    AffectationTollReader tollReader)
    : ICommandHandler<EmptyGroupCommand, EmptyGroupReport>
{
    public async Task<Result<EmptyGroupReport>> Handle(
        EmptyGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.Id == request.GroupId)
            .Select(g => new { g.Label })
            .FirstOrDefaultAsync(cancellationToken);

        if (group is null)
            return Result.Failure<EmptyGroupReport>(AcademicGroupErrors.NotFound(request.GroupId));

        string groupLabel = group.Label ?? $"Groupe {request.GroupId}";

        var toll = await tollReader.ForRosterAsync(request.GroupId, cancellationToken);

        if (toll.IsUnderway)
            return Result.Failure<EmptyGroupReport>(AcademicGroupErrors.RosterAffectationsUnderway(
                groupLabel, toll.Periods, toll.Started, toll.Evaluated, toll.AttendanceDays));

        if (!toll.IsEmpty && !request.DropAffectations)
            return Result.Failure<EmptyGroupReport>(AcademicGroupErrors.RosterHasAffectations(
                groupLabel, toll.Assignments, toll.Periods));

        int affectationsRemoved = 0;
        int periodsRemoved = 0;

        if (!toll.IsEmpty)
        {
            // Tracked rather than ExecuteDelete: a roster is 6-7 students over a handful of cohortes,
            // so the cost is nil — and the bulk helpers are unsupported by the in-memory provider,
            // which would leave the one branch that destroys rows unreachable by any test.
            var assignments = await dbContext.InternshipAssignments
                .Where(a => a.Cohort.AcademicGroupId == request.GroupId)
                .Include(a => a.ServicePeriods)
                .Include(a => a.MembershipHistory)
                .ToListAsync(cancellationToken);

            affectationsRemoved = assignments.Count;
            periodsRemoved = assignments.Sum(a => a.ServicePeriods.Count);

            foreach (var assignment in assignments)
                dbContext.ServicePeriods.RemoveRange(assignment.ServicePeriods);

            dbContext.InternshipAssignments.RemoveRange(assignments);

            // Memberships pointing *into* this roster's cohortes from assignments that live elsewhere:
            // the trace a temporary transfer leaves behind. They are not covered by the cascade above
            // and their cohorte is about to have nobody in it.
            var visitingMemberships = await dbContext.CohortMembership
                .Where(m => m.Cohort.AcademicGroupId == request.GroupId)
                .ToListAsync(cancellationToken);

            dbContext.CohortMembership.RemoveRange(visitingMemberships);
        }

        var registrations = await dbContext.Registrations
            .Where(r => r.AcademicGroupId == request.GroupId)
            .ToListAsync(cancellationToken);

        foreach (var registration in registrations)
            registration.AcademicGroupId = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new EmptyGroupReport(registrations.Count, affectationsRemoved, periodsRemoved);
    }
}
