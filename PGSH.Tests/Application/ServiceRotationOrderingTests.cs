using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.AllowedServices;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Authoring the order a stage's services are walked in, and what that order actually decides.
///
/// <para>The order used to be <c>OrderBy(Service.Id)</c> — catalogue creation order, i.e. the legacy
/// import order — so nobody had chosen it, while it decides which contiguous run of group numbers
/// lands in which service: <c>BuildServiceQueue</c> emits each service's block consecutively and the
/// earliest column takes phase 0.</para>
///
/// <para>⚠ Why it matters beyond tidiness: a nominative placement (« ces étudiants au HMIMV pour ce
/// stage ») could otherwise only be met by editing a cell on the planning grid afterwards — and the
/// printed répartition <b>shows</b> that edit, because <c>GroupNumberRanges</c> refuses to merge
/// across the hole it leaves (« 21-23, 25-27 » beside a lone « 24 » in another row). Authoring the
/// order makes the same placement fall out of the plan, in clean ranges.</para>
/// </summary>
public class ServiceRotationOrderingTests
{
    private const int First = 10;
    private const int Second = 11;
    private const int Third = 12;

    /// <summary>Three services of equal capacity, six rosters, one période.</summary>
    private static Stage SeedThreeServiceStage(ApplicationDbContext db)
    {
        var stage = db.SeedCatalog();

        db.SeedService(First, "Cardiologie");
        db.SeedService(Second, "Pneumologie");
        db.SeedService(Third, "Rhumatologie");

        db.SeedSlot(stage, 1, 1, new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 25));

        for (int n = 1; n <= 6; n++)
            db.SeedCohortFor(stage, db.SeedGroup(n, n), 100 + n);

        return stage;
    }

    private static Service Svc(ApplicationDbContext db, int id) =>
        db.Services.Local.First(s => s.Id == id);

    /// <summary>The service each group number was placed in, for the stage's single période.</summary>
    private static async Task<Dictionary<int, int>> PlacementByGroupAsync(ApplicationDbContext db) =>
        await db.CohortSlotAssignments
            .Include(a => a.Cohort).ThenInclude(c => c.AcademicGroup)
            .ToDictionaryAsync(a => a.Cohort.AcademicGroup.GroupNumber, a => a.ServiceId);

    [Fact]
    public async Task The_service_ranked_first_receives_the_first_groups_in_the_first_period()
    {
        // This is the whole claim the feature rests on. Six rosters over three equal services is two
        // apiece, blocks emitted consecutively, and the earliest column takes phase 0 — so groups
        // 1-2 go to whichever service is ranked 1, whatever its catalogue id.
        await using var db = TestHarness.NewContext(
            nameof(The_service_ranked_first_receives_the_first_groups_in_the_first_period));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, Third), Svc(db, First), Svc(db, Second));
        await db.SaveChangesAsync();

        var result = await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        result.IsSuccess.Should().BeTrue();

        var placement = await PlacementByGroupAsync(db);
        placement[1].Should().Be(Third, "it was ranked first, whatever its catalogue id");
        placement[2].Should().Be(Third);
        placement[3].Should().Be(First);
        placement[5].Should().Be(Second);
    }

    [Fact]
    public async Task Reordering_moves_the_first_groups_to_the_newly_first_service()
    {
        // The same promotion, the same rosters, the same période — only the authored order differs.
        await using var db = TestHarness.NewContext(
            nameof(Reordering_moves_the_first_groups_to_the_newly_first_service));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, First), Svc(db, Second), Svc(db, Third));
        await db.SaveChangesAsync();

        await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        (await PlacementByGroupAsync(db))[1].Should().Be(First);

        var reorder = await new SetAllowedServiceOrderCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new SetAllowedServiceOrderCommand(TestHarness.StageId, [Second, Third, First]), default);

        reorder.IsSuccess.Should().BeTrue();

        await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        (await PlacementByGroupAsync(db))[1].Should().Be(Second,
            "the order is a planning input — the next arrange reads it");
    }

    [Fact]
    public async Task A_stage_nobody_has_ordered_arranges_exactly_as_it_did_before()
    {
        // The migration backfills the rank from ORDER BY "ServiceId", so applying it changes no
        // plan. In memory, Allow leaves the rank at 0 — nobody chose an order — and the arranger has
        // to fall back to id order rather than reading 0 as « premier ».
        await using var db = TestHarness.NewContext(
            nameof(A_stage_nobody_has_ordered_arranges_exactly_as_it_did_before));

        var stage = SeedThreeServiceStage(db);
        db.Allow(stage, Svc(db, Third), Svc(db, Second), Svc(db, First));
        await db.SaveChangesAsync();

        await db.Arranger().ArrangeAsync(
            TestHarness.StageId, TestHarness.CurrentYearId, null, null, null, default);

        var placement = await PlacementByGroupAsync(db);
        placement[1].Should().Be(First, "lowest service id, as before the rank existed");
        placement[3].Should().Be(Second);
        placement[5].Should().Be(Third);
    }

    [Fact]
    public async Task Authoring_an_order_writes_contiguous_ranks()
    {
        await using var db = TestHarness.NewContext(nameof(Authoring_an_order_writes_contiguous_ranks));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, First), Svc(db, Second), Svc(db, Third));
        await db.SaveChangesAsync();

        var result = await new SetAllowedServiceOrderCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new SetAllowedServiceOrderCommand(TestHarness.StageId, [Third, First, Second]), default);

        result.IsSuccess.Should().BeTrue();

        (await db.StageAllowedServices.AsNoTracking().ToListAsync())
            .ToDictionary(r => r.ServiceId, r => r.Rank)
            .Should().BeEquivalentTo(new Dictionary<int, int> { [Third] = 1, [First] = 2, [Second] = 3 });
    }

    [Fact]
    public async Task A_swap_of_two_adjacent_ranks_is_written_without_colliding()
    {
        // ⚠ IX_StageAllowedServices_Stage_Rank is unique and non-deferrable, so a 1↔2 swap in one
        // SaveChanges leaves the order of the two UPDATEs to EF, and one of the two orders violates
        // the constraint half-way through. ServiceRankWriter parks the rows on their negative ranks
        // first. The in-memory provider enforces no index, so what this pins is that the two-phase
        // write produces the right end state — the constraint itself needs Testcontainers.
        await using var db = TestHarness.NewContext(
            nameof(A_swap_of_two_adjacent_ranks_is_written_without_colliding));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, First), Svc(db, Second), Svc(db, Third));
        await db.SaveChangesAsync();

        var result = await new SetAllowedServiceOrderCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new SetAllowedServiceOrderCommand(TestHarness.StageId, [Second, First, Third]), default);

        result.IsSuccess.Should().BeTrue();

        var ranks = await db.StageAllowedServices.AsNoTracking().ToListAsync();
        ranks.Select(r => r.Rank).Should().BeEquivalentTo(new[] { 1, 2, 3 });
        ranks.Single(r => r.ServiceId == Second).Rank.Should().Be(1);
    }

    [Fact]
    public async Task Re_sending_the_order_already_on_disk_writes_nothing()
    {
        // The writer parks only the rows that actually move — a stationary row keeps a rank no mover
        // can target, since the ranking is a bijection onto 1..n. So re-sending the current order
        // moves nobody and must not rewrite eighteen rows to arrive where it started.
        await using var db = TestHarness.NewContext(nameof(Re_sending_the_order_already_on_disk_writes_nothing));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, First), Svc(db, Second), Svc(db, Third));
        await db.SaveChangesAsync();

        var result = await new SetAllowedServiceOrderCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new SetAllowedServiceOrderCommand(TestHarness.StageId, [First, Second, Third]), default);

        result.IsSuccess.Should().BeTrue();

        (await db.StageAllowedServices.AsNoTracking().ToListAsync())
            .ToDictionary(r => r.ServiceId, r => r.Rank)
            .Should().BeEquivalentTo(new Dictionary<int, int> { [First] = 1, [Second] = 2, [Third] = 3 });
    }

    [Fact]
    public async Task The_act_records_itself_even_though_the_write_opens_its_own_transaction()
    {
        // ⚠ The bug this pins, found by driving the real screen on 2026-09-05: the reorder wrote its
        // ranks and no STAGE_SERVICE_ORDER_SET appeared. AuditLogPipelineBehavior stages the journal
        // entry BEFORE the handler — that is what makes a refused act write nothing and a successful
        // one record itself in the same unit of work — and ExecuteAtomicallyAsync opened by clearing
        // the change tracker, which ate it. The act succeeded; its trail vanished silently, on the one
        // table whose whole purpose is to be read back later.
        await using var db = TestHarness.NewContext(
            nameof(The_act_records_itself_even_though_the_write_opens_its_own_transaction));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, First), Svc(db, Second), Svc(db, Third));
        await db.SaveChangesAsync();

        // Staged exactly as the pipeline stages it: added to the context, committed by the handler.
        db.AuditLogs.Add(PGSH.Domain.Audit.AuditLog.Record(
            "STAGE_SERVICE_ORDER_SET", "Stage", TestHarness.StageId.ToString(),
            null, null, new DateTime(2026, 9, 5, 9, 0, 0, DateTimeKind.Utc)));

        var result = await new SetAllowedServiceOrderCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new SetAllowedServiceOrderCommand(TestHarness.StageId, [Third, First, Second]), default);

        result.IsSuccess.Should().BeTrue();

        (await db.AuditLogs.AsNoTracking().ToListAsync())
            .Should().ContainSingle(e => e.Action == "STAGE_SERVICE_ORDER_SET",
                "the entry is staged before the handler, so a transaction that clears the tracker eats it");
    }

    [Fact]
    public async Task An_order_that_leaves_a_service_out_is_refused_and_writes_nothing()
    {
        // ⚠ The refusal and the store are two separate assertions: a guard ordered *after* the write
        // returns the same Result.Failure and passes a handler test that only looks at the Result.
        await using var db = TestHarness.NewContext(
            nameof(An_order_that_leaves_a_service_out_is_refused_and_writes_nothing));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, First), Svc(db, Second), Svc(db, Third));
        await db.SaveChangesAsync();

        var result = await new SetAllowedServiceOrderCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new SetAllowedServiceOrderCommand(TestHarness.StageId, [Third, First]), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Stages.ServiceOrderNotAPermutation");

        (await db.StageAllowedServices.AsNoTracking().ToListAsync())
            .ToDictionary(r => r.ServiceId, r => r.Rank)
            .Should().BeEquivalentTo(
                new Dictionary<int, int> { [First] = 1, [Second] = 2, [Third] = 3 },
                "the order on disk is untouched");
    }

    [Fact]
    public async Task An_order_decided_before_a_service_was_authorised_is_refused_by_the_write_itself()
    {
        // ⚠ The caller checks the permutation against a read taken BEFORE the transaction. A service
        // authorised in between arrives as a row the ranking says nothing about — it would keep a
        // positive rank, collide with a ranked one, and surface as a constraint violation naming an
        // index, i.e. a 500. The writer re-checks inside its own transaction and refuses in words.
        await using var db = TestHarness.NewContext(
            nameof(An_order_decided_before_a_service_was_authorised_is_refused_by_the_write_itself));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, First), Svc(db, Second), Svc(db, Third));
        await db.SaveChangesAsync();

        // The order somebody decided while the stage still held only two services.
        var result = await new ServiceRankWriter(db)
            .ApplyAsync(TestHarness.StageId, [Second, First], default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Stages.ServiceOrderIsStale");

        (await db.StageAllowedServices.AsNoTracking().ToListAsync())
            .ToDictionary(r => r.ServiceId, r => r.Rank)
            .Should().BeEquivalentTo(
                new Dictionary<int, int> { [First] = 1, [Second] = 2, [Third] = 3 },
                "a refused write leaves the order exactly as it was — never parked on negatives");
    }

    [Fact]
    public async Task A_written_order_never_leaves_a_rank_parked_on_a_negative()
    {
        // The parked state is worse than either end: every rank negative reads as « nobody chose »
        // (SortKeyOf), so the stage would silently fall back to id order on a list somebody had just
        // ordered by hand. The two saves are therefore one transaction.
        await using var db = TestHarness.NewContext(
            nameof(A_written_order_never_leaves_a_rank_parked_on_a_negative));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, First), Svc(db, Second), Svc(db, Third));
        await db.SaveChangesAsync();

        await new SetAllowedServiceOrderCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new SetAllowedServiceOrderCommand(TestHarness.StageId, [Third, Second, First]), default);

        (await db.StageAllowedServices.AsNoTracking().ToListAsync())
            .Should().OnlyContain(r => r.Rank > 0);
    }

    [Fact]
    public async Task Ordering_an_unknown_stage_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(Ordering_an_unknown_stage_is_refused));

        SeedThreeServiceStage(db);
        await db.SaveChangesAsync();

        var result = await new SetAllowedServiceOrderCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new SetAllowedServiceOrderCommand(4242, []), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Stages.NotFound");
    }

    [Fact]
    public async Task A_newly_authorised_service_is_appended_and_never_takes_the_first_position()
    {
        // It has no claim on a position somebody chose for the services already there.
        await using var db = TestHarness.NewContext(
            nameof(A_newly_authorised_service_is_appended_and_never_takes_the_first_position));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, Third), Svc(db, First));
        await db.SaveChangesAsync();

        var result = await new AddAllowedServiceCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new AddAllowedServiceCommand(TestHarness.StageId, Second), default);

        result.IsSuccess.Should().BeTrue();

        (await db.StageAllowedServices.AsNoTracking().ToListAsync())
            .ToDictionary(r => r.ServiceId, r => r.Rank)
            .Should().BeEquivalentTo(new Dictionary<int, int> { [Third] = 1, [First] = 2, [Second] = 3 });
    }

    [Fact]
    public async Task Removing_a_service_closes_the_hole_in_the_order()
    {
        // A hole would leave the number shown beside a service disagreeing with the place it holds.
        await using var db = TestHarness.NewContext(nameof(Removing_a_service_closes_the_hole_in_the_order));

        var stage = SeedThreeServiceStage(db);
        db.AllowInOrder(stage, Svc(db, First), Svc(db, Second), Svc(db, Third));
        await db.SaveChangesAsync();

        var result = await new RemoveAllowedServiceCommandHandler(db, new ServiceRankWriter(db))
            .Handle(new RemoveAllowedServiceCommand(TestHarness.StageId, Second), default);

        result.IsSuccess.Should().BeTrue();

        (await db.StageAllowedServices.AsNoTracking().ToListAsync())
            .ToDictionary(r => r.ServiceId, r => r.Rank)
            .Should().BeEquivalentTo(new Dictionary<int, int> { [First] = 1, [Third] = 2 });
    }
}
