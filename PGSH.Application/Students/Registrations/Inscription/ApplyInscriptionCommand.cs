using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;
using System.Text.Json;

namespace PGSH.Application.Students.Registrations.Inscription;

/// <summary>
/// Inscribes into one promotion the students the déliberation and the réinscription cannot reach:
/// the September intake, transfers arriving from another faculty, students coming back after an
/// absence, and réorientations.
///
/// <para>What survives is the students and their registrations, the équivalences recorded for those
/// who arrived from outside, and this command's audit entry naming the promotion, the author and the
/// date. The uploaded file is not stored — the authoritative record afterwards is the rows themselves.</para>
/// </summary>
/// <param name="ConfirmedStudentCount">
/// How many <b>people</b> the caller was shown as being created. Required whenever the plan creates
/// any, and refused when it does not match what the plan computes.
/// <para>⚠ The déliberation asks for a number rather than a checkbox because an omission promotes
/// somebody. Here the stake is higher: a student row is an identity — a CNE, a numéro Apogée and an
/// e-mail a Keycloak login will be matched against — and nothing puts a wrongly-created promotion
/// back. A file edited between the preview and the apply is exactly what this comparison catches.</para>
/// <para>It is asked of the <em>file</em> path only. <see cref="InscribeStudentCommand"/> names one
/// person in a form, with no preview and nothing in between to change: there is no number to confirm
/// that the request does not already state.</para>
/// </param>
public sealed record ApplyInscriptionCommand(
    IReadOnlyList<InscriptionRow> Rows,
    int LevelId,
    int? AcademicYearId = null,
    int? ConfirmedStudentCount = null) : ICommand<InscriptionReport>, IAuditableCommand
{
    public InscriptionScope Scope => new(LevelId, AcademicYearId);

    public string AuditAction => "INSCRIPTION_APPLIED";
    public string AuditEntityType => "Level";
    public string? AuditEntityId => LevelId.ToString();

    public string? AuditMetadata =>
        JsonSerializer.Serialize(new
        {
            levelId = LevelId,
            academicYearId = AcademicYearId,
            rows = Rows.Count,
            confirmedStudents = ConfirmedStudentCount,
        });
}

internal sealed class ApplyInscriptionCommandValidator : AbstractValidator<ApplyInscriptionCommand>
{
    public ApplyInscriptionCommandValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.Rows).NotEmpty().WithMessage(InscriptionErrors.EmptySheetMessage);
    }
}

/// <summary>
/// Runs the same planner the preview ran, refuses outright if anything at all is wrong, and hands the
/// plan to <see cref="InscriptionApplier"/> — the one place that writes.
/// </summary>
internal sealed class ApplyInscriptionCommandHandler(
    InscriptionPlanner planner,
    InscriptionApplier applier,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ApplyInscriptionCommand, InscriptionReport>
{
    public async Task<Result<InscriptionReport>> Handle(
        ApplyInscriptionCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(InscriptionErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<InscriptionReport>(access.Error);

        var planned = await planner.PlanAsync(request.Scope, request.Rows, cancellationToken);
        if (planned.IsFailure)
            return Result.Failure<InscriptionReport>(planned.Error);

        var plan = planned.Value;
        var report = plan.Report;

        if (!report.CanApply)
            return Result.Failure<InscriptionReport>(InscriptionErrors.Rejected(report.ErrorCount));

        if (report.WillCreateStudents > 0
            && request.ConfirmedStudentCount != report.WillCreateStudents)
            return Result.Failure<InscriptionReport>(InscriptionErrors.CreationsNotConfirmed(
                report.WillCreateStudents, request.ConfirmedStudentCount));

        var written = await applier.ApplyAsync(plan, cancellationToken);

        return written.IsFailure
            ? Result.Failure<InscriptionReport>(written.Error)
            : report;
    }
}
