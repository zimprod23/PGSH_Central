using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.Cnpn;

/// <summary>
/// Which CNPN governs an intake.
///
/// <para><b>The rule comes from the text, not from us.</b> Arrêté 1650.25 art. 2 assigns by
/// <i>date of first registration</i> — students registered before 2024-2025 stay under the previous
/// arrêté — and says nothing about the level anyone currently sits in. Those two criteria agree for a
/// student who never repeated and disagree for one who did, and 2,635 students in the imported
/// history have repeated a level. Entry wins.</para>
///
/// <para>Entry itself is often unrecorded, and deducing it is
/// <see cref="EntryYearDeduction"/>'s job. This class answers only the half that needs the store:
/// given an intake, which published text was in force.</para>
/// </summary>
public sealed class CnpnAssignment(IApplicationDbContext dbContext)
{
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
}
