using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;
using System.Text.Json;

namespace PGSH.Application.Students.Registrations.Outcome;

/// <summary>
/// Records one student's year verdict, without a file.
/// </summary>
/// <remarks>
/// <para>The déliberation canvas closes a promotion; this closes a registration. Both exist because a
/// jury is not the only way a verdict arrives: a student deliberated late, a PV corrected for one
/// name, an abandon notified in November. Uploading a one-line workbook to record that would be
/// absurd — and under an exceptions file it is worse than absurd, since re-uploading the promotion's
/// file is precisely what must not be needed to fix one row.</para>
///
/// <para>It writes through <see cref="Registration.RecordYearOutcome"/> like the import, so the
/// verdict is <see cref="RegistrationOutcomeSource.Declared"/>, the timeline entry is the same one,
/// and the réinscription reads it the same way.</para>
/// </remarks>
public sealed record RecordRegistrationOutcomeCommand(
    Guid RegistrationId,
    RegistrationStatus Outcome,
    string? Motif = null) : ICommand, IAuditableCommand
{
    public string AuditAction => "YEAR_OUTCOME_RECORDED";
    public string AuditEntityType => "Registration";
    public string? AuditEntityId => RegistrationId.ToString();

    public string? AuditMetadata =>
        JsonSerializer.Serialize(new { outcome = Outcome.ToString(), motif = Motif });
}

internal sealed class RecordRegistrationOutcomeCommandValidator
    : AbstractValidator<RecordRegistrationOutcomeCommand>
{
    public RecordRegistrationOutcomeCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.Outcome).IsInEnum();
        RuleFor(x => x.Motif).MaximumLength(500);
    }
}

internal sealed class RecordRegistrationOutcomeCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<RecordRegistrationOutcomeCommand>
{
    public async Task<Result> Handle(
        RecordRegistrationOutcomeCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(RegistrationErrors.OutcomeNotAllowed);
        if (access.IsFailure)
            return access;

        var registration = await dbContext.Registrations
            .Include(r => r.Level)
            .FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);

        if (registration is null)
            return Result.Failure(RegistrationErrors.NotFound(request.RegistrationId));

        // The same rule the canvas applies, and it stands aside the same way where the student carries
        // no CNPN stamp: one student at a time must not be stricter than a whole promotion at once, or
        // the single-row path becomes the one that cannot record what the import just did.
        if (request.Outcome == RegistrationStatus.Graduated)
        {
            // The text that governed *this* year, falling back to the student's own stamp — the same
            // order the canvas uses, for the same reason: an effectivity rule can put a student on a
            // six-year text after he sat a year under a seven-year one, and "was that his last year?"
            // is a question about the year, not about where he stands today.
            int? totalYears = registration.CnpnVersionId is not null
                ? await dbContext.CnpnVersions
                    .AsNoTracking()
                    .Where(v => v.Id == registration.CnpnVersionId)
                    .Select(v => (int?)v.TotalYears)
                    .FirstOrDefaultAsync(cancellationToken)
                : await dbContext.Students
                    .AsNoTracking()
                    .Where(s => s.Id == registration.StudentId && s.CnpnVersionId != null)
                    .Select(s => (int?)s.CnpnVersion!.TotalYears)
                    .FirstOrDefaultAsync(cancellationToken);

            int levelYear = registration.Level?.Year ?? 0;

            if (totalYears is { } total && levelYear != total)
                return Result.Failure(RegistrationErrors.NotAFinalYear(levelYear, total));
        }

        var motif = string.IsNullOrWhiteSpace(request.Motif)
            ? null
            : new FailureReasons(request.Motif.Trim(), []);

        // A motif only qualifies a decision that goes against the student; on a favourable one it has
        // nothing to qualify, exactly as the canvas drops it.
        bool adverse = request.Outcome is RegistrationStatus.Failed
            or RegistrationStatus.Excluded or RegistrationStatus.Withdrawn;

        var result = registration.RecordYearOutcome(
            request.Outcome, RegistrationOutcomeSource.Declared,
            adverse ? motif : null, DateTime.UtcNow);

        if (result.IsFailure)
            return result;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
