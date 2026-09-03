namespace PGSH.Application.Hospitals.Chefs;

/// <summary>
/// Which of a service's three chef sources a document is allowed to read.
///
/// <para>The sources themselves never change — <see cref="ServiceChefDirectory"/> still loads the
/// tenure trail, the sitting chef and the legacy note. What this says is which of them a printed
/// page may answer from, and it exists because the answer is currently a fact about the *data*
/// rather than about the document: <c>ServiceChefAssignment</c> holds two rows in the whole base and
/// both were linked to try the mechanism out, so a file resolving them would print a test account's
/// name beside real students.</para>
/// </summary>
public enum ServiceChefSourcePolicy
{
    /// <summary>The full authority order: the tenure open on the date → the sitting chef → the
    /// legacy note. What the faculty gets back once real chefs are linked in Personnel.</summary>
    Authority = 0,

    /// <summary>
    /// The legacy import note alone (<see cref="PGSH.Domain.Hospitals.ServiceChefSourceNote"/>).
    /// A linked chef is ignored, whether it is dated or sitting.
    ///
    /// <para>⚠ Every name a document prints under this policy is therefore
    /// <see cref="ServiceChefAttribution.FromSourceNote"/> — undated, and it must stay flagged as
    /// such. Narrowing the sources is not a licence to stop saying where the name came from.</para>
    /// </summary>
    SourceNoteOnly = 1,
}

/// <summary>
/// Which policy every reader of « qui dirige ce service ? » runs on, in <b>one</b> place.
///
/// <para>⚠ <b>Temporary, and dated 2026-09-03.</b> The two <c>ServiceChefAssignment</c> rows in the
/// base are test links, not the faculty's chefs — so until real ones are recorded in Personnel, a
/// document naming a chef from an affectation names the wrong person, while the legacy note names
/// who the Access base last recorded for 140 of the 148 services. The note is the better answer
/// *today*; it is not the better rule.</para>
///
/// <para>The two documents <b>and the service fiche</b> read this constant rather than choosing for
/// themselves, for the reason the directory was extracted in the first place: two screens of one
/// faculty disagreeing about who leads a service is the drift <c>StageScoring</c> and
/// <c>ServicePeriodLifecycle</c> exist to prevent — and it had already happened, the page ranking the
/// three sources one way while the export ranked them another. Restoring the dated record is one line
/// here, and <c>ServiceChefDirectoryTests</c> keeps the authority order covered in the meantime so
/// the line is safe to flip.</para>
///
/// <para>⚠ A <c>const</c>, deliberately, rather than configuration: it is a statement about the
/// state of the *data* that has to be removed by a deploy, not a knob to be turned per environment —
/// the same reason the backup container is discovered rather than configured. It also means the
/// tests can name both policies without a host.</para>
/// </summary>
public static class ServiceChefPolicy
{
    public const ServiceChefSourcePolicy InForce = ServiceChefSourcePolicy.SourceNoteOnly;
}
