using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.GroupChange;

/// <summary>
/// « Changement de groupe » — the student is in the target roster, and the record now says he always
/// was.
/// </summary>
/// <remarks>
/// <para><b>It is the third act on a roster, and the three are not interchangeable.</b>
/// <list type="bullet">
///   <item><c>AssignStudentToGroupCommand</c> — he is in <i>no</i> roster and joins one.</item>
///   <item><c>TransferStudentCommand</c> — he moves, and the move is a fact: the running rotation is
///   cut, the future one rehomed, and the dossier says when and why.</item>
///   <item>this — he was recorded in the wrong roster, and nothing that happened contradicts putting
///   it right.</item>
/// </list></para>
///
/// <para>⚠ <b>No <c>Reason</c>, deliberately.</b> Every other act on a roster takes one because it is
/// written down and read back later; here there is nowhere for it to go — the whole act is the absence
/// of a trace on the student's file. What the operator did is in the register, which is a different
/// document with a different reader.</para>
///
/// <para>⚠ <b>Auditable, and that is not a contradiction of « sans historique ».</b> The two words name
/// different things: the <i>dossier</i> is the student's record and must show nothing, the
/// <i>registre</i> is the log of administrative acts and must show everything. This act cannot be
/// undone — the roster it came from is written down nowhere afterwards — so the entry carries it, and
/// that is the one place it survives. A register a destructive act can slip past is not a register.</para>
/// </remarks>
public sealed record ChangeStudentGroupCommand(Guid RegistrationId, int TargetGroupId)
    : ICommand<GroupChangeReport>, IAuditableCommand
{
    public string AuditAction => "STUDENT_GROUP_CHANGED";
    public string AuditEntityType => "Registration";
    public string? AuditEntityId => RegistrationId.ToString();

    public string? AuditMetadata => AuditMetadataJson.Of(("targetGroupId", TargetGroupId));
}

internal sealed class ChangeStudentGroupCommandValidator : AbstractValidator<ChangeStudentGroupCommand>
{
    public ChangeStudentGroupCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.TargetGroupId).GreaterThan(0);
    }
}

internal sealed class ChangeStudentGroupCommandHandler(
    IApplicationDbContext dbContext,
    StudentGroupRelocator relocator,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<ChangeStudentGroupCommand, GroupChangeReport>
{
    public async Task<Result<GroupChangeReport>> Handle(
        ChangeStudentGroupCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(RegistrationErrors.GroupingNotAllowed);
        if (access.IsFailure)
            return Result.Failure<GroupChangeReport>(access.Error);

        var report = await relocator.RelocateAsync(
            request.RegistrationId, request.TargetGroupId, cancellationToken);

        if (report.IsFailure)
            return report;

        // One save, so the roster pointer, the affectations, the rewritten memberships and the
        // rebuilt périodes land together. No ExecuteAtomicallyAsync is needed for that — a single
        // SaveChanges is already one transaction — and it keeps the act on the plain path where
        // AuditLogPipelineBehavior's row, staged before the handler ran, commits with it.
        await dbContext.SaveChangesAsync(cancellationToken);

        return report;
    }
}
