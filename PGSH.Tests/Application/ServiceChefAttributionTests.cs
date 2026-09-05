using FluentAssertions;
using PGSH.Application.Hospitals.Chefs;
using PGSH.Application.Hospitals.Services.GetById;
using PGSH.Application.Hospitals.Services.GetMany;
using PGSH.Domain.Employees;
using PGSH.Domain.Hospitals;
using PGSH.Infrastructure.Database;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// Who the <b>service page</b> names as a service's chef — and it is the same answer the two
/// documents print, resolved by <see cref="ServiceChefDirectory"/> rather than re-ranked per screen.
///
/// <para>⚠ <b>The case that forced this, measured on the live base 2026-09-03.</b> Pédiatrie1 and
/// Pédiatrie2 carry the base's only two <c>ServiceChefAssignment</c> rows — both <em>Youssef
/// Alaoui</em>, open since 29/08/2026 — while <c>Services.ServiceChefId</c> is null on all 148
/// services. The export resolved the tenure and printed « Youssef Alaoui »; the page read the
/// sitting FK, fell through to the import note, and headlined « Pr.N.Elhafidi », filing the open
/// tenure under « Historique ». Both were faithful to their own ranking of the same three sources,
/// and nothing on either screen said the other existed.</para>
///
/// <para>Same class as <c>ServicePeriodResponse.State</c>: one rule, two sides of a network
/// boundary, so the server sends the resolved answer.</para>
/// </summary>
public class ServiceChefAttributionTests
{
    private const int ServiceId = 45;

    private static GetServiceByIdQueryHandler Handler(ApplicationDbContext db) =>
        new(db, new ServiceChefProvider(db));

    private static void LinkChef(ApplicationDbContext db, Service service, DateOnly? end = null)
    {
        var chef = new Employee
        {
            Id = Guid.NewGuid(), FirstName = "Youssef", LastName = "Alaoui",
            Position = Position.ServiceChef,
        };
        db.Users.Add(chef);

        // No Id on the tenure: pre-setting a store-generated key on a child of an already-tracked
        // parent makes EF classify it Modified and UPDATE a row that was never inserted.
        service.ChefHistory.Add(new ServiceChefAssignment
        {
            ServiceId = service.Id, EmployeeId = chef.Id, Employee = chef,
            StartDate = new DateOnly(2026, 8, 29), EndDate = end,
        });
    }

    /// <summary>
    /// The reported case, end to end: the open tenure is passed over, the note is what the page
    /// names, and — the sentence the page owed and did not have — it is told that somebody
    /// <em>is</em> linked.
    /// </summary>
    [Fact]
    public async Task An_open_tenure_is_not_the_printed_name_and_the_page_is_told_why()
    {
        await using var db = TestHarness.NewContext("chef-attribution-withheld");
        db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Pédiatrie1");
        service.Description = ServiceChefSourceNote.Format("Pr.N.Elhafidi");
        LinkChef(db, service);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetServiceByIdQuery(ServiceId), default);

        result.IsSuccess.Should().BeTrue();
        var chef = result.Value.ChefAttribution;

        chef.Name.Should().Be("Pr.N.Elhafidi",
            "the page must name whoever the export names — the two disagreeing is the defect");
        chef.FromSourceNote.Should().BeTrue();
        chef.LinkedChefWithheld.Should().BeTrue(
            "a tenure marked « en cours » under a headline naming somebody else, unexplained, is "
            + "the confusion this change removes rather than relocates");
    }

    /// <summary>
    /// ⚠ The half a caller could get silently wrong: <c>LinkedChefWithheld</c> reads the tenure
    /// trail, so a provider that skipped loading it under the narrowed policy would answer
    /// <c>false</c> on exactly the two services in the base that need <c>true</c> — an optimisation
    /// that erases its own subject. The policy narrows what a document may <em>name</em>, never what
    /// the directory <em>knows</em>.
    /// </summary>
    [Fact]
    public async Task The_trail_is_loaded_even_under_the_policy_that_will_not_print_it()
    {
        await using var db = TestHarness.NewContext("chef-attribution-trail-loaded");
        db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Pédiatrie2");
        service.Description = ServiceChefSourceNote.Format("Pr.A.Mdaghri Alaoui");
        LinkChef(db, service);
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(new GetServiceByIdQuery(ServiceId), default);

        result.Value.ChefHistory.Should().ContainSingle()
            .Which.LastName.Should().Be("Alaoui", "the history section is what the sentence points at");
        result.Value.ChefAttribution.LinkedChefWithheld.Should().BeTrue();
    }

    /// <summary>
    /// « Personne » and « quelqu'un que rien n'imprime » call for opposite acts — designate a chef,
    /// versus wait for the policy — so they cannot share one flag. This is the ordinary service:
    /// 140 of 148 carry a note and no link at all.
    /// </summary>
    [Fact]
    public async Task A_note_with_nobody_linked_is_not_reported_as_withheld()
    {
        await using var db = TestHarness.NewContext("chef-attribution-note-only");
        db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Chirurgie B");
        service.Description = ServiceChefSourceNote.Format("Pr.A.Settaf");
        await db.SaveChangesAsync();

        var chef = (await Handler(db).Handle(new GetServiceByIdQuery(ServiceId), default))
            .Value.ChefAttribution;

        chef.Name.Should().Be("Pr.A.Settaf");
        chef.FromSourceNote.Should().BeTrue();
        chef.LinkedChefWithheld.Should().BeFalse(
            "« désignez un chef » is the right advice here and the wrong advice on a linked service");
    }

    /// <summary>
    /// The cost of the policy, as the page has to state it: a service named <em>only</em> by an
    /// affectation names nobody, and that blank is not « aucun chef désigné ».
    /// </summary>
    [Fact]
    public async Task A_service_named_only_by_a_link_prints_no_name_but_is_not_reported_as_empty()
    {
        await using var db = TestHarness.NewContext("chef-attribution-link-only");
        db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Pédiatrie1");
        LinkChef(db, service);
        await db.SaveChangesAsync();

        var chef = (await Handler(db).Handle(new GetServiceByIdQuery(ServiceId), default))
            .Value.ChefAttribution;

        chef.Name.Should().BeNull();
        chef.FromSourceNote.Should().BeFalse("nothing was printed, so nothing has a source");
        chef.LinkedChefWithheld.Should().BeTrue();
    }

    [Fact]
    public async Task A_service_with_neither_a_link_nor_a_note_names_nobody_and_withholds_nothing()
    {
        await using var db = TestHarness.NewContext("chef-attribution-nothing");
        db.SeedCatalog();
        db.SeedService(ServiceId, "Pédiatrie");
        await db.SaveChangesAsync();

        var chef = (await Handler(db).Handle(new GetServiceByIdQuery(ServiceId), default))
            .Value.ChefAttribution;

        chef.Name.Should().BeNull();
        chef.LinkedChefWithheld.Should().BeFalse();
    }

    /// <summary>
    /// ⚠ A <b>closed</b> tenure is not a withheld chef: the order falling through to the note
    /// because nobody currently leads the service is the rule working, and telling the page a name
    /// is being held back would send somebody looking for one that does not exist.
    /// </summary>
    [Fact]
    public async Task A_tenure_that_has_ended_is_not_a_withheld_chef()
    {
        await using var db = TestHarness.NewContext("chef-attribution-closed-tenure");
        db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Pédiatrie1");
        service.Description = ServiceChefSourceNote.Format("Pr.N.Elhafidi");
        LinkChef(db, service, end: new DateOnly(2026, 8, 30));
        await db.SaveChangesAsync();

        var chef = (await Handler(db).Handle(new GetServiceByIdQuery(ServiceId), default))
            .Value.ChefAttribution;

        chef.Name.Should().Be("Pr.N.Elhafidi");
        chef.LinkedChefWithheld.Should().BeFalse();
    }

    // ── The services list ─────────────────────────────────────────────────────
    // Same question, a second screen. The list bound its « Chef de service » column to
    // Service.ServiceChefId, which is null on all 148 services of the base — so the column read
    // « — » on every row while the fiche, the répartition and the export all named somebody for 140
    // of them. The fix is not a second ranking here: it is the same directory, over the page's ids.

    private static GetServicesQueryHandler ListHandler(ApplicationDbContext db) =>
        new(db, new ServiceChefProvider(db));

    /// <summary>
    /// ⚠ <b>The assertion that prevents the drift is the equivalence, not the value.</b> A list
    /// naming « Pr.N.Elhafidi » and a fiche naming « Pr.N.Elhafidi » could still be two rules that
    /// happen to agree on this row; asserting the row <em>equals</em> the fiche's answer is what
    /// fails the day one of them starts ranking the sources itself again.
    /// </summary>
    [Fact]
    public async Task The_services_list_names_exactly_what_the_fiche_names()
    {
        await using var db = TestHarness.NewContext("chef-list-matches-fiche");
        db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Pédiatrie1");
        service.Description = ServiceChefSourceNote.Format("Pr.N.Elhafidi");
        LinkChef(db, service);
        await db.SaveChangesAsync();

        var row = (await ListHandler(db).Handle(new GetServicesQuery(PageSize: 50), default))
            .Value.Items.Single();
        var fiche = (await Handler(db).Handle(new GetServiceByIdQuery(ServiceId), default))
            .Value.ChefAttribution;

        row.ChefAttribution.Should().Be(fiche);
        row.ChefAttribution.Name.Should().Be("Pr.N.Elhafidi");
        row.ServiceChefName.Should().BeNull(
            "the link is null here — which is the state of all 148 services, and exactly why a "
            + "column bound to it printed « — » on every row");
    }

    /// <summary>
    /// Every row of the page, not the first: the directory is built over the ids the page returned,
    /// so a caller that resolved one service and reused the answer would show here.
    /// </summary>
    [Fact]
    public async Task Every_row_of_the_page_carries_its_own_answer()
    {
        await using var db = TestHarness.NewContext("chef-list-per-row");
        db.SeedCatalog();

        var named = db.SeedService(ServiceId, "Alpha");
        named.Description = ServiceChefSourceNote.Format("Pr.A.Settaf");

        var linkedOnly = db.SeedService(ServiceId + 1, "Beta");
        LinkChef(db, linkedOnly);

        db.SeedService(ServiceId + 2, "Gamma");
        await db.SaveChangesAsync();

        var rows = (await ListHandler(db).Handle(new GetServicesQuery(PageSize: 50), default))
            .Value.Items.ToDictionary(r => r.Name, r => r.ChefAttribution);

        rows["Alpha"].Name.Should().Be("Pr.A.Settaf");
        rows["Alpha"].FromSourceNote.Should().BeTrue();
        rows["Alpha"].LinkedChefWithheld.Should().BeFalse();

        rows["Beta"].Name.Should().BeNull("the policy holds the link back and there is no note");
        rows["Beta"].LinkedChefWithheld.Should().BeTrue(
            "« personne » and « quelqu'un que rien n'imprime » are different sentences on a row too");

        rows["Gamma"].Name.Should().BeNull();
        rows["Gamma"].LinkedChefWithheld.Should().BeFalse();
    }

    /// <summary>
    /// ⚠ The directory is built over <b>the page</b>, so a service the paging left out must not be
    /// resolved — and, more importantly, a service the page <em>did</em> return must be, whichever
    /// page it lands on. A build keyed on the filtered query instead would grow with the catalogue
    /// for rows nobody is looking at.
    /// </summary>
    [Fact]
    public async Task A_row_on_the_second_page_is_resolved_like_any_other()
    {
        await using var db = TestHarness.NewContext("chef-list-second-page");
        db.SeedCatalog();

        var first = db.SeedService(ServiceId, "Alpha");
        first.Description = ServiceChefSourceNote.Format("Pr.A.Settaf");

        var second = db.SeedService(ServiceId + 1, "Beta");
        second.Description = ServiceChefSourceNote.Format("Pr.N.Elhafidi");
        await db.SaveChangesAsync();

        var page = await ListHandler(db).Handle(
            new GetServicesQuery(PageNumber: 2, PageSize: 1), default);

        page.Value.Items.Single().Name.Should().Be("Beta", "ordered by name");
        page.Value.Items.Single().ChefAttribution.Name.Should().Be("Pr.N.Elhafidi");
    }
}
