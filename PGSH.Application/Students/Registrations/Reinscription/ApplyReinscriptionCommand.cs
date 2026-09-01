using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Stages.Cnpn;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;
using System.Text.Json;

namespace PGSH.Application.Students.Registrations.Reinscription;

/// <summary>
/// Creates the next year's registrations from the closed verdicts of the year that is ending.
/// </summary>
/// <param name="LevelId">One promotion, or every promotion of the closing year when omitted.</param>
public sealed record ApplyReinscriptionCommand(
    int FromAcademicYearId,
    int ToAcademicYearId,
    int? LevelId = null) : ICommand<ReinscriptionReport>, IAuditableCommand
{
    public string AuditAction => "REINSCRIPTION_APPLIED";
    public string AuditEntityType => LevelId is null ? "AcademicYear" : "Level";
    public string? AuditEntityId => (LevelId ?? FromAcademicYearId).ToString();

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
        RuleFor(x => x.LevelId).GreaterThan(0).When(x => x.LevelId is not null);
    }
}

internal sealed class ApplyReinscriptionCommandHandler(
    IApplicationDbContext dbContext,
    ReinscriptionPlanner planner,
    RegistrationCnpnStamper stamper,
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
        var created = new List<Registration>(plan.Value.Work.Count);

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
            created.Add(registration);
        }

        if (created.Count > 0)
        {
            // The rollover is where an effectivity rule authored over the summer actually bites: it
            // is the act that creates next year's registrations, and a repeater re-entering the level
            // the rule names is stamped here rather than by anyone remembering to run a command.
            await stamper.StampAsync(created, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return plan.Value.Report;
    }
}
