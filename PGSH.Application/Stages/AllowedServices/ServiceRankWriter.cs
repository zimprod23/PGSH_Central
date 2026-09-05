using Microsoft.EntityFrameworkCore;
using PGSH.Application.Abstractions.Data;
using PGSH.Domain.Stages;
using PGSH.SharedKernel;

namespace PGSH.Application.Stages.AllowedServices;

/// <summary>
/// Owns a stage's rotation order on disk: every write that can change which service holds which
/// position goes through here, so the ranks are re-based to a contiguous 1..n by one piece of code
/// rather than by three.
///
/// <para>⚠ <b>Each write is two statements, never one.</b> <c>IX_StageAllowedServices_Stage_Rank</c>
/// is unique and non-deferrable, so PostgreSQL checks it as each row is written: swapping ranks 1
/// and 2 in a single <c>SaveChanges</c> leaves the order of the two <c>UPDATE</c>s to EF, and one of
/// the two orders violates the constraint half-way through. The rows are parked on their negative
/// ranks first — distinct among themselves and unable to collide with any positive one — then set.
/// Same shape, and the same reason, as <c>SetCurrentAcademicYearCommandHandler</c> saving the
/// demotion before the promotion.</para>
///
/// <para>⚠ <b>…and the pair is one transaction, because the state between them is worse than either
/// end.</b> A connection dropped mid-write would leave every rank negative, which
/// <see cref="ServiceRotationOrder.SortKeyOf"/> reads as « nobody chose », so the stage would
/// silently fall back to id order — the pre-feature behaviour, restored without anybody being told,
/// on a stage somebody had just ordered by hand.</para>
///
/// <para>⚠ <b>Every row this class writes is loaded inside its own transaction.</b>
/// <c>ExecuteAtomicallyAsync</c> opens each attempt with <c>ChangeTracker.Clear()</c> — a retry has
/// to drop what the failed attempt tracked, or it inserts it twice — so an entity handed in from
/// outside would be detached by the time it is mutated and <c>SaveChanges</c> would write nothing.
/// That is the same defect <c>CnpnTargetPlanner</c> had from an <c>AsNoTracking()</c>: preview
/// right, apply reporting success, not one row moved.</para>
/// </summary>
internal sealed class ServiceRankWriter(IApplicationDbContext dbContext)
{
    /// <summary>
    /// The service ids of a stage, in the order the rotation currently walks them — for <i>deciding</i>
    /// what the new order should be.
    /// </summary>
    /// <remarks>
    /// Deliberately <c>AsNoTracking</c>: this read informs a decision and never a mutation, and the
    /// rows it would track are about to be dropped by the transaction anyway.
    /// </remarks>
    public async Task<List<int>> ReadOrderAsync(int stageId, CancellationToken cancellationToken) =>
        (await OrderedQuery(dbContext, stageId).AsNoTracking().ToListAsync(cancellationToken))
        .Select(a => a.ServiceId)
        .ToList();

    /// <summary>Re-bases the stage's ranks to express <paramref name="serviceIdsInOrder"/>.</summary>
    public Task<Result> ApplyAsync(
        int stageId, IReadOnlyList<int> serviceIdsInOrder, CancellationToken cancellationToken) =>
        WriteAsync(stageId, serviceIdsInOrder, insertServiceId: null, removeServiceId: null, cancellationToken);

    /// <summary>Authorises a service and gives it the last position in the order.</summary>
    /// <remarks>
    /// Last, because a service newly added to the list has no claim on a position somebody chose for
    /// the ones already there. Reordering afterwards is its own act.
    /// </remarks>
    public async Task<Result> AppendAsync(int stageId, int serviceId, CancellationToken cancellationToken)
    {
        var order = await ReadOrderAsync(stageId, cancellationToken);

        return await WriteAsync(
            stageId,
            ServiceRotationOrder.Append(order, serviceId),
            insertServiceId: order.Contains(serviceId) ? null : serviceId,
            removeServiceId: null,
            cancellationToken);
    }

    /// <summary>Withdraws a service and closes the hole its position leaves.</summary>
    public async Task<Result> RemoveAsync(int stageId, int serviceId, CancellationToken cancellationToken)
    {
        var order = await ReadOrderAsync(stageId, cancellationToken);

        return await WriteAsync(
            stageId,
            ServiceRotationOrder.Without(order, serviceId),
            insertServiceId: null,
            removeServiceId: order.Contains(serviceId) ? serviceId : null,
            cancellationToken);
    }

    /// <summary>
    /// The one write. Insertion, removal and re-basing land in a single transaction because they are
    /// a single act: the row added is the row being ranked last, and the row removed is the reason
    /// the survivors move up.
    /// </summary>
    private async Task<Result> WriteAsync(
        int stageId,
        IReadOnlyList<int> serviceIdsInOrder,
        int? insertServiceId,
        int? removeServiceId,
        CancellationToken cancellationToken) =>
        await dbContext.ExecuteAtomicallyAsync<int>(async ct =>
        {
            var rows = await OrderedQuery(dbContext, stageId).ToListAsync(ct);
            var ranking = ServiceRotationOrder.RanksFor(serviceIdsInOrder);

            var survivors = rows.Where(r => r.ServiceId != removeServiceId).ToList();

            // ⚠ Re-checked here, and not only by the caller: the caller decided the order from a read
            // taken *before* this transaction, so a service authorised in between would arrive as a
            // row the ranking says nothing about — it would keep a positive rank, collide with a
            // ranked one, and surface as a constraint violation, i.e. a 500 naming an index. The
            // caller's own sentence is the right answer, and « reload the page » is the right advice.
            int expected = survivors.Count + (insertServiceId is null ? 0 : 1);
            if (ranking.Count != expected || survivors.Any(r => !ranking.ContainsKey(r.ServiceId)))
                return Result.Failure<int>(StageErrors.ServiceOrderIsStale(expected, ranking.Count));

            // Only the rows that actually move need parking, and that is not an optimisation for its
            // own sake: a stationary row keeps a rank no mover can target, because the ranking is a
            // bijection onto 1..n and that rank is already taken by the row holding it. So an arrow
            // click parks two rows instead of eighteen, and authorising a service — which appends at
            // the end and moves nobody — parks none and becomes a single INSERT.
            var movers = survivors.Where(r => r.Rank != ranking[r.ServiceId]).ToList();

            if (movers.Count == 0 && insertServiceId is null && removeServiceId is null)
                return Result.Success(0);

            if (removeServiceId is { } removed)
                dbContext.StageAllowedServices.RemoveRange(
                    rows.Where(r => r.ServiceId == removed));

            // ⚠ Parked before anything is inserted or set: the unique index is checked as each row is
            // written, so a 1↔2 swap in one statement leaves the order of the two UPDATEs to EF and
            // one of the two orders violates it half-way through. Negative ranks are distinct among
            // themselves and cannot collide with any positive one.
            foreach (var row in movers)
                row.Rank = -ranking[row.ServiceId];

            await dbContext.SaveChangesAsync(ct);

            if (insertServiceId is { } inserted)
                dbContext.StageAllowedServices.Add(new StageAllowedService
                {
                    StageId = stageId,
                    ServiceId = inserted,
                    Rank = ranking[inserted],
                });

            foreach (var row in movers)
                row.Rank = -row.Rank;

            await dbContext.SaveChangesAsync(ct);

            return Result.Success(movers.Count);
        }, cancellationToken);

    /// <summary>
    /// A stage's authored positions, read flat and keyed on the stage id — the lookup both
    /// <c>RotationArranger</c> and the stage's own fiche resolve the order through.
    /// </summary>
    /// <remarks>
    /// ⚠ Here rather than beside either caller: the arranger is the planning engine and the fiche is
    /// a read screen, so putting it on one would have made the other depend on it for a fact that
    /// belongs to neither. Named and <c>internal static</c> so <c>SqlTranslationTests</c> can compile
    /// it without a database — a projected join row is a computed element carrying no key, the shape
    /// Npgsql refuses inside a collection, so this must stay a top-level query.
    /// </remarks>
    internal static IQueryable<ServiceRank> RanksQuery(IApplicationDbContext dbContext, int stageId) =>
        dbContext.StageAllowedServices
            .AsNoTracking()
            .Where(a => a.StageId == stageId)
            .Select(a => new ServiceRank(a.ServiceId, a.Rank));

    /// <summary>
    /// A stage's join rows in the order the rotation walks them.
    /// </summary>
    /// <remarks>
    /// ⚠ Unranked rows (<c>Rank</c> defaults to 0) sort <b>last</b>, matching
    /// <see cref="ServiceRotationOrder.SortKeyOf"/>. On the raw value a 0 would sort ahead of every
    /// service somebody deliberately placed. Named and <c>internal static</c> for the usual reason:
    /// a query buried in a private async method cannot be handed to <c>ToQueryString()</c>.
    /// </remarks>
    internal static IQueryable<StageAllowedService> OrderedQuery(
        IApplicationDbContext dbContext, int stageId) =>
        dbContext.StageAllowedServices
            .Where(a => a.StageId == stageId)
            .OrderBy(a => a.Rank <= 0)
            .ThenBy(a => a.Rank)
            .ThenBy(a => a.ServiceId);

    internal sealed record ServiceRank(int ServiceId, int Rank);
}
