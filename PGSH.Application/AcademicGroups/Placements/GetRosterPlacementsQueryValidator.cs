using FluentValidation;

namespace PGSH.Application.AcademicGroups.Placements;

/// <summary>
/// ⚠ Both rules refuse a request that would otherwise be answered by <b>quietly ignoring part of what
/// the caller said</b> — the failure mode this codebase treats as worse than a refusal, because the
/// answer looks correct and names nothing that was dropped.
/// </summary>
public sealed class GetRosterPlacementsQueryValidator : AbstractValidator<GetRosterPlacementsQuery>
{
    public GetRosterPlacementsQueryValidator()
    {
        // ⚠ Two rules, two messages. `.WithMessage` attaches to the validator immediately before it,
        // so chaining one message onto NotNull().GreaterThan(0) leaves the null case falling back to
        // FluentValidation's default — « 'Level Id' ne doit pas avoir la valeur null », which names a
        // property and no remedy.
        RuleFor(x => x.LevelId)
            .NotNull()
            .WithMessage("La promotion est obligatoire : un numéro de groupe sans sa promotion "
                       + "n'identifie rien.");

        RuleFor(x => x.LevelId)
            .GreaterThan(0)
            .When(x => x.LevelId is not null)
            .WithMessage("La promotion est obligatoire : un numéro de groupe sans sa promotion "
                       + "n'identifie rien.");

        // A service belongs to exactly one hospital and a hospital to exactly one city, so any pair of
        // these is either redundant or contradictory — and contradictory it returns an empty page that
        // reads as « personne n'y va ».
        RuleFor(x => x)
            .Must(x => new[]
            {
                x.ServiceId is not null,
                x.HospitalId is not null,
                !string.IsNullOrWhiteSpace(x.City),
            }.Count(named => named) <= 1)
            .WithMessage("Indiquez un service, un hôpital ou une ville — un seul des trois : un "
                       + "service appartient déjà à un hôpital, et un hôpital à une ville.");

        // « Exclusivement » has nothing to be exclusive to. Accepted silently it would fall back to
        // listing the promotion, i.e. answer a much weaker question than the one that was asked.
        RuleFor(x => x)
            .Must(x => x.Match == PlacementMatch.Anywhere || x.HasTarget)
            .WithMessage("« Exclusivement » suppose un service ou un hôpital à comparer : "
                       + "précisez-en un, ou cherchez sans critère de lieu.");
    }
}
