using System.Text.Json;
using FluentValidation;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Deliberation;

/// <summary>
/// Closes an academic year with the verdicts pronounced in deliberation.
///
/// <para>What survives is the verdict on each registration plus this command's audit entry naming the
/// scope, the author and the date. The uploaded file is not stored: it is the jury's working
/// document, and the authoritative record afterwards is the registrations themselves.</para>
/// </summary>
/// <param name="ConfirmedDefaultCount">
/// How many students the caller was shown as being admitted <em>by silence</em>. Required whenever the
/// scope defaults unlisted students, and refused when it does not match what the plan computes.
/// ⚠ A plain boolean would not do: the danger of an exceptions file is a student nobody named, and a
/// registration created between the preview and the apply adds one. The number the user confirmed is
/// the only thing that catches that, and it costs one comparison.
/// </param>
public sealed record ApplyDeliberationCommand(
    IReadOnlyList<DeliberationRow> Rows,
    int? LevelId = null,
    int? AcademicYearId = null,
    bool DefaultUnlistedToAdmis = false,
    int? ConfirmedDefaultCount = null) : ICommand<DeliberationReport>, IAuditableCommand
{
    public DeliberationScope Scope => new(LevelId, AcademicYearId, DefaultUnlistedToAdmis);

    public string AuditAction => "DELIBERATION_APPLIED";
    public string AuditEntityType => LevelId is null ? "AcademicYear" : "Level";
    public string? AuditEntityId => (LevelId ?? AcademicYearId)?.ToString();

    public string? AuditMetadata =>
        JsonSerializer.Serialize(new
        {
            levelId = LevelId,
            academicYearId = AcademicYearId,
            rows = Rows.Count,
            defaultUnlisted = DefaultUnlistedToAdmis,
            confirmedDefaults = ConfirmedDefaultCount,
        });
}

internal sealed class ApplyDeliberationCommandValidator : AbstractValidator<ApplyDeliberationCommand>
{
    public ApplyDeliberationCommandValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0).When(x => x.LevelId is not null);
        RuleFor(x => x.Rows).NotEmpty().WithMessage(DeliberationErrors.EmptySheetMessage);
    }
}

/// <summary>
/// Runs the same planner the preview ran, refuses outright if anything at all is wrong, and writes
/// every verdict through <see cref="Registration.RecordYearOutcome"/> — so the timeline entry and the
/// declared-versus-inferred marker are identical to a verdict entered one at a time. One SaveChanges,
/// so the whole year closes or none of it does.
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

        var plan = await planner.PlanAsync(request.Scope, request.Rows, cancellationToken);

        if (plan.IsFailure)
            return Result.Failure<DeliberationReport>(plan.Error);

        var report = plan.Value.Report;
        if (!report.CanApply)
            return Result.Failure<DeliberationReport>(
                DeliberationErrors.Rejected(report.ErrorCount));

        if (report.DefaultedCount > 0 && request.ConfirmedDefaultCount != report.DefaultedCount)
            return Result.Failure<DeliberationReport>(DeliberationErrors.DefaultsNotConfirmed(
                report.DefaultedCount, request.ConfirmedDefaultCount));

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
