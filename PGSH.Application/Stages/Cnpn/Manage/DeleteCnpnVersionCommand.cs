using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Application.Employees.MyServices;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn.Manage;

/// <summary>
/// Removes a recorded text. Meant for the mistyped row, not for retiring a text that governed
/// anybody — a superseded arrêté stays, because the students who followed it stay too.
///
/// <para><b>Students are a hard gate.</b> Deleting a text they are stamped with would leave them
/// following no CNPN at all, and nothing downstream can answer "what does this student owe" from
/// that state. It is also what keeps a raw foreign-key violation (Users → CnpnVersions is NO ACTION)
/// from surfacing as a 500 instead of a sentence someone can act on.</para>
///
/// <para><b>Requirement sets go with it, and the count is returned.</b> The database cascades
/// <c>Curriculums</c> and their stage entries. That is destructive, and deliberately allowed only
/// because of the gate above: a text nobody follows has nobody who could owe anything, so removing
/// its requirements strands no obligation. The caller is told how much went so the confirmation can
/// say it out loud.</para>
///
/// <para>⚠ One consequence worth surfacing in the UI rather than here: if the deleted text was the
/// only one governing entrants from its year, new registrations silently fall back to the previous
/// text — which for Médecine means a seven-year degree instead of six.</para>
/// </summary>
public sealed record DeleteCnpnVersionCommand(int Id) : ICommand<int>, IAuditableCommand
{
    public string  AuditAction     => "CNPN_VERSION_DELETED";
    public string  AuditEntityType => "CnpnVersion";
    public string? AuditEntityId   => Id.ToString();
    public string? AuditMetadata   => null;
}

internal sealed class DeleteCnpnVersionCommandValidator : AbstractValidator<DeleteCnpnVersionCommand>
{
    public DeleteCnpnVersionCommandValidator() => RuleFor(x => x.Id).GreaterThan(0);
}

internal sealed class DeleteCnpnVersionCommandHandler(
    IApplicationDbContext dbContext,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<DeleteCnpnVersionCommand, int>
{
    public async Task<Result<int>> Handle(DeleteCnpnVersionCommand request, CancellationToken ct)
    {
        var access = authorizer.EnsureIsAdministrative(StageErrors.AdministrativeOnly);
        if (access.IsFailure) return Result.Failure<int>(access.Error);

        var version = await dbContext.CnpnVersions
            .FirstOrDefaultAsync(v => v.Id == request.Id, ct);

        if (version is null)
            return Result.Failure<int>(CnpnErrors.VersionNotFound(request.Id));

        int students = await dbContext.Students.CountAsync(s => s.CnpnVersionId == request.Id, ct);
        if (students > 0)
            return Result.Failure<int>(CnpnErrors.CannotDeleteWithStudents(version.Code, students));

        // The same gate from the other side, and it is not redundant: a text can govern a *closed*
        // year of a student who has since moved to another one, so the student count reaches zero
        // while registrations still name it. Those rows are the record of what those years required —
        // Registrations → CnpnVersions is Restrict, so without this the cascade is a 500.
        int registrations = await dbContext.Registrations.CountAsync(
            r => r.CnpnVersionId == request.Id, ct);

        if (registrations > 0)
            return Result.Failure<int>(
                CnpnErrors.CannotDeleteWithRegistrations(version.Code, registrations));

        // Counted before the cascade takes them, so the caller can report what was actually removed.
        int curricula = await dbContext.Curriculums.CountAsync(c => c.CnpnVersionId == request.Id, ct);

        dbContext.CnpnVersions.Remove(version);
        await dbContext.SaveChangesAsync(ct);

        return curricula;
    }
}
