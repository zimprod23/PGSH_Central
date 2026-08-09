namespace PGSH.Domain.Registrations;

/// <summary>
/// How a year's verdict came to be recorded. The distinction is load-bearing: PGSH cannot see the
/// pedagogical side of the faculty, so it has two very different ways of learning that a year is
/// over, and a reader who cannot tell them apart will treat a guess as a fact.
/// </summary>
public enum RegistrationOutcomeSource
{
    /// <summary>
    /// The faculty said so — a déliberation canvas signed off by scolarité and uploaded. Authoritative.
    /// </summary>
    Declared,

    /// <summary>
    /// PGSH deduced it from the shape of the enrolment sequence (a later registration at the same
    /// level means this one failed). Only ever applied to the imported years nobody will upload a
    /// file for, and never allowed to overwrite a <see cref="Declared"/> verdict.
    /// </summary>
    Inferred,
}
