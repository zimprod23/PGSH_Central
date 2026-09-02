using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Application.Abstractions.Messaging;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.SharedKernel;

namespace PGSH.Application.AcademicGroups.Manage;

/// <summary>
/// Distributes a level's unassigned students into groups of the requested size.
///
/// <para><b>Groups never mix CNPN texts.</b> A group rotates through a stage set together, so two
/// students owing different sets cannot share one — no rotation would satisfy both. Since arrêté
/// 1650.25 a level holds students of two texts (from 2026-2027 the third year holds those arriving
/// on the six-year CNPN and those repeating under the seven-year one), so the split is by
/// (year, level, <c>CnpnVersionId</c>) and each text gets whole groups of its own.</para>
/// </summary>
internal sealed class AutoArrangeGroupsCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<AutoArrangeGroupsCommand, BulkResponse<Guid, int>>
{
    public async Task<Result<BulkResponse<Guid, int>>> Handle(
        AutoArrangeGroupsCommand request, CancellationToken cancellationToken)
    {
        // Only a promotion is arranged into groups. « Retrait » (year 0) is a withdrawal marker the
        // legacy import kept as a level — see Level.IsPromotion — and the students carrying it left;
        // building rosters for them would put the withdrawn into a rotation that does not exist, since
        // the marker has no stages at all.
        var level = await dbContext.Levels
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LevelId, cancellationToken);

        if (level is null)
            return Result.Failure<BulkResponse<Guid, int>>(LevelErrors.NotFound(request.LevelId));

        if (!level.IsPromotion)
            return Result.Failure<BulkResponse<Guid, int>>(
                LevelErrors.NotAPromotion(level.Label ?? $"niveau {request.LevelId}"));

        // ⚠ The holds are Included, not filtered away in SQL: a held registration is not a row this
        // handler may pretend it never saw. Cutting a promotion silently one student short is the
        // failure mode the flag exists to remove — it looks exactly like a promotion that size — so
        // the held ones are read, excluded from the cut, and returned as named refusals below.
        var candidates = await dbContext.Registrations
            .Include(r => r.Holds)
            .Where(r => r.LevelId == request.LevelId &&
                        r.AcademicYearId == request.AcademicYearId &&
                        r.AcademicGroupId == null)
            .OrderBy(r => r.Student.LastName)
            .ToListAsync(cancellationToken);

        var held = candidates.Where(RegistrationHoldPolicy.IsOnHold).ToList();
        var registrations = candidates.Where(RegistrationHoldPolicy.IsPlannable).ToList();

        // ⚠ Held rows are counted here, so « tous les étudiants restants sont signalés » is a report
        // naming each of them rather than « aucun étudiant non affecté » — two states that call for
        // opposite acts and that the old message collapsed into one.
        var itemResults = held
            .Select(r => new BulkItemResult<Guid, int>(
                r.StudentId,
                default,
                HeldError(r)))
            .ToList();

        if (registrations.Count == 0)
        {
            if (itemResults.Count > 0)
                return Result.Success(new BulkResponse<Guid, int>(
                    itemResults, itemResults.Count, 0, itemResults.Count));

            return Result.Failure<BulkResponse<Guid, int>>(Error.NotFound(
                "Groups.NoUnassignedStudents",
                "No unassigned students found for the selected level and year."));
        }

        // ⚠ The registration's own stamp decides the bucket; the student's is only the fallback for a
        // row the resolution never reached. A roster is built for one year, and what its members owe
        // that year is what the text governing *that* registration requires — which is precisely what
        // stops agreeing with the student's current stamp the moment an effectivity rule moves him
        // mid-cursus, i.e. exactly the case groups must not mix.
        var studentIds = registrations.Select(r => r.StudentId).ToList();

        var studentStamps = await dbContext.Students
            .AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.CnpnVersionId })
            .ToDictionaryAsync(s => s.Id, s => s.CnpnVersionId, cancellationToken);

        int? CnpnOf(Registration r) => r.CnpnVersionId ?? studentStamps.GetValueOrDefault(r.StudentId);

        var versionCodes = await dbContext.CnpnVersions
            .AsNoTracking()
            .ToDictionaryAsync(v => v.Id, v => v.Code, cancellationToken);

        // GroupNumber is unique per (year, promotion), so the count restarts at 1 for each — which is
        // how the faculty numbers them and how they are printed: the 3rd year runs 1-80 and the 5th
        // year 1-60, at the same time. It used to continue from the year's highest number, because
        // the index spanned the year alone; the répartition then had to print a 5th year whose groups
        // began at 81.
        int nextNumber = (await dbContext.AcademicGroups
            .Where(g => g.AcademicYearId == request.AcademicYearId && g.LevelId == request.LevelId)
            .Select(g => (int?)g.GroupNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        // Include the level label so admins can tell which level owns each group
        string levelLabel = level.Label ?? $"Niveau {request.LevelId}";

        // One run of the loop per text present at this level. Students with no stamp form their own
        // bucket rather than being folded into someone else's: an unassigned CNPN is a question for
        // scolarité, and silently grouping them with a text they may not follow answers it wrongly.
        var buckets = registrations
            .GroupBy(CnpnOf)
            .OrderBy(g => g.Key ?? int.MaxValue)
            .ToList();

        foreach (var bucket in buckets)
        {
            var members = bucket.ToList();
            int groupCount = (int)Math.Ceiling((double)members.Count / request.GroupSize);

            // The code only qualifies the label when there is something to distinguish: naming every
            // group after its CNPN would be noise in the ordinary case where a level has just one.
            string suffix = buckets.Count > 1
                ? bucket.Key is { } versionId
                    ? $" [{versionCodes.GetValueOrDefault(versionId, versionId.ToString())}]"
                    : " [CNPN à confirmer]"
                : string.Empty;

            var newGroups = Enumerable.Range(0, groupCount)
                .Select(i => new AcademicGroup
                {
                    Label          = $"Groupe {nextNumber + i} — {levelLabel}{suffix}",
                    GroupNumber    = nextNumber + i,
                    AcademicYearId = request.AcademicYearId,
                    LevelId        = request.LevelId,
                })
                .ToList();

            dbContext.AcademicGroups.AddRange(newGroups);
            await dbContext.SaveChangesAsync(cancellationToken);

            for (int i = 0; i < newGroups.Count; i++)
            {
                foreach (var reg in members.Skip(i * request.GroupSize).Take(request.GroupSize))
                {
                    reg.AcademicGroupId = newGroups[i].Id;
                    itemResults.Add(new BulkItemResult<Guid, int>(reg.StudentId, newGroups[i].Id, null));
                }
            }

            nextNumber += groupCount;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResponse<Guid, int>(
            itemResults,
            candidates.Count,
            itemResults.Count(x => x.IsSuccess),
            itemResults.Count(x => !x.IsSuccess)));
    }

    /// <summary>
    /// The refusal for one held registration, carrying the hold's own evidence. Where several stand,
    /// the oldest is named: it is the one that has been waiting longest for a decision.
    /// </summary>
    private static Error HeldError(Registration registration)
    {
        var hold = registration.Holds
            .Where(h => h.ReleasedOn is null)
            .OrderBy(h => h.RaisedOn)
            .First();

        return RegistrationErrors.Held(hold.Reason, hold.Evidence);
    }
}
