using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;
using System.Text.Json;

namespace PGSH.Application.AcademicYears.Manage;

/// <summary>
/// Designates « l'année en cours » — the year every handler that omits one resolves to.
/// </summary>
/// <remarks>
/// <para>It is a distinct act from creating a year, and it has to be: a year is normally created
/// months before it becomes current (the axis is authored, the groups are cut, the déliberation of
/// the year below has not happened yet), and until now the only way to move the flag was to create
/// another year with <c>IsCurrent: true</c>.</para>
///
/// <para>The ordering the database forces — demote, save, then promote — lives in
/// <see cref="CurrentYearDesignation"/>, shared with the create path so it is stated once.</para>
/// </remarks>
public sealed record SetCurrentAcademicYearCommand(int AcademicYearId)
    : ICommand<CurrentAcademicYearReport>, IAuditableCommand
{
    public string AuditAction => "ACADEMIC_YEAR_SET_CURRENT";
    public string AuditEntityType => "AcademicYear";
    public string? AuditEntityId => AcademicYearId.ToString();
    public string? AuditMetadata => JsonSerializer.Serialize(new { academicYearId = AcademicYearId });
}

/// <param name="PreviousLabel">
/// The year that stood down, so the confirmation can say what changed rather than only what it changed
/// to. Null when no year was current — the ordinary state of a fresh base, and not an error.
/// </param>
public sealed record CurrentAcademicYearReport(
    int AcademicYearId,
    string Label,
    string? PreviousLabel);

internal sealed class SetCurrentAcademicYearCommandValidator
    : AbstractValidator<SetCurrentAcademicYearCommand>
{
    public SetCurrentAcademicYearCommandValidator() =>
        RuleFor(x => x.AcademicYearId).GreaterThan(0);
}

internal sealed class SetCurrentAcademicYearCommandHandler(
    IApplicationDbContext dbContext,
    CurrentYearDesignation designation,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<SetCurrentAcademicYearCommand, CurrentAcademicYearReport>
{
    public async Task<Result<CurrentAcademicYearReport>> Handle(
        SetCurrentAcademicYearCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(AcademicYearErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<CurrentAcademicYearReport>(access.Error);

        var target = await dbContext.AcademicYears
            .FirstOrDefaultAsync(y => y.Id == request.AcademicYearId, cancellationToken);

        if (target is null)
            return Result.Failure<CurrentAcademicYearReport>(
                AcademicYearErrors.NotFound(request.AcademicYearId));

        if (target.IsCurrent)
            return Result.Failure<CurrentAcademicYearReport>(
                AcademicYearErrors.AlreadyCurrent(target.Label));

        var change = await designation.PromoteAsync(target, cancellationToken);
        if (change.IsFailure)
            return Result.Failure<CurrentAcademicYearReport>(change.Error);

        return new CurrentAcademicYearReport(target.Id, target.Label, change.Value.PreviousLabel);
    }
}
