using System.Globalization;
using PGSH.Application.Hospitals.Chefs;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using PGSH.Domain.Users;

namespace PGSH.Application.Exports;

/// <summary>
/// The French wording of every enum that reaches an exported cell.
///
/// <para>An export is read by people who never see the API: <c>NonÉvalué</c>, <c>Withdrawn</c> and
/// <c>PayeeAmie</c> are identifiers, not words a scolarité document may print. This is the single
/// place they are turned into French, so two exports cannot call the same verdict two things — the
/// same reason <see cref="StageScoring"/> and <see cref="ServicePeriodLifecycle"/> exist.</para>
///
/// <para>⚠ It is deliberately <em>not</em> a general-purpose i18n table. The API keeps sending enum
/// names (<c>JsonStringEnumConverter</c>) and the frontend keeps translating them; this covers the
/// one output that leaves the system as a finished document.</para>
/// </summary>
public static class ExportLabels
{
    /// <summary>
    /// The document's own culture. ⚠ Not <c>CurrentCulture</c>: the API process's culture is whatever
    /// the host happens to set, and it reached the page — a caption built with <c>:N0</c> printed
    /// « 5.932 inscription(s) » against a roll of five thousand nine hundred and thirty-two, which in
    /// French reads as five-point-nine-three-two. A file that leaves the system states its own
    /// language and its own separators.
    /// </summary>
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>A count as the document should print it — grouped, no decimals.</summary>
    public static string Count(int value) => value.ToString("N0", Fr);

    public static string Program(AcademicProgram program) => program switch
    {
        AcademicProgram.Medecine  => "Médecine",
        AcademicProgram.Pharmacie => "Pharmacie",
        AcademicProgram.Master    => "Master",
        AcademicProgram.Doctorat  => "Doctorat",
        _                         => program.ToString(),
    };

    public static string Gender(Gender gender) => gender switch
    {
        Domain.Users.Gender.Male   => "M",
        Domain.Users.Gender.Female => "F",
        _                          => "",
    };

    public static string Agreement(AgreementType agreement) => agreement switch
    {
        AgreementType.None          => "",
        AgreementType.PayeeAmie     => "Payée amie",
        AgreementType.International => "International",
        _                           => "Autre",
    };

    public static string RegistrationStatus(RegistrationStatus status) => status switch
    {
        Domain.Registrations.RegistrationStatus.Pending   => "En attente",
        Domain.Registrations.RegistrationStatus.Active    => "En cours",
        Domain.Registrations.RegistrationStatus.Validated => "Admis",
        Domain.Registrations.RegistrationStatus.Failed    => "Redoublant",
        Domain.Registrations.RegistrationStatus.Withdrawn => "Abandon",
        Domain.Registrations.RegistrationStatus.Graduated => "Diplômé",
        Domain.Registrations.RegistrationStatus.Excluded  => "Exclu",
        _                                                 => status.ToString(),
    };

    /// <summary>
    /// ⚠ Kept in the document because a verdict nobody pronounced and a verdict PGSH guessed are not
    /// the same fact — the whole point of <c>RegistrationOutcomeSource</c>. An empty cell means
    /// nobody has ruled yet, which is every legacy year.
    /// </summary>
    public static string OutcomeSource(RegistrationOutcomeSource? source) => source switch
    {
        RegistrationOutcomeSource.Declared => "Déclarée (PV)",
        RegistrationOutcomeSource.Inferred => "Déduite",
        _                                  => "",
    };

    public static string InternshipStatus(InternshipStatus status) => status switch
    {
        Domain.Common.Utils.InternshipStatus.Planned   => "Planifié",
        Domain.Common.Utils.InternshipStatus.Ongoing   => "En cours",
        Domain.Common.Utils.InternshipStatus.Completed => "Terminé",
        Domain.Common.Utils.InternshipStatus.Evaluated => "Évalué",
        Domain.Common.Utils.InternshipStatus.Validated => "Validé",
        Domain.Common.Utils.InternshipStatus.Rejected  => "Rejeté",
        Domain.Common.Utils.InternshipStatus.Paused    => "Suspendu",
        _                                              => status.ToString(),
    };

    public static string StageResult(StageAssignmentResult? result) => result switch
    {
        StageAssignmentResult.Validé     => "Validé",
        StageAssignmentResult.NonValidé  => "Non validé",
        StageAssignmentResult.NonÉvalué  => "Non évalué",
        _                                => "Non évalué",
    };

    public static string PeriodState(ServicePeriodState state) => state switch
    {
        ServicePeriodState.Planned            => "Planifiée",
        ServicePeriodState.Underway           => "En cours",
        ServicePeriodState.AwaitingEvaluation => "À évaluer",
        _                                     => "Clôturée",
    };

    public static string RotationMode(StageRotationMode mode) => mode switch
    {
        StageRotationMode.SingleService => "Service unique",
        _                               => "Rotation par période",
    };

    /// <summary>
    /// Where a printed chef's name came from.
    ///
    /// <para>⚠ <b>Never omitted beside the name.</b> 140 of the 148 imported services name their
    /// professor only in a free-text note the Access base last recorded, and that note is
    /// <em>undated</em>: printing it unqualified claims this student served under that chef, which
    /// nothing in the base supports. A dated tenure does support it, and the two must be
    /// distinguishable in the file — same reason <c>OutcomeSource</c> and <c>CnpnSource</c> exist.</para>
    ///
    /// <para>« Mixte » is a multi-service rotation where one leg is on record and another is not.</para>
    /// </summary>
    public static string ChefOrigin(IReadOnlyList<ServiceChefAttribution> attributions)
    {
        var named = attributions.Where(a => a.Name is not null).ToList();
        if (named.Count == 0)
            return "";

        bool anyNote = named.Any(a => a.FromSourceNote);
        bool anyRecord = named.Any(a => !a.FromSourceNote);

        return (anyRecord, anyNote) switch
        {
            (true, true) => "Mixte",
            (_, true)    => "Note (import)",
            _            => "Affectation",
        };
    }

    /// <summary>« 3ᵉ année Médecine » from the parts, for the rows where no label was authored.</summary>
    public static string Level(string? label, int year, AcademicProgram program) =>
        !string.IsNullOrWhiteSpace(label) ? label! : $"Année {year} — {Program(program)}";
}
