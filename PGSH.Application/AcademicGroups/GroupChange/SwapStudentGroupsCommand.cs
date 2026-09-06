using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.GroupChange;

/// <summary>
/// « Échanger deux étudiants » — A takes B's roster and B takes A's, in one act and without a trace on
/// either file.
/// </summary>
/// <remarks>
/// <para>It exists because it is what an admin actually asks for: the two rosters are already the right
/// size, and moving one student without the other leaves one roster short and the other over. Run as
/// two separate changements it is also two chances to be interrupted between them, which is precisely
/// the state — one roster of 8, one of 6 — that the échange is meant to avoid.</para>
///
/// <para><b>It is two changements, and it is implemented as two changements.</b> The rules of « sans
/// trace » live in <see cref="StudentGroupRelocator"/> and are applied twice; nothing about the pair
/// relaxes any of them, so a student whose rotations have begun refuses the échange exactly as he would
/// refuse a single move.</para>
///
/// <para>⚠ <b>Both destinations are read before either student is moved.</b> Taken from the registration
/// as it stands at the moment it is needed, the second half would send B to the roster A has just been
/// moved <i>into</i> — both students in one roster, and the other one empty.</para>
/// </remarks>
public sealed record SwapStudentGroupsCommand(Guid FirstRegistrationId, Guid SecondRegistrationId)
    : ICommand<GroupSwapReport>, IAuditableCommand
{
    public string AuditAction => "STUDENT_GROUPS_SWAPPED";
    public string AuditEntityType => "Registration";
    public string? AuditEntityId => FirstRegistrationId.ToString();

    /// <remarks>
    /// The second registration goes in the metadata rather than in a second entry: one act, one line.
    /// <see cref="AuditEntityId"/> can only name one of them, so the other has to be somewhere the
    /// register can still be searched on.
    /// </remarks>
    public string? AuditMetadata =>
        AuditMetadataJson.Of(("secondRegistrationId", SecondRegistrationId.ToString()));
}

internal sealed class SwapStudentGroupsCommandValidator : AbstractValidator<SwapStudentGroupsCommand>
{
    public SwapStudentGroupsCommandValidator()
    {
        RuleFor(x => x.FirstRegistrationId).NotEmpty();
        RuleFor(x => x.SecondRegistrationId).NotEmpty();
    }
}

internal sealed class SwapStudentGroupsCommandHandler(
    IApplicationDbContext dbContext,
    StudentGroupRelocator relocator,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<SwapStudentGroupsCommand, GroupSwapReport>
{
    public async Task<Result<GroupSwapReport>> Handle(
        SwapStudentGroupsCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(RegistrationErrors.GroupingNotAllowed);
        if (access.IsFailure)
            return Result.Failure<GroupSwapReport>(access.Error);

        if (request.FirstRegistrationId == request.SecondRegistrationId)
            return Result.Failure<GroupSwapReport>(GroupChangeErrors.CannotSwapWithSelf());

        var first = await GroupOfAsync(request.FirstRegistrationId, cancellationToken);
        if (first is null)
            return Result.Failure<GroupSwapReport>(
                RegistrationErrors.NotFound(request.FirstRegistrationId));

        var second = await GroupOfAsync(request.SecondRegistrationId, cancellationToken);
        if (second is null)
            return Result.Failure<GroupSwapReport>(
                RegistrationErrors.NotFound(request.SecondRegistrationId));

        // An échange is defined by the two rosters, so a student in none of them has no place in it —
        // and the relocator's own refusal would name only one of the two, which reads as though the
        // other half had gone through.
        if (first.AcademicGroupId is not { } firstGroupId || second.AcademicGroupId is not { } secondGroupId)
            return Result.Failure<GroupSwapReport>(GroupChangeErrors.NotInAGroup());

        if (firstGroupId == secondGroupId)
            return Result.Failure<GroupSwapReport>(GroupChangeErrors.SwapWithinOneGroup(
                await GroupLabelAsync(firstGroupId, cancellationToken)));

        var movedFirst = await relocator.RelocateAsync(
            request.FirstRegistrationId, secondGroupId, cancellationToken);
        if (movedFirst.IsFailure)
            return Result.Failure<GroupSwapReport>(movedFirst.Error);

        var movedSecond = await relocator.RelocateAsync(
            request.SecondRegistrationId, firstGroupId, cancellationToken);
        if (movedSecond.IsFailure)
            return Result.Failure<GroupSwapReport>(movedSecond.Error);

        // One save for both halves. Nothing was written until here, so the second student's refusal
        // leaves the first exactly where he was — an échange that half happened is two rosters of the
        // wrong size and no record of why.
        await dbContext.SaveChangesAsync(cancellationToken);

        return new GroupSwapReport(movedFirst.Value, movedSecond.Value);
    }

    private Task<RegistrationRef?> GroupOfAsync(Guid registrationId, CancellationToken ct) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.Id == registrationId)
            .Select(r => new RegistrationRef(r.AcademicGroupId))
            .FirstOrDefaultAsync(ct);

    private async Task<string> GroupLabelAsync(int groupId, CancellationToken ct) =>
        await dbContext.AcademicGroups
            .Where(g => g.Id == groupId)
            .Select(g => g.Label)
            .FirstOrDefaultAsync(ct) ?? $"Groupe {groupId}";

    private sealed record RegistrationRef(int? AcademicGroupId);
}
