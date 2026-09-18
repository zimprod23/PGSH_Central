using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Common.Utils;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using PGSH.Domain.Students;
using Xunit;

namespace PGSH.Tests.Postgres;

/// <summary>
/// The invariants the <b>database</b> enforces, asserted against the database that enforces them.
/// </summary>
/// <remarks>
/// <para>Both indexes here are <c>UNIQUE</c> with a <c>WHERE</c> clause, and a partial index is
/// precisely what none of this repository's other three providers can express: the in-memory provider
/// ignores unique indexes altogether, SQLite has no filtered indexes, and
/// <c>SqlTranslationTests</c> never opens a connection. Until now these two rules were enforced in
/// production and asserted nowhere.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class SchemaInvariantTests(PostgresFixture postgres)
{
    /// <summary>
    /// ⚠ « L'année en cours » is a singleton, and <c>IX_AcademicYear_IsCurrent</c> is what makes it
    /// one. <c>AcademicYearResolver</c> takes the <i>first</i> row flagged current and every handler
    /// that omits a year gets it, so two rows flagged at once means two screens quietly disagreeing
    /// about which promotion they show, with nothing on either to say so.
    /// </summary>
    [PostgresFact]
    public async Task Two_current_academic_years_cannot_coexist()
    {
        await using var db = (await postgres.NewDatabaseAsync()).Connect();

        db.AcademicYears.Add(Year(1, "2025-2026", isCurrent: true));
        await db.SaveChangesAsync();

        db.AcademicYears.Add(Year(2, "2026-2027", isCurrent: true));

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "the filtered unique index is the invariant — CreateAcademicYear demoting the others is "
            + "one write path guarding it, not the guarantee");
    }

    /// <summary>
    /// ⚠ <b>Why <c>CurrentYearDesignation</c> saves the demotion first.</b> Postgres checks a unique
    /// index at the end of each <i>statement</i>, so « promote, then demote » is not a transient state
    /// EF can order its way out of — it is a constraint violation at the first statement. That class
    /// says so in words and its own tests cannot show it, because the in-memory provider ignores the
    /// index entirely. This is the assertion that was missing.
    /// </summary>
    [PostgresFact]
    public async Task Promoting_before_demoting_is_refused_by_the_server()
    {
        await using var db = (await postgres.NewDatabaseAsync()).Connect();

        db.AcademicYears.Add(Year(1, "2025-2026", isCurrent: true));
        db.AcademicYears.Add(Year(2, "2026-2027", isCurrent: false));
        await db.SaveChangesAsync();

        var incoming = await db.AcademicYears.SingleAsync(y => y.Id == 2);
        incoming.MakeCurrent().IsSuccess.Should().BeTrue();

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "the sitting year is still flagged, so this statement leaves two rows current");
    }

    /// <summary>And the order the helper actually uses, on the server that imposes it.</summary>
    [PostgresFact]
    public async Task Demoting_before_promoting_moves_the_current_year()
    {
        await using var db = (await postgres.NewDatabaseAsync()).Connect();

        db.AcademicYears.Add(Year(1, "2025-2026", isCurrent: true));
        db.AcademicYears.Add(Year(2, "2026-2027", isCurrent: false));
        await db.SaveChangesAsync();

        foreach (var sitting in await db.AcademicYears.Where(y => y.IsCurrent && y.Id != 2).ToListAsync())
            sitting.Relinquish();
        await db.SaveChangesAsync();

        var incoming = await db.AcademicYears.SingleAsync(y => y.Id == 2);
        incoming.MakeCurrent().IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        var current = await db.AcademicYears.Where(y => y.IsCurrent).Select(y => y.Label).ToListAsync();
        current.Should().ContainSingle().Which.Should().Be("2026-2027");
    }

    /// <summary>
    /// ⚠ <b>46% of the roll carries no CNE</b> — the import manufactured placeholders for 4 693 of
    /// 10 203 students and they have since been cleared — so « many students share a null code » is
    /// the ordinary state of this table, not an edge case. <c>IX_Student_CNE</c> is filtered
    /// <c>WHERE "CNE" IS NOT NULL</c> so that it stays a uniqueness rule about codes people actually
    /// hold.
    /// </summary>
    [PostgresFact]
    public async Task Students_without_a_CNE_do_not_collide()
    {
        await using var db = (await postgres.NewDatabaseAsync()).Connect();

        db.Users.Add(Student("Amina", "Benali", cne: null, appogee: "AP0001"));
        db.Users.Add(Student("Youssef", "Idrissi", cne: null, appogee: "AP0002"));

        var act = () => db.SaveChangesAsync();

        await act.Should().NotThrowAsync(
            "the unique index is filtered to the rows that carry a code, and half the roll does not");
    }

    /// <summary>The control: the rule still bites on a code two students really do share.</summary>
    [PostgresFact]
    public async Task Two_students_cannot_share_a_CNE()
    {
        await using var db = (await postgres.NewDatabaseAsync()).Connect();

        db.Users.Add(Student("Amina", "Benali", cne: "R130896", appogee: "AP0001"));
        await db.SaveChangesAsync();

        db.Users.Add(Student("Youssef", "Idrissi", cne: "R130896", appogee: "AP0002"));

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// Un <c>StageSlot</c> construit par <c>StageSlot.For</c> — clés en <c>private set</c>, constructeur
    /// sans paramètre privé — fait-il l'aller-retour par le <b>vrai</b> fournisseur&nbsp;?
    /// </summary>
    /// <remarks>
    /// ⚠ <b>La question que fermer un type pose, et que les 2 059 autres cas ne posaient pas.</b>
    /// <c>StageSlot</c> n'était exercé que par le fournisseur <i>in-memory</i> : aucun test ne
    /// l'écrivait ni ne le relisait sur un moteur relationnel. Or rendre des propriétés
    /// <c>private set</c> et le constructeur privé change la façon dont EF <b>matérialise</b> l'entité
    /// à la lecture — c'est de la mécanique d'EF, pas du fournisseur, mais « très probablement
    /// identique » n'est pas « vérifié », et la différence entre les deux est exactement ce que ce
    /// niveau de test existe pour trancher.
    ///
    /// <para>⚠ Il vérifie aussi l'index unique <c>IX_StageSlot_Stage_Year_Period</c>, qui est la moitié
    /// que le schéma tient : la fabrique empêche d'<em>omettre</em> l'identité, l'index empêche de la
    /// <em>dupliquer</em>. Aucun autre fournisseur d'ici ne sait exprimer la seconde.</para>
    /// </remarks>
    [PostgresFact]
    public async Task A_slot_made_through_its_factory_round_trips_and_keeps_its_identity()
    {
        var database = await postgres.NewDatabaseAsync();
        var catalog = await PostgresSeed.CatalogAsync(database);

        await using (var db = database.Connect())
        {
            var made = StageSlot.For(catalog.StageId, TestHarness.CurrentYearId, 3,
                new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 27), "P3");

            made.IsSuccess.Should().BeTrue();

            db.StageSlots.Add(made.Value);
            await db.SaveChangesAsync();
        }

        // ⚠ Relu par un second contexte : le premier rendrait l'objet que le change tracker tient déjà,
        // donc ne prouverait rien de la matérialisation.
        await using (var db = database.Connect())
        {
            var read = await db.StageSlots.AsNoTracking().SingleAsync();

            read.StageId.Should().Be(catalog.StageId);
            read.AcademicYearId.Should().Be(TestHarness.CurrentYearId,
                "c'est la clé que la fabrique existe pour exiger");
            read.PeriodNumber.Should().Be(3);
            read.Label.Should().Be("P3");
            read.StartDate.Should().Be(new DateOnly(2026, 3, 2));
        }
    }

    /// <summary>
    /// ⚠ L'autre moitié : la fabrique interdit d'omettre l'identité, l'<b>index</b> interdit de la
    /// répéter. Deux P3 pour le même (stage, année) sont le même créneau écrit deux fois.
    /// </summary>
    [PostgresFact]
    public async Task Two_slots_cannot_share_a_stage_year_and_period()
    {
        var database = await postgres.NewDatabaseAsync();
        var catalog = await PostgresSeed.CatalogAsync(database);

        await using var db = database.Connect();

        db.StageSlots.Add(StageSlot.For(catalog.StageId, TestHarness.CurrentYearId, 3,
            new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 27)).Value);
        await db.SaveChangesAsync();

        db.StageSlots.Add(StageSlot.For(catalog.StageId, TestHarness.CurrentYearId, 3,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 26)).Value);

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "IX_StageSlot_Stage_Year_Period est l'invariant ; la fabrique en garde l'autre moitié");
    }

    private static AcademicYear Year(int id, string label, bool isCurrent) => new()
    {
        Id = id,
        Label = label,
        IsCurrent = isCurrent,
        StartDate = new DateOnly(int.Parse(label[..4]), 9, 1),
        EndDate = new DateOnly(int.Parse(label[..4]) + 1, 8, 31),
    };

    private static Student Student(string firstName, string lastName, string? cne, string appogee) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = firstName,
        LastName = lastName,
        Email = $"{firstName}.{lastName}@etu.ma".ToLowerInvariant(),
        CNE = cne,
        Appogee = appogee,
        BacYear = "2022",
        AcademicProgram = AcademicProgram.Medecine,
    };
}
