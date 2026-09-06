using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;

namespace PGSH.Application.Stages.Delocalization.Bulk;

/// <summary>
/// Turns « le G3 au complet, plus ces douze-là, plus la liste du formulaire » into a set of
/// registration ids — and into a row for each line that names nobody.
/// </summary>
/// <remarks>
/// <para>Its own class because it answers a different question from the planner's. The planner asks
/// <i>what would happen to these students</i>; this asks <i>which students did the operator mean</i>,
/// and the two fail for unrelated reasons — a typo in a pasted CNE has nothing to do with a stage
/// carrying a mark. Keeping them together made one object own both an identifier grammar and a
/// délocalisation's preconditions.</para>
///
/// <para>⚠ <b>A line that resolves to nobody comes back as a row, never as silence.</b> Dropping it
/// is the defect that silently lost 182 students of a réinscription roll: the file was applied, the
/// report said nothing, and the only trace was a spreadsheet nobody re-read.</para>
/// </remarks>
internal sealed class DelocalizationTargetResolver(IApplicationDbContext dbContext)
{
    /// <param name="RegistrationIds">
    /// The registrations to plan, each mapped to the identifier it was typed as — null when it was
    /// named by id or reached through a roster. It travels so an unmatched line can be found in the
    /// file it came from.
    /// </param>
    /// <param name="Unresolved">The lines that name nobody in this year, already worded.</param>
    internal sealed record ResolvedTargets(
        IReadOnlyDictionary<Guid, string?> RegistrationIds,
        IReadOnlyList<BulkDelocalizationRow> Unresolved);

    public async Task<ResolvedTargets> ResolveAsync(
        DelocalizationTargets targets, int academicYearId, CancellationToken ct)
    {
        var resolved = new Dictionary<Guid, string?>();
        var unresolved = new List<BulkDelocalizationRow>();

        await AddRostersAsync(targets, academicYearId, resolved, ct);
        await AddNamedRegistrationsAsync(targets, academicYearId, resolved, unresolved, ct);
        await AddIdentifiersAsync(targets, academicYearId, resolved, unresolved, ct);

        return new ResolvedTargets(resolved, unresolved);
    }

    private async Task AddRostersAsync(
        DelocalizationTargets targets, int academicYearId,
        Dictionary<Guid, string?> resolved, CancellationToken ct)
    {
        var groupIds = targets.AcademicGroupIds?.Distinct().ToList() ?? [];
        if (groupIds.Count == 0)
            return;

        // ⚠ The year predicate is not redundant: a roster is keyed (year, level, number), so a group
        // id belongs to exactly one promotion, and being handed one from another year is a mistake to
        // refuse rather than a filter to widen.
        var fromGroups = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.AcademicGroupId != null
                     && groupIds.Contains(r.AcademicGroupId.Value)
                     && r.AcademicYearId == academicYearId)
            .Select(r => r.Id)
            .ToListAsync(ct);

        foreach (var id in fromGroups)
            resolved[id] = null;
    }

    private async Task AddNamedRegistrationsAsync(
        DelocalizationTargets targets, int academicYearId,
        Dictionary<Guid, string?> resolved, List<BulkDelocalizationRow> unresolved, CancellationToken ct)
    {
        var explicitIds = targets.RegistrationIds?.Distinct().ToList() ?? [];
        if (explicitIds.Count == 0)
            return;

        var found = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => explicitIds.Contains(r.Id))
            .Select(r => new IdentifierMatch(
                r.Id, r.AcademicYearId, r.Student.CNE, r.Student.Appogee,
                r.Student.FirstName, r.Student.LastName))
            .ToListAsync(ct);

        foreach (var id in explicitIds)
        {
            var match = found.FirstOrDefault(f => f.Id == id);

            if (match is null)
            {
                unresolved.Add(new BulkDelocalizationRow(
                    id, $"Inscription {id}", null, null, null,
                    BulkDelocalizationRowStatus.NotFound,
                    "Cette inscription n'existe pas."));
                continue;
            }

            if (match.AcademicYearId != academicYearId)
            {
                unresolved.Add(new BulkDelocalizationRow(
                    id, match.FullName, match.CNE, match.Appogee, null,
                    BulkDelocalizationRowStatus.WrongYear,
                    "Cette inscription appartient à une autre année universitaire."));
                continue;
            }

            resolved.TryAdd(id, null);
        }
    }

    private async Task AddIdentifiersAsync(
        DelocalizationTargets targets, int academicYearId,
        Dictionary<Guid, string?> resolved, List<BulkDelocalizationRow> unresolved, CancellationToken ct)
    {
        var identifiers = targets.Identifiers?
            .Select(i => i.Trim())
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (identifiers.Count == 0)
            return;

        // Lowered on both sides and on both columns. A single field left un-lowered is a silent miss —
        // Appogee was case-sensitive for months, so "ap2200a" never found AP2200A.
        var lowered = identifiers.Select(i => i.ToLower()).ToList();

        var matches = await MatchedIdentifiersQuery(dbContext, lowered).ToListAsync(ct);

        foreach (var identifier in identifiers)
        {
            var forIdentifier = matches.Where(m => m.Matches(identifier)).ToList();
            var inYear = forIdentifier.FirstOrDefault(m => m.AcademicYearId == academicYearId);

            if (inYear is not null)
            {
                resolved.TryAdd(inYear.Id, identifier);
                continue;
            }

            // ⚠ « je ne le trouve pas » and « il est en 5ᵉ, pas en 6ᵉ » are two different corrections,
            // so they are two different answers. This is why the query above is not scoped by year.
            var elsewhere = forIdentifier.FirstOrDefault();

            unresolved.Add(elsewhere is null
                ? new BulkDelocalizationRow(
                    null, identifier, null, null, null,
                    BulkDelocalizationRowStatus.NotFound,
                    "Aucun étudiant ne porte ce CNE ni cet Apogée.", identifier)
                : new BulkDelocalizationRow(
                    null, elsewhere.FullName, elsewhere.CNE, elsewhere.Appogee, null,
                    BulkDelocalizationRowStatus.WrongYear,
                    "Étudiant connu, mais sans inscription sur l'année sélectionnée.", identifier));
        }
    }

    /// <summary>
    /// Every registration whose student carries one of these identifiers, on either column.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately <b>unscoped by year</b>: the caller picks the year's row and reports the
    /// rest as <c>WrongYear</c>.</para>
    ///
    /// <para>⚠ Named so <c>SqlTranslationTests</c> can compile it: a <c>ToLower()</c> on a nullable
    /// column inside a <c>Contains</c> over an in-memory list is a <b>predicate</b>, and a
    /// client-side call in a predicate is the shape a provider refuses — unlike the same call in a
    /// projection, which quietly evaluates on the client and proves nothing.</para>
    /// </remarks>
    internal static IQueryable<IdentifierMatch> MatchedIdentifiersQuery(
        IApplicationDbContext dbContext, IReadOnlyList<string> loweredIdentifiers) =>
        dbContext.Registrations
            .AsNoTracking()
            .Where(r => (r.Student.CNE != null && loweredIdentifiers.Contains(r.Student.CNE.ToLower()))
                     || loweredIdentifiers.Contains(r.Student.Appogee.ToLower()))
            .Select(r => new IdentifierMatch(
                r.Id, r.AcademicYearId, r.Student.CNE, r.Student.Appogee,
                r.Student.FirstName, r.Student.LastName));

    internal sealed record IdentifierMatch(
        Guid Id, int AcademicYearId, string? CNE, string Appogee, string? FirstName, string? LastName)
    {
        public string FullName => $"{FirstName ?? ""} {LastName ?? ""}".Trim();

        public bool Matches(string identifier) =>
            string.Equals(CNE, identifier, StringComparison.OrdinalIgnoreCase)
         || string.Equals(Appogee, identifier, StringComparison.OrdinalIgnoreCase);
    }
}
