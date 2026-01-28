using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Application.Students.Registrations.Create;

public sealed class CreateRegistrationCommandValidator : AbstractValidator<CreateRegistrationCommand>
{
    public CreateRegistrationCommandValidator()
    {
        RuleFor(x => x.StudentId).NotEmpty();
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.AcademicYear).NotEmpty();
        RuleFor(x => x.Status).NotEmpty().MaximumLength(50);
    }
}
