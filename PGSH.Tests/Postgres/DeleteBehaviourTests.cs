using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PGSH.Tests.Postgres;

/// <summary>
/// What the schema does to the children when a parent is deleted — the fact every delete guard in
/// this system is written against, and the one no other provider here can answer.
/// </summary>
/// <remarks>
/// <para>⚠ <c>UseInMemoryDatabase</c> ignores <c>OnDelete</c> entirely, so a guard removed by accident
/// leaves the suite green. SQLite enforces foreign keys but is not the schema that runs in
/// production. These tests assert the two behaviours <c>CLAUDE.md</c> singles out, because they fail
/// in opposite and equally bad ways: a <c>RESTRICT</c> reached without a guard is a <b>500</b> whose
/// only content is the name of a PostgreSQL constraint, and a <c>CASCADE</c> takes its children
/// <b>silently</b>.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class DeleteBehaviourTests(PostgresFixture postgres)
{
    /// <summary>
    /// ⚠ <c>Cohort.Stage</c> is <c>RESTRICT</c>. That is why <c>DeleteStageCommand</c> must count its
    /// cohortes <i>before</i> deleting and refuse in words — without the guard this exact exception is
    /// what the user gets, rendered as « Une erreur serveur est survenue ».
    /// </summary>
    [PostgresFact]
    public async Task Deleting_a_stage_a_cohort_still_points_at_is_refused_by_the_schema()
    {
        var database = await postgres.NewDatabaseAsync();
        var seed = await PostgresSeed.CatalogAsync(database);

        // ⚠ A second context: the first one tracks the cohorte, and EF would answer for the server.
        await using var db = database.Connect();

        db.Stages.Remove(await db.Stages.SingleAsync(s => s.Id == seed.StageId));

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "the FK is RESTRICT, so the guard in the handler is what turns this into a sentence");
    }

    /// <summary>
    /// And the control: with the cohorte gone first, the same delete goes through. Without it the
    /// assertion above would also pass on a stage that could never be deleted for some other reason.
    /// </summary>
    [PostgresFact]
    public async Task Deleting_a_stage_nothing_points_at_succeeds()
    {
        var database = await postgres.NewDatabaseAsync();
        var seed = await PostgresSeed.CatalogAsync(database);

        // ⚠ A second context: the first one tracks the cohorte, and EF would answer for the server.
        await using var db = database.Connect();

        db.Cohorts.RemoveRange(await db.Cohorts.Where(c => c.StageId == seed.StageId).ToListAsync());
        await db.SaveChangesAsync();

        db.Stages.Remove(await db.Stages.SingleAsync(s => s.Id == seed.StageId));
        await db.SaveChangesAsync();

        (await db.Stages.CountAsync(s => s.Id == seed.StageId)).Should().Be(0);
    }
}
