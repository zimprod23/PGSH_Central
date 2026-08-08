using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn;

/// <summary>
/// Decides which CNPN governs a student, and stamps it.
///
/// <para><b>The rule comes from the text, not from us.</b> Arrêté 1650.25 art. 2 assigns by
/// <i>date of first registration</i> — students registered before 2024-2025 stay under the previous
/// arrêté — and says nothing about the level anyone currently sits in. Those two criteria agree for a
/// student who never repeated and disagree for one who did, and 2,635 students in the imported
/// history have repeated a level. Entry wins.</para>
///
/// <para><b>Entry is not always recorded.</b> The legacy import only carried students once they had
/// stages, so roughly 2,200 currently-enrolled students have no registration before 2025-2026 even
/// though they plainly did not start that year. Where the earliest registration is missing, entry is
/// deduced from the level they sit in now — you cannot be in the third year without having spent two
/// — and the assignment is flagged <c>IsInferred</c> for scolarité rather than presented as fact.
/// The deduction assumes no unrecorded repeat, which is the one assumption in the whole
/// backfill.</para>
/// </summary>
public sealed class CnpnAssignment(IApplicationDbContext dbContext)
{
    /// <summary>What governs a student, and how confident the answer is.</summary>
    public sealed record Resolution(int CnpnVersionId, bool IsInferred, int EntryAcademicYearId, string Basis);

    /// <summary>
    /// Resolves the CNPN for one student. <paramref name="asOfAcademicYearId"/> anchors the level-based
    /// deduction when entry is unrecorded; it is otherwise unused, because the current year must never
    /// influence which text applies.
    /// </summary>
    public async Task<Result<Resolution>> ResolveAsync(
        Guid studentId, int asOfAcademicYearId, CancellationToken ct)
    {
        var registrations = await dbContext.Registrations
            .AsNoTracking()
            .Where(r => r.StudentId == studentId)
            .Select(r => new
            {
                r.AcademicYearId,
                YearStart = r.AcademicYear.StartDate,
                LevelYear = r.Level.Year,
                Program   = r.Level.AcademicProgram,
            })
            .ToListAsync(ct);

        if (registrations.Count == 0)
            return Result.Failure<Resolution>(CnpnErrors.NoRegistration(studentId));

        var earliest = registrations.OrderBy(r => r.YearStart).First();
        var program = registrations.OrderByDescending(r => r.YearStart).First().Program;

        var years = await dbContext.AcademicYears
            .AsNoTracking()
            .OrderBy(y => y.StartDate)
            .Select(y => new YearRef(y.Id, y.StartDate))
            .ToListAsync(ct);

        // A first registration at level 1 is a genuine entry; at any higher level it is only the
        // first year we happen to hold, and the real entry is (level - 1) years earlier.
        bool recorded = earliest.LevelYear <= 1;

        int entryYearId = recorded
            ? earliest.AcademicYearId
            : DeduceEntryYearId(years, earliest.AcademicYearId, earliest.LevelYear);

        var version = await SelectVersionAsync(program, entryYearId, ct);
        if (version.IsFailure)
            return Result.Failure<Resolution>(version.Error);

        string basis = recorded
            ? "première inscription enregistrée"
            : $"déduite du niveau {earliest.LevelYear} à la première inscription connue";

        return new Resolution(version.Value, !recorded, entryYearId, basis);
    }

    /// <summary>
    /// The text governing an intake: the version for the programme whose
    /// <c>AppliesToEntrantsFrom</c> is the latest one at or before <paramref name="entryYearId"/>.
    /// Versions with no such year are recorded for history and never selected.
    /// </summary>
    public async Task<Result<int>> SelectVersionAsync(
        AcademicProgram program, int entryYearId, CancellationToken ct)
    {
        var entryStart = await dbContext.AcademicYears
            .AsNoTracking()
            .Where(y => y.Id == entryYearId)
            .Select(y => (DateOnly?)y.StartDate)
            .FirstOrDefaultAsync(ct);

        if (entryStart is null)
            return Result.Failure<int>(StageErrors.AcademicYearNotFound(entryYearId));

        var candidates = await dbContext.CnpnVersions
            .AsNoTracking()
            .Where(v => v.AcademicProgram == program && v.AppliesToEntrantsFromAcademicYearId != null)
            .Select(v => new
            {
                v.Id,
                From = v.AppliesToEntrantsFromAcademicYear!.StartDate,
            })
            .ToListAsync(ct);

        var governing = candidates
            .Where(v => v.From <= entryStart.Value)
            .OrderByDescending(v => v.From)
            .FirstOrDefault();

        return governing is null
            ? Result.Failure<int>(CnpnErrors.NoVersionForIntake(program, entryStart.Value))
            : Result.Success(governing.Id);
    }

    private sealed record YearRef(int Id, DateOnly StartDate);

    /// <summary>
    /// Walks back <paramref name="levelYear"/> - 1 academic years from the first one on record. Falls
    /// back to the earliest known year when history does not reach far enough — which still lands
    /// before any modern CNPN, so the answer stays right even when the exact year does not.
    /// </summary>
    private static int DeduceEntryYearId(
        IReadOnlyList<YearRef> orderedYears, int firstKnownYearId, int levelYear)
    {
        int index = orderedYears.ToList().FindIndex(y => y.Id == firstKnownYearId);
        if (index < 0) return firstKnownYearId;

        return orderedYears[Math.Max(0, index - (levelYear - 1))].Id;
    }
}
