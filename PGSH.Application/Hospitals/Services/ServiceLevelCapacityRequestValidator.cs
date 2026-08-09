using FluentValidation;

namespace PGSH.Application.Hospitals.Services;

public sealed class ServiceLevelCapacityRequestValidator : AbstractValidator<ServiceLevelCapacityRequest>
{
    public ServiceLevelCapacityRequestValidator()
    {
        RuleFor(x => x.LevelId).GreaterThan(0);

        // Zero would mean "admitted, but there is no room for you" — a contradiction. A service that
        // takes none of a promotion says so by having no row for it.
        RuleFor(x => x.Capacity)
            .InclusiveBetween(1, 200)
            .WithMessage("Un quota doit être d'au moins une place. Pour fermer le service à une promotion, retirez son quota.");
    }
}
