using FluentValidation;

namespace PGSH.Application.Students.Update;

/// <summary>
/// What saving an <em>existing</em> student must satisfy.
/// </summary>
/// <remarks>
/// ⚠ <b>Every rule here is applied to rows that are already in the base</b>, so a rule the imported
/// data does not meet does not describe a good record — it makes those students <b>read-only</b>, and
/// the refusal names a field nobody was editing. Three rules did exactly that until 14/09/2026, and
/// each one is relaxed below against the write path that actually produces the value. This is the
/// same defect as the old CNE regex (5 646 unsaveable students) and <c>Objectives.NotEmpty()</c> (the
/// whole stage catalogue) — the third, fourth and fifth instances of one class.
///
/// <para>⚠ <b>The create side is deliberately stricter, and the asymmetry is the point.</b> A human
/// filling a form can be asked for a gender and a date of birth; an imported row cannot be asked
/// anything. See <c>CreateStudentCommandValidator</c>.</para>
/// </remarks>
public sealed class UpdateStudentCommandValidator : AbstractValidator<UpdateStudentCommand>
{
    public UpdateStudentCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .UniversityEmail();

        RuleFor(x => x.CNE).ValidCne();

        // ⚠ Not `int.TryParse`. A validator describes what a *save* must satisfy, so a rule the
        // stored data does not meet makes those rows read-only — and the refusal names a field
        // nobody was editing. `InscriptionPlanner` derives « SANS-APOGEE-<cne> » for a student whose
        // numéro Apogée the faculty has not allocated yet, which is not a number and never will be.
        // Same mistake as the old CNE regex (5 646 unsaveable students) and `Objectives.NotEmpty()`
        // (the whole stage catalogue). Length and presence are what the column actually requires.
        RuleFor(x => x.Appogee)
            .NotEmpty()
            .MaximumLength(StudentIdentifierRules.MaxAppogeeLength);

        // ⚠ `Gender.None` is **accepted**, and it is not a loophole — it is the value the base holds.
        // `LegacyIdentityMapper.MapGender` writes it for the 1 050 rows the Access base leaves blank
        // and the 3 that hold "C", its own comment reading « None is the honest answer; it is not a
        // guess », and `InscriptionPlanner` writes it for every canvas row with an empty Sexe column —
        // so the base keeps acquiring them. Refusing it here made those 1 053 students unsaveable and
        // demanded the operator invent a sex in order to fix a misspelt name.
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.AcademicProgram).IsInEnum();
        RuleFor(x => x.BacSeries).IsInEnum();
        RuleFor(x => x.CivilStatus).IsInEnum();
        RuleFor(x => x.NationalityStatus).IsInEnum();

        // ⚠ 100, not 50 — the column's width, which the import already truncates to. And neither name
        // is required on its own: `SplitName` gives a single-token name an empty *first* name by
        // design. See StudentIdentifierRules.MaxNameLength / HasAName.
        RuleFor(x => x.FirstName).MaximumLength(StudentIdentifierRules.MaxNameLength);
        RuleFor(x => x.LastName).MaximumLength(StudentIdentifierRules.MaxNameLength);

        RuleFor(x => x)
            .Must(x => StudentIdentifierRules.HasAName(x.FirstName, x.LastName))
            .WithName(nameof(UpdateStudentCommand.LastName))
            .WithMessage(StudentIdentifierRules.NameMissingMessage);

        // ⚠ Optional, because `User.DateOfBirth` is `DateOnly?` and both write paths store null when
        // the source carries no date — `LegacyImportPlanner` for every Access row without one, and
        // `InscriptionPlanner` for every canvas row with an empty cell. `NotEmpty()` made each of them
        // read-only. The age rule stands, but only over a date that is actually there: asked of null it
        // refuses too, which would have re-created the same refusal one line lower.
        RuleFor(x => x.DateOfBirth)
            .Must(date => date < DateOnly.FromDateTime(DateTime.Now.AddYears(-15)))
            .WithMessage("Student must be at least 15 years old.")
            .When(x => x.DateOfBirth is not null);
    }
}
