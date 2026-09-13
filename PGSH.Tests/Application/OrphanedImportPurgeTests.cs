using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Stages.InternshipAssignments.Sheet;
using PGSH.Application.Stages.InternshipAssignments.Sheet.Reversal;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// An import survives its reversal, but not its subjects.
///
/// <para><c>AffectationImport</c> is deliberately kept after an undo: « cet étudiant a-t-il été planifié
/// par un fichier, puis dé-planifié ? » is a question the dossier must answer, and a row that erases
/// itself answers « il ne s'est rien passé ». ⚠ That reasoning needs a student to ask it about — when
/// every registration an import names has been deleted, the row documents nothing and simply
/// accumulates.</para>
///
/// <para>⚠ <b>The predicate is « all », never « any », and the control below is the point.</b> An import
/// still naming one surviving registration documents a real act on a real student; tidying it away to
/// clean up the others would lose exactly what the record exists for.</para>
/// </summary>
public class OrphanedImportPurgeTests
{
    private const int CardioId = 1;

    private static readonly DateOnly Start = new(2025, 10, 1);
    private static readonly DateOnly End = new(2025, 10, 31);

    /// <summary>An import naming <paramref name="students"/>, of which the first is then deleted.</summary>
    private static async Task<Guid> SeedImportAsync(
        ApplicationDbContext db, int students, int deleteFirst)
    {
        var stage = db.SeedCatalog();
        db.SeedService(CardioId, "Cardiologie");
        var group = db.SeedGroup(groupId: 10, groupNumber: 10);
        var cohort = db.SeedCohortFor(stage, group, cohortId: 500);

        var registrations = new List<Registration>();
        for (int i = 0; i < students; i++)
        {
            var r = db.SeedRegistration($"E{i}", $"N{i}", group);
            db.SeedAssignment(r, cohort);
            registrations.Add(r);
        }
        await db.SaveChangesAsync();

        var journal = AffectationImport.Record(
            TestHarness.CurrentYearId, TestHarness.LevelId, "canevas.xlsx", DateTime.UtcNow, null);

        foreach (var r in registrations)
            journal.Wrote(r.Id, TestHarness.StageId, Guid.NewGuid(),
                AffectationImportOutcome.Created, writtenPeriods: 1, []);

        db.AffectationImports.Add(journal);
        await db.SaveChangesAsync();

        // Delete the registrations themselves — the students are gone, the import stays.
        for (int i = 0; i < deleteFirst; i++)
            db.Registrations.Remove(registrations[i]);
        await db.SaveChangesAsync();

        return journal.Id;
    }

    [Fact]
    public async Task An_import_whose_students_are_all_gone_is_listed_as_orphaned()
    {
        await using var db = TestHarness.NewContext(nameof(An_import_whose_students_are_all_gone_is_listed_as_orphaned));
        await SeedImportAsync(db, students: 2, deleteFirst: 2);

        var listed = await db.OrphanedImportsHandler().Handle(
            new GetOrphanedAffectationImportsQuery(TestHarness.LevelId), default);

        listed.IsSuccess.Should().BeTrue();
        listed.Value.Should().ContainSingle().Which.Should().Match<AffectationImportSummary>(
            i => i.FileName == "canevas.xlsx" && !i.CanBeReversed);
    }

    /// <summary>
    /// ⚠ The control, and the reason the predicate is « all ». One surviving registration keeps the
    /// whole record — it still documents a real act on a real student.
    /// </summary>
    [Fact]
    public async Task An_import_that_still_names_one_surviving_student_is_not_orphaned()
    {
        await using var db = TestHarness.NewContext(nameof(An_import_that_still_names_one_surviving_student_is_not_orphaned));
        await SeedImportAsync(db, students: 2, deleteFirst: 1);

        var listed = await db.OrphanedImportsHandler().Handle(
            new GetOrphanedAffectationImportsQuery(TestHarness.LevelId), default);

        listed.Value.Should().BeEmpty("half its subjects are still there, so it still documents something");

        var (purge, _) = db.PurgeImportsHandler();
        var purged = await purge.Handle(
            new PurgeOrphanedAffectationImportsCommand(0, TestHarness.LevelId), default);

        purged.IsSuccess.Should().BeTrue();
        purged.Value.Should().Be(0);
        (await db.AffectationImports.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Purging_removes_the_import_and_its_entries()
    {
        await using var db = TestHarness.NewContext(nameof(Purging_removes_the_import_and_its_entries));
        await SeedImportAsync(db, students: 2, deleteFirst: 2);

        (await db.AffectationImportEntries.CountAsync()).Should().Be(2);

        var (purge, trail) = db.PurgeImportsHandler();
        var purged = await purge.Handle(
            new PurgeOrphanedAffectationImportsCommand(1, TestHarness.LevelId), default);

        purged.IsSuccess.Should().BeTrue();
        purged.Value.Should().Be(1);

        (await db.AffectationImports.CountAsync()).Should().Be(0);
        (await db.AffectationImportEntries.CountAsync()).Should().Be(0, "the entries go by cascade");

        trail.Fields["importsPurged"].Should().Be(1);
        trail.Fields["affectationsDocumented"].Should().Be(2);
    }

    [Fact]
    public async Task A_stale_count_refuses_and_deletes_nothing()
    {
        await using var db = TestHarness.NewContext(nameof(A_stale_count_refuses_and_deletes_nothing));
        await SeedImportAsync(db, students: 2, deleteFirst: 2);

        var (purge, _) = db.PurgeImportsHandler();
        var refused = await purge.Handle(
            new PurgeOrphanedAffectationImportsCommand(7, TestHarness.LevelId), default);

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("AffectationImportReversal.PurgeCountMismatch");
        (await db.AffectationImports.CountAsync()).Should().Be(1, "a refusal deletes nothing");
    }

    [Fact]
    public async Task A_caller_who_is_not_scolarite_is_refused()
    {
        await using var db = TestHarness.NewContext(nameof(A_caller_who_is_not_scolarite_is_refused));
        await SeedImportAsync(db, students: 1, deleteFirst: 1);

        var (purge, _) = db.PurgeImportsHandler(db.StrangerAuthorizer());
        var refused = await purge.Handle(
            new PurgeOrphanedAffectationImportsCommand(1, TestHarness.LevelId), default);

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("AffectationSheet.NotAllowed");
        (await db.AffectationImports.CountAsync()).Should().Be(1);
    }
}
