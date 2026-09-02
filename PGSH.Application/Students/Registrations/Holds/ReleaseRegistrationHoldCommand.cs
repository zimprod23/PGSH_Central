using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.Students.Registrations.Holds;

/// <summary>
/// Lifts one hold, so the registration takes part in planning again — provided nothing else holds it.
/// </summary>
/// <remarks>
/// <para><b>One student at a time, deliberately.</b> The roll raises holds by the thousand; they come
/// off one by one, because each is a different question — has this évaluation been keyed in, did this
/// student really defend, is this one coming back late. A « tout lever » button would undo in one
/// click the only thing that made a 1 267-row inference safe to record.</para>
///
/// <para>⚠ <b>The registration may still be held after this.</b> Two reasons can stand at once —
/// a student absent from the roll who is then registered by hand into a final year he owes stages in
/// — so the response says what remains rather than claiming the student is free.
/// <see cref="ReleaseHoldReport.StillHeld"/> is the difference between « c'est réglé » and « il en
/// reste un », which the caller cannot work out from a 204.</para>
///
/// <para>⚠ <b>The note is required</b> (<c>RegistrationErrors.HoldReleaseNoteRequired</c>). The hold
/// row survives its own release precisely so the file can say who cleared the student and on what;
/// an empty note throws that half away and leaves a flag that appeared and vanished.</para>
/// </remarks>
public sealed record ReleaseRegistrationHoldCommand(
    Guid HoldId,
    string ReleaseNote) : ICommand<ReleaseHoldReport>, IAuditableCommand
{
    public string AuditAction => "REGISTRATION_HOLD_RELEASED";
    public string AuditEntityType => "RegistrationHold";
    public string? AuditEntityId => HoldId.ToString();

    public string? AuditMetadata => JsonSerializer.Serialize(new { releaseNote = ReleaseNote });
}

/// <param name="StillHeld">
/// Unreleased flags still standing on the same registration, blocking or advisory.
/// </param>
/// <param name="StillBlocked">
/// Whether any of those actually withdraws the registration from planning. ⚠ Distinct from
/// <paramref name="StillHeld"/>: a student left carrying only « dossier à compléter » is on the
/// worklist and <b>is planned</b>, and telling the operator he is still frozen would be false.
/// </param>
public sealed record ReleaseHoldReport(
    Guid RegistrationId,
    RegistrationHoldReason Released,
    int StillHeld,
    bool StillBlocked);

internal sealed class ReleaseRegistrationHoldCommandValidator
    : AbstractValidator<ReleaseRegistrationHoldCommand>
{
    public ReleaseRegistrationHoldCommandValidator()
    {
        RuleFor(x => x.HoldId).NotEmpty();
        RuleFor(x => x.ReleaseNote).NotEmpty().MaximumLength(1000);
    }
}

internal sealed class ReleaseRegistrationHoldCommandHandler(
    IApplicationDbContext dbContext,
    IUserContext userContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ReleaseRegistrationHoldCommand, ReleaseHoldReport>
{
    public async Task<Result<ReleaseHoldReport>> Handle(
        ReleaseRegistrationHoldCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(RegistrationErrors.HoldNotAllowed);
        if (access.IsFailure)
            return Result.Failure<ReleaseHoldReport>(access.Error);

        // The whole hold set is loaded, not just the one named: the release goes through the
        // aggregate, and StillHeld is read from the same graph rather than from a second count that
        // could disagree with it.
        var registration = await dbContext.Registrations
            .Include(r => r.Holds)
            .FirstOrDefaultAsync(r => r.Holds.Any(h => h.Id == request.HoldId), cancellationToken);

        if (registration is null)
            return Result.Failure<ReleaseHoldReport>(RegistrationErrors.HoldNotFound(request.HoldId));

        var reason = registration.Holds.First(h => h.Id == request.HoldId).Reason;

        var released = registration.ReleaseHold(
            request.HoldId, request.ReleaseNote, DateTime.UtcNow, userContext.UserId);

        if (released.IsFailure)
            return Result.Failure<ReleaseHoldReport>(released.Error);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReleaseHoldReport(
            registration.Id,
            reason,
            registration.Holds.Count(h => h.ReleasedOn is null),
            registration.IsOnHold);
    }
}
