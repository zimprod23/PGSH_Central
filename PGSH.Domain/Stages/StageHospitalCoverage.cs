namespace PGSH.Domain.Stages;

/// <summary>
/// Whether one hospital can host one stage at all — read off <c>Stage.AllowedServices</c>, which is
/// the list a cell is checked against before it may be written.
///
/// <para>This is the check that has to happen <b>before</b> a placement is promised. Measured on the
/// live catalogue 2026-09-03: the Hôpital Militaire Mohammed V carries a service for every one of the
/// six 6ᵉ année stages, so « tout au militaire » is expressible there — and for 5ᵉ année it covers six
/// stages of seven, because <b>Santé Publique lists exactly one allowed service and it is not at that
/// hospital</b>. Without this read the promise is made in September and the contradiction is found at
/// the sixth cell.</para>
///
/// <para>⚠ <b><see cref="NoServicesAuthored"/> is not a weaker <see cref="NotAtThisHospital"/>.</b>
/// A stage whose allowed-services list is empty is open to every service — that is what an unset
/// whitelist means to <c>SetCohortSlotAssignmentCommandHandler</c>, which only enforces the list
/// « when configured » — so it is not an obstacle, it is a list nobody has authored. The two call for
/// opposite acts: one says « choisissez un autre hôpital », the other says « saisissez la liste des
/// services de ce stage ». Collapsed into a single « non couvert » the second reads as a refusal that
/// no data supports. It is not hypothetical: three stages of the catalogue (the two stages
/// d'immersion and le stage hospitalier d'initiation) carry no allowed service at all.</para>
/// </summary>
public enum StageHospitalCoverage
{
    /// <summary>
    /// The stage authorises no service at all. Not a statement about this hospital: an empty
    /// whitelist is not enforced, so every service is allowed and nobody has said which are meant.
    /// </summary>
    NoServicesAuthored = 0,

    /// <summary>The stage authorises services, and none of them is at this hospital.</summary>
    NotAtThisHospital,

    /// <summary>At least one service the stage authorises is at this hospital.</summary>
    Covered,
}

/// <summary>
/// The one place <see cref="StageHospitalCoverage"/> is decided. Pure, for the same reason
/// <see cref="RosterHospitalPlacementTest"/> is.
/// </summary>
public static class StageHospitalCoverageTest
{
    /// <param name="allowedServices">How many services the stage authorises, all hospitals together.</param>
    /// <param name="servicesAtHospital">
    /// How many of those are at the hospital being asked about — a subset count by construction.
    /// </param>
    public static StageHospitalCoverage Of(int allowedServices, int servicesAtHospital) =>
        allowedServices <= 0    ? StageHospitalCoverage.NoServicesAuthored
        : servicesAtHospital > 0 ? StageHospitalCoverage.Covered
                                 : StageHospitalCoverage.NotAtThisHospital;
}
