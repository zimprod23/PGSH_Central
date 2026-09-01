using FluentValidation;
using PGSH.Application.Abstractions.Authorization;
using PGSH.Application.Abstractions.Messaging;
using PGSH.SharedKernel;
using System.Text.Json;

namespace PGSH.Application.Students.Registrations.Inscription;

/// <summary>
/// Inscribes <b>one</b> student into one promotion — the transfer notified in November, the returner
/// who turns up in week three, the réorientation settled after the intake file was sent.
/// </summary>
/// <remarks>
/// <para><b>Every bulk import needs a single-row way in, or the only way to fix one person is to
/// re-send the whole file.</b> That is the rule the déliberation follows with
/// <c>RecordRegistrationOutcomeCommand</c>, and it matters more here: an inscription file names
/// people who do not exist yet, so re-sending it to add one late arrival means re-stating a whole
/// promotion to say one thing.</para>
///
/// <para><b>Every value arrives as text, exactly as a sheet cell would.</b> Dates, the série de bac,
/// the sexe and the convention are parsed by the same code, so the form and the file cannot disagree
/// about what « 03/09/2006 » or « SM A » means, and a refusal reads identically on both. The
/// alternative — typed fields here, strings there — is two grammars for one column.</para>
///
/// <para><b>No preview and no confirmation, deliberately.</b> The file path asks for
/// <c>ConfirmedStudentCount</c> because its danger is a row nobody read and a file edited between the
/// simulation and the apply. Here the request <em>is</em> the row: one named person, typed once, with
/// nothing in between. What comes back is that person's own
/// <see cref="InscriptionRowReport"/> — including any identifier PGSH had to manufacture for him.</para>
/// </remarks>
public sealed record InscribeStudentCommand(
    int LevelId,
    string? Cne,
    string? Appogee,
    string? LastName,
    string? FirstName,
    string? Cin = null,
    string? Email = null,
    string? Gender = null,
    string? DateOfBirth = null,
    string? PlaceOfBirth = null,
    string? BacYear = null,
    string? BacSeries = null,
    string? AccessGrade = null,
    string? Agreement = null,
    string? OriginInstitution = null,
    string? OriginCountry = null,
    string? OriginLastYearCompleted = null,
    string? EquivalenceReference = null,
    string? EquivalenceDate = null,
    int? AcademicYearId = null) : ICommand<InscriptionRowReport>, IAuditableCommand
{
    /// <summary>The one row, in the shape the planner and the sheet parser both speak.</summary>
    public InscriptionRow Row => new(
        SheetRow: 1, Cne, Appogee, LastName, FirstName, Cin, Email, Gender, DateOfBirth,
        PlaceOfBirth, BacYear, BacSeries, AccessGrade, Agreement, OriginInstitution, OriginCountry,
        OriginLastYearCompleted, EquivalenceReference, EquivalenceDate);

    public InscriptionScope Scope => new(LevelId, AcademicYearId);

    public string AuditAction => "STUDENT_INSCRIBED";
    public string AuditEntityType => "Level";
    public string? AuditEntityId => LevelId.ToString();

    public string? AuditMetadata =>
        JsonSerializer.Serialize(new
        {
            levelId = LevelId,
            academicYearId = AcademicYearId,
            cne = Cne,
            appogee = Appogee,
        });
}

internal sealed class InscribeStudentCommandValidator : AbstractValidator<InscribeStudentCommand>
{
    public InscribeStudentCommandValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);

        // ⚠ Nothing further is asserted here, and that is the point. Whether the row names anybody,
        // whether a transfer carries its équivalence, whether a date is readable — all of it is the
        // planner's, so the form is refused for exactly the reasons a sheet row is, in the same words.
        // A rule stated twice is a rule that can disagree with itself.
    }
}

/// <summary>
/// The same planner and the same writer the file path uses, on a list of one.
/// </summary>
internal sealed class InscribeStudentCommandHandler(
    InscriptionPlanner planner,
    InscriptionApplier applier,
    ExecutionAuthorizer authorizer)
    : ICommandHandler<InscribeStudentCommand, InscriptionRowReport>
{
    public async Task<Result<InscriptionRowReport>> Handle(
        InscribeStudentCommand request, CancellationToken cancellationToken)
    {
        var access = authorizer.EnsureIsAdministrative(InscriptionErrors.NotAllowed);
        if (access.IsFailure)
            return Result.Failure<InscriptionRowReport>(access.Error);

        var planned = await planner.PlanAsync(request.Scope, [request.Row], cancellationToken);
        if (planned.IsFailure)
            return Result.Failure<InscriptionRowReport>(planned.Error);

        var plan = planned.Value;
        var row = plan.Report.Rows[0];

        // ⚠ The row's own sentence, not « 1 ligne en erreur ». On a form the refusal has to name the
        // field: the count is what a file needs, and it explains nothing to somebody who typed one
        // person in.
        if (!plan.Report.CanApply)
            return Result.Failure<InscriptionRowReport>(
                InscriptionErrors.RowRefused(row.Action, row.Message));

        var written = await applier.ApplyAsync(plan, cancellationToken);

        return written.IsFailure
            ? Result.Failure<InscriptionRowReport>(written.Error)
            : row;
    }
}
