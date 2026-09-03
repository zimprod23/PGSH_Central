using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authentication;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Backups;
using PGSH.SharedKernel;

namespace PGSH.Application.Backups;

/// <summary>
/// Takes a safe point now: a <c>pg_dump -Fc</c> plus the manifest that says what code it was taken
/// under and what the base held at the time.
/// </summary>
/// <remarks>
/// ⚠ <b>This is the command the confirmation dialogs call, and that is the whole feature.</b> A dump
/// somebody has to remember to take at a terminal is a procedure, and procedures are skipped on the
/// day they are needed — which is exactly the day a promotion is being written. Reachable from inside
/// the act, it becomes a side effect of the act.
///
/// <para><c>Kind</c> is <see cref="BackupKind.PreAct"/> when a confirmation dialog took it and
/// <see cref="BackupKind.Named"/> when a human did, and neither is prunable: retention removes
/// scheduled points only.</para>
///
/// <para>Open to <c>Roles.Administrative</c> rather than to a narrower administrator, deliberately —
/// scolarité is the role that <em>applies</em> the déliberation and the réinscription roll, so a gate
/// it could not pass would put the button out of reach of the only person who needs it.</para>
/// </remarks>
public sealed record CreateBackupPointCommand(
    string Label,
    string? Note = null,
    BackupKind Kind = BackupKind.Named)
    : ICommand<BackupPointResponse>, IAuditableCommand
{
    public string AuditAction => "BACKUP_POINT_CREATED";
    public string AuditEntityType => "BackupPoint";
    public string? AuditEntityId => null;
    public string? AuditMetadata => $$"""{"label":"{{Label}}","kind":"{{Kind}}"}""";
}

internal sealed class CreateBackupPointCommandValidator : AbstractValidator<CreateBackupPointCommand>
{
    public CreateBackupPointCommandValidator()
    {
        RuleFor(c => c.Label)
            .NotEmpty().WithMessage("Un point de sauvegarde doit porter un libellé.")
            .MaximumLength(80);

        RuleFor(c => c.Note).MaximumLength(500);
        RuleFor(c => c.Kind).IsInEnum();
    }
}

internal sealed class CreateBackupPointCommandHandler(
    SafePointTaker taker,
    ISchemaFingerprintProvider fingerprints,
    IApplicationDbContext dbContext,
    IUserContext userContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<CreateBackupPointCommand, BackupPointResponse>
{
    public async Task<Result<BackupPointResponse>> Handle(
        CreateBackupPointCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(BackupErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<BackupPointResponse>(access.Error);

        var created = await taker.TakeAsync(
            request.Label.Trim(),
            request.Kind,
            string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            await DescribeCallerAsync(cancellationToken),
            cancellationToken);

        if (created.IsFailure)
            return Result.Failure<BackupPointResponse>(created.Error);

        // The audit entry the pipeline behaviour queued is written here: taking a dump touches no
        // aggregate, so nothing else in this handler would ever call SaveChanges — and the act most
        // worth being able to reconstruct afterwards would leave no trace at all.
        await dbContext.SaveChangesAsync(cancellationToken);

        return created.Value.ToResponse(await fingerprints.GetAsync(cancellationToken));
    }

    private async Task<string?> DescribeCallerAsync(CancellationToken cancellationToken)
    {
        string identity = userContext.UserId.ToString();

        return await dbContext.Users
            .AsNoTracking()
            .Where(u => u.IdentityProviderId == identity)
            .Select(u => u.FirstName + " " + u.LastName)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

/// <summary>
/// Reads a dump's table of contents back. ⚠ <b>A backup nobody has read back is a hypothesis</b>, and
/// this is the cheap half of disproving it — it catches a truncated or corrupt archive, which is the
/// failure a piped <c>pg_dump</c> produced here once. It does not prove the rows are right; only a
/// restore into a scratch base does that, and that is an operator act.
/// </summary>
public sealed record VerifyBackupPointCommand(string Id) : ICommand<BackupPointResponse>, IAuditableCommand
{
    public string AuditAction => "BACKUP_POINT_VERIFIED";
    public string AuditEntityType => "BackupPoint";
    public string? AuditEntityId => Id;
    public string? AuditMetadata => null;
}

internal sealed class VerifyBackupPointCommandHandler(
    IBackupArchive archive,
    ISchemaFingerprintProvider fingerprints,
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<VerifyBackupPointCommand, BackupPointResponse>
{
    public async Task<Result<BackupPointResponse>> Handle(
        VerifyBackupPointCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(BackupErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<BackupPointResponse>(access.Error);

        var verified = await archive.VerifyAsync(request.Id, cancellationToken);
        if (verified.IsFailure)
            return Result.Failure<BackupPointResponse>(verified.Error);

        await dbContext.SaveChangesAsync(cancellationToken);

        var running = await fingerprints.GetAsync(cancellationToken);
        return verified.Value.ToResponse(running);
    }
}

/// <summary>
/// Removes a point and its manifest.
/// </summary>
/// <remarks>
/// ⚠ Restricted to <c>Roles.SuperUser</c>, and it is the only act here that is — creating a point is
/// harmless and scolarité must be able to, while deleting one removes an undo somebody may be
/// counting on.
///
/// <para>⚠ The newest point is refused outright. It is the one every confirmation dialog reads, so
/// removing it moves every bulk act onto an older undo — or onto none — without anything on any
/// screen changing to say so.</para>
/// </remarks>
public sealed record DeleteBackupPointCommand(string Id) : ICommand, IAuditableCommand
{
    public string AuditAction => "BACKUP_POINT_DELETED";
    public string AuditEntityType => "BackupPoint";
    public string? AuditEntityId => Id;
    public string? AuditMetadata => null;
}

internal sealed class DeleteBackupPointCommandHandler(
    IBackupArchive archive,
    IApplicationDbContext dbContext,
    IUserContext userContext)
    : ICommandHandler<DeleteBackupPointCommand>
{
    public async Task<Result> Handle(DeleteBackupPointCommand request, CancellationToken cancellationToken)
    {
        if (!userContext.IsInRole(Roles.SuperUser))
            return Result.Failure(BackupErrors.NotAllowed);

        var points = await archive.ListAsync(cancellationToken);

        var target = points.FirstOrDefault(p => p.Id == request.Id);
        if (target is null)
            return Result.Failure(BackupErrors.NotFound(request.Id));

        if (points[0].Id == target.Id)
            return Result.Failure(BackupErrors.CannotDeleteLatest(target.Id));

        var deleted = await archive.DeleteAsync(request.Id, cancellationToken);
        if (deleted.IsFailure)
            return deleted;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
