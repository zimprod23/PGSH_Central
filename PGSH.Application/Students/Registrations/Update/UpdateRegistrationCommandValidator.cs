using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PGSH.Application.Students.Registrations.Update;

public sealed class UpdateRegistrationCommandValidator : AbstractValidator<UpdateRegistrationCommand>
{
    public UpdateRegistrationCommandValidator()
    {
        RuleFor(x => x.Status).NotEmpty().MaximumLength(50);
        RuleFor(x => x.LevelId).GreaterThan(0);
        RuleFor(x => x.FailureDescription).MaximumLength(500);
    }
}