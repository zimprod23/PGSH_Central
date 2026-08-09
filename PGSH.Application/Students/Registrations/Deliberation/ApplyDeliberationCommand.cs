using System.Text.Json;
using FluentValidation;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Deliberation;

/// <summary>
/// Closes a promotion's academic year with the verdicts pronounced in deliberation.
///
/// <para>What survives is the verdict on each registration plus this command's audit entry naming the
/// promotion, the author and the date. The uploaded file is not stored: it is the jury's working
/// document, and the authoritative record afterwards is the registrations themselves.</para>
/// </summary>
public sealed record ApplyDeliberationCommand(
    int LevelId,
    IReadOnlyList<DeliberationRow> Rows,
    int? AcademicYearId = null) : ICommand<DeliberationReport>, IAuditableCommand
{
    public string AuditAction => "DELIBERATION_APPLIED";
    public string AuditEntityType => "Level";
    public string? AuditEntityId => LevelId.ToString();

    public string? AuditMetadata =>
        JsonSerializer.Serialize(new { levelId = LevelId, academicYearId = AcademicYearId, rows = Rows.Count });
}

internal sealed class ApplyDeliberationCommandValidator : AbstractValidator<ApplyDeliberationCommand>
{
    public ApplyDeliberationCommandValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.Rows).NotEmpty().WithMessage("Le fichier ne contient aucune ligne.");
    }
}

/// <summary>
/// Runs the same planner the preview ran, refuses outright if anything at all is wrong, and writes
/// every verdict through <see cref="Registration.RecordYearOutcome"/> — so the timeline entry and the
/// declared-versus-inferred marker are identical to a verdict entered one at a time. One SaveChanges,
/// so the whole promotion closes or none of it does.
/// </summary>
internal sealed class ApplyDeliberationCommandHandler(
    IApplicationDbContext dbContext,
    DeliberationPlanner planner,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ApplyDeliberationCommand, DeliberationReport>
{
    public async Task<Result<DeliberationReport>> Handle(
        ApplyDeliberationCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(DeliberationErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<DeliberationReport>(access.Error);

        var plan = await planner.PlanAsync(
            request.LevelId, request.AcademicYearId, request.Rows, cancellationToken);

        if (plan.IsFailure)
            return Result.Failure<DeliberationReport>(plan.Error);

        var report = plan.Value.Report;
        if (!report.CanApply)
            return Result.Failure<DeliberationReport>(DeliberationErrors.Rejected(report.ErrorCount));

        var recordedOn = DateTime.UtcNow;

        foreach (var item in plan.Value.Work)
        {
            var motif = item.Motif is null ? null : new FailureReasons(item.Motif, []);

            var result = item.Registration.RecordYearOutcome(
                item.Outcome, RegistrationOutcomeSource.Declared, motif, recordedOn);

            // The planner already cleared every guard this can return, so a failure here means the
            // plan and the entity disagree — refuse the batch rather than write part of it.
            if (result.IsFailure)
                return Result.Failure<DeliberationReport>(result.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return report;
    }
}
