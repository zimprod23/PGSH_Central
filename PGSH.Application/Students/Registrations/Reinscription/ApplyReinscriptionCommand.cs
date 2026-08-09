using System.Text.Json;
using FluentValidation;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Reinscription;

/// <summary>
/// Creates the next year's registrations from the promotion's closed verdicts.
/// </summary>
public sealed record ApplyReinscriptionCommand(
    int FromAcademicYearId,
    int ToAcademicYearId,
    int LevelId) : ICommand<ReinscriptionReport>, IAuditableCommand
{
    public string AuditAction => "REINSCRIPTION_APPLIED";
    public string AuditEntityType => "Level";
    public string? AuditEntityId => LevelId.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new
    {
        fromAcademicYearId = FromAcademicYearId,
        toAcademicYearId = ToAcademicYearId,
        levelId = LevelId,
    });
}

internal sealed class ApplyReinscriptionCommandValidator : AbstractValidator<ApplyReinscriptionCommand>
{
    public ApplyReinscriptionCommandValidator()
    {
        RuleFor(x => x.FromAcademicYearId).GreaterThan(0);
        RuleFor(x => x.ToAcademicYearId).GreaterThan(0);
        RuleFor(x => x.LevelId).GreaterThan(0);
    }
}

internal sealed class ApplyReinscriptionCommandHandler(
    IApplicationDbContext dbContext,
    ReinscriptionPlanner planner,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ApplyReinscriptionCommand, ReinscriptionReport>
{
    public async Task<Result<ReinscriptionReport>> Handle(
        ApplyReinscriptionCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(ReinscriptionErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<ReinscriptionReport>(access.Error);

        var plan = await planner.PlanAsync(
            request.FromAcademicYearId, request.ToAcademicYearId, request.LevelId, cancellationToken);

        if (plan.IsFailure)
            return Result.Failure<ReinscriptionReport>(plan.Error);

        var registeredOn = DateTime.UtcNow;

        foreach (var item in plan.Value.Work)
        {
            var registration = new Registration
            {
                Id = Guid.NewGuid(),
                StudentId = item.StudentId,
                AcademicYearId = plan.Value.ToAcademicYearId,
                LevelId = item.LevelId,
                // Active, not Pending: nothing in the app filters planning by this field, so a Pending
                // registration would be grouped and planned exactly like an active one while claiming
                // not to be enrolled. Active is also what the year means — in progress.
                Status = RegistrationStatus.Active,
                RegistrationDate = registeredOn,
                // No group: répartition is AutoArrangeGroupsCommand's job and runs after this, which is
                // what puts these students in the "Non réparti" bucket it reads from.
                AcademicGroupId = null,
            };

            registration.Raise(new StudentRegisteredDomainEvent(
                registration.Id, item.StudentId, item.LevelId, plan.Value.ToAcademicYearId));

            dbContext.Registrations.Add(registration);
        }

        if (plan.Value.Work.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return plan.Value.Report;
    }
}
