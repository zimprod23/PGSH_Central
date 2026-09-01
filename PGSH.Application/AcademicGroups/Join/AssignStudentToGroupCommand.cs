using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Planning;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;
using System.Text.Json;

namespace PGSH.Application.AcademicGroups.Join;

/// <summary>
/// Puts a registration that has <b>no</b> roster into one, and gives the student the rotations that
/// roster still has ahead of it.
/// </summary>
/// <remarks>
/// <para>⚠ <b>This is not a transfer, and the two must not be fused.</b> A transfer moves a student who
/// is already somewhere: it interrupts the rotation he is standing in, rehomes the future ones and
/// leaves the completed ones alone. A late arrival has none of that — running him through the transfer
/// path silently did nothing at all, because every step of it filters on assignments he does not have,
/// and he ended up on the roster with no cohorte and no période: a student the planning had never heard
/// of, in a group that looked correct.</para>
///
/// <para>It is the answer to the ordinary September case: the déliberation is applied, the groups are
/// cut, the schedule is published — and then somebody registers.</para>
/// </remarks>
public sealed record AssignStudentToGroupCommand(
    Guid RegistrationId,
    int AcademicGroupId,
    string? Reason = null) : ICommand<GroupJoinReport>, IAuditableCommand
{
    public string AuditAction => "STUDENT_JOINED_GROUP";
    public string AuditEntityType => "Registration";
    public string? AuditEntityId => RegistrationId.ToString();

    public string? AuditMetadata =>
        JsonSerializer.Serialize(new { academicGroupId = AcademicGroupId, reason = Reason });
}

/// <summary>
/// What joining actually produced. <see cref="StagesAlreadyOver"/> is the number worth reading: those
/// stages are owed and unserved, and somebody has to decide between a délocalisation, a revalidation
/// and letting the student take them next year.
/// </summary>
public sealed record GroupJoinReport(
    string GroupLabel,
    int CohortsJoined,
    int PeriodsCreated,
    int StagesAlreadyOver);

internal sealed class AssignStudentToGroupCommandValidator : AbstractValidator<AssignStudentToGroupCommand>
{
    public AssignStudentToGroupCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.AcademicGroupId).GreaterThan(0);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

internal sealed class AssignStudentToGroupCommandHandler(
    IApplicationDbContext dbContext,
    StudentAffectationService affectation,
    LateArrivalScheduler scheduler,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<AssignStudentToGroupCommand, GroupJoinReport>
{
    public async Task<Result<GroupJoinReport>> Handle(
        AssignStudentToGroupCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(RegistrationErrors.GroupingNotAllowed);
        if (access.IsFailure)
            return Result.Failure<GroupJoinReport>(access.Error);

        var registration = await dbContext.Registrations
            .FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);

        if (registration is null)
            return Result.Failure<GroupJoinReport>(RegistrationErrors.NotFound(request.RegistrationId));

        if (registration.AcademicGroupId is { } current)
            return Result.Failure<GroupJoinReport>(AcademicGroupErrors.AlreadyInAGroup(
                await GroupLabelAsync(current, cancellationToken)));

        if (registration.Status.EndsTheCursus())
            return Result.Failure<GroupJoinReport>(
                AcademicGroupErrors.CursusEndedCannotJoin(registration.Status.ToString()));

        var target = await dbContext.AcademicGroups
            .AsNoTracking()
            .Where(g => g.Id == request.AcademicGroupId)
            .Select(g => new { g.Label, g.AcademicYearId, g.LevelId })
            .FirstOrDefaultAsync(cancellationToken);

        if (target is null)
            return Result.Failure<GroupJoinReport>(AcademicGroupErrors.NotFound(request.AcademicGroupId));

        string groupLabel = target.Label ?? $"Groupe {request.AcademicGroupId}";

        // The same two guards a transfer makes, and for the same reason: every check downstream is
        // keyed on the roster the registration claims, so a roster of another year or another
        // promotion is never caught again after this point.
        if (target.AcademicYearId != registration.AcademicYearId)
            return Result.Failure<GroupJoinReport>(AcademicGroupErrors.TargetGroupInAnotherYear(
                groupLabel,
                await YearLabelAsync(target.AcademicYearId, cancellationToken),
                await YearLabelAsync(registration.AcademicYearId, cancellationToken)));

        // A level-less target is « Non réparti » — the bucket that belongs to no promotion. Joining it
        // is a legitimate way of parking a registration, and it carries no cohorte, so nothing follows.
        if (target.LevelId is { } targetLevel && targetLevel != registration.LevelId)
            return Result.Failure<GroupJoinReport>(AcademicGroupErrors.TargetGroupInAnotherLevel(
                groupLabel,
                await LevelLabelAsync(targetLevel, cancellationToken),
                await LevelLabelAsync(registration.LevelId, cancellationToken)));

        registration.TransferToGroup(request.AcademicGroupId, request.Reason);

        var created = await affectation.AssignRegistrationAsync(
            registration, request.AcademicGroupId, cancellationToken);

        var outcome = await scheduler.MaterializeRemainingAsync(
            created, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new GroupJoinReport(
            groupLabel, created.Count, outcome.PeriodsCreated, outcome.WindowsAlreadyClosed);
    }

    private async Task<string> GroupLabelAsync(int groupId, CancellationToken ct) =>
        await dbContext.AcademicGroups
            .Where(g => g.Id == groupId)
            .Select(g => g.Label)
            .FirstOrDefaultAsync(ct) ?? $"Groupe {groupId}";

    private async Task<string> YearLabelAsync(int academicYearId, CancellationToken ct) =>
        await dbContext.AcademicYears
            .Where(y => y.Id == academicYearId)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(ct) ?? $"année {academicYearId}";

    private async Task<string> LevelLabelAsync(int levelId, CancellationToken ct) =>
        await dbContext.Levels
            .Where(l => l.Id == levelId)
            .Select(l => l.Label)
            .FirstOrDefaultAsync(ct) ?? $"niveau {levelId}";
}
