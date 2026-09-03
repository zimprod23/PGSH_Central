using PGSH.Domain.Hospitals;

namespace PGSH.Application.Hospitals.Chefs;

/// <summary>
/// Who led a set of services, answerable <b>as of any date</b>.
///
/// <para>Pure and immutable, built once by <see cref="ServiceChefProvider"/> — the same split as
/// <c>WorkingDayProvider</c> / <c>WorkingDayCalendar</c>, and for the same reason: the resolution
/// order is a rule about authority, and a rule that can be tested without a store is a rule that
/// gets tested.</para>
///
/// <para><b>Why it is shared.</b> The répartition owned a private copy of this order, and the stage
/// export needs the same answer. Two documents of one faculty disagreeing about who leads a service
/// is exactly the drift <c>StageScoring</c> and <c>ServicePeriodLifecycle</c> exist to prevent.</para>
///
/// <para>⚠ <b>The as-of date is per question, not per build.</b> The répartition asks once, at the
/// start of the axis; the export asks per période, because a file covering a year of rotations spans
/// months and a chef who took over in January did not lead the students who were there in October.
/// Answering the whole file from one date would print the wrong name on half of it.</para>
///
/// <para><b>Which sources it may answer from is the caller's</b> —
/// <see cref="ServiceChefSourcePolicy"/>, decided once in <see cref="ServiceChefPolicy"/> so the two
/// documents cannot narrow differently. The order below is the rule; the policy says how much of it
/// is in force while the linked chefs are test accounts.</para>
/// </summary>
public sealed class ServiceChefDirectory
{
    public static readonly ServiceChefDirectory Empty =
        new([], ServiceChefSourcePolicy.SourceNoteOnly);

    private readonly Dictionary<int, ServiceChefRecord> _services;
    private readonly ServiceChefSourcePolicy _policy;

    internal ServiceChefDirectory(
        IReadOnlyList<ServiceChefRecord> services, ServiceChefSourcePolicy policy)
    {
        _services = services.ToDictionary(s => s.ServiceId);
        _policy = policy;
    }

    /// <summary>
    /// The name to print for <paramref name="serviceId"/> on <paramref name="asOf"/>, and where it
    /// came from. Three sources, in descending order of authority:
    ///
    /// <list type="number">
    /// <item>the tenure open on <paramref name="asOf"/> — dated, and the only one that survives a
    /// reprint years later;</item>
    /// <item>the sitting chef, for services whose tenure trail predates the audit trail;</item>
    /// <item>the legacy note in the description (<see cref="ServiceChefSourceNote"/>) — undated, and
    /// the only name available for 140 of the 148 imported services.</item>
    /// </list>
    ///
    /// ⚠ The third is <b>flagged rather than blended in</b>. It says who the Access base last
    /// recorded, not who this document was published under, and only the caller can decide whether
    /// that distinction matters on its page — hence <see cref="ServiceChefAttribution.FromSourceNote"/>.
    ///
    /// <para>Under <see cref="ServiceChefSourcePolicy.SourceNoteOnly"/> the first two are skipped
    /// and only the note answers, so every name comes back flagged. A service whose note is absent
    /// then names nobody <em>even though a chef is linked</em> — which is the intended reading: the
    /// link is a test row, and a blank cell says less wrongly than a wrong name.</para>
    /// </summary>
    public ServiceChefAttribution For(int serviceId, DateOnly asOf)
    {
        if (!_services.TryGetValue(serviceId, out var service))
            return ServiceChefAttribution.Unknown;

        if (_policy is ServiceChefSourcePolicy.Authority)
        {
            // Most recently opened tenure covering the date: two overlapping tenures are a data
            // defect, and the later one is the better guess about which replaced which.
            var tenure = service.Tenures
                .Where(t => t.Start <= asOf && (t.End is null || t.End >= asOf))
                .OrderByDescending(t => t.Start)
                .Select(t => t.Name)
                .FirstOrDefault();

            string? configured = Normalize(tenure) ?? Normalize(service.SittingChefName);
            if (configured is not null)
                return new ServiceChefAttribution(configured, FromSourceNote: false);
        }

        string? fromNote = ServiceChefSourceNote.Read(service.Description);
        return new ServiceChefAttribution(fromNote, FromSourceNote: fromNote is not null);
    }

    /// <summary>
    /// Whether a chef <b>is</b> linked in Personnel for this service on <paramref name="asOf"/> and
    /// <see cref="For"/> is nonetheless not printing them — i.e. the policy is holding a real
    /// affectation back.
    ///
    /// <para>⚠ <b>The screen that shows the trail needs this, or it recreates the confusion the
    /// policy was meant to remove.</b> Pédiatrie1 and Pédiatrie2 carry the base's only two chef
    /// affectations, both open; a page listing them under « Historique » while its headline names
    /// the import note says nothing about *why* the two differ, which is exactly the « d'où sort ce
    /// nom ? » this answers. Same rule as <c>ExportNotes</c>: what a narrowing removed has to
    /// announce itself.</para>
    ///
    /// <para>Always false under <see cref="ServiceChefSourcePolicy.Authority"/> — the order falling
    /// through to the note because every tenure is closed is the rule working, not a name withheld.
    /// It is also false where nobody is linked at all: « aucun chef désigné » is a different
    /// sentence and the caller must be able to print it.</para>
    /// </summary>
    public bool HasWithheldLinkedChef(int serviceId, DateOnly asOf)
    {
        if (_policy is ServiceChefSourcePolicy.Authority)
            return false;

        return _services.TryGetValue(serviceId, out var service)
               && (Normalize(service.SittingChefName) is not null
                   || service.Tenures.Any(t => t.Start <= asOf && (t.End is null || t.End >= asOf)));
    }

    private static string? Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim();
}

/// <summary>
/// The name a document prints for a service, and whether it is the dated record or the legacy note.
/// ⚠ Never collapse the two into a bare string: printing an undated import note as though it were
/// the chef the plan was published under is the kind of claim a reprint cannot defend.
/// </summary>
public readonly record struct ServiceChefAttribution(string? Name, bool FromSourceNote)
{
    public static readonly ServiceChefAttribution Unknown = new(null, false);
}

/// <summary>One service's three sources, as read from the store. Internal — the answer is
/// <see cref="ServiceChefDirectory.For"/>, never these fields separately.</summary>
internal sealed record ServiceChefRecord(
    int ServiceId,
    string? SittingChefName,
    string? Description,
    IReadOnlyList<ServiceChefTenure> Tenures);

internal sealed record ServiceChefTenure(string? Name, DateOnly Start, DateOnly? End);
