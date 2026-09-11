using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using PGSH.Domain.Registrations;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;
using Roles = PGSH.Application.Abstractions.Authentication.Roles;

namespace PGSH.Tests.Integration;

/// <summary>
/// « Par taille » ou « par nombre » — la règle qui dit qu'une coupe se demande dans <b>une</b> unité.
///
/// <para>⚠ <b>Cette règle vit dans le validateur, donc elle n'existe que dans le pipeline.</b> Un test
/// de handler passe une commande malformée directement au handler et ne voit rien : c'est la leçon de
/// <c>StageRotationModePersistenceTests</c>, resté vert pendant que le catalogue entier était
/// insauvegardable. Une règle de validateur est couverte ici ou elle ne l'est pas.</para>
/// </summary>
public class AutoArrangeUnitEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int YearId = 1;
    private const int PromotionId = 3;
    private const string Route = "/api/groups/auto-arrange";

    private readonly ApiFactory _factory;

    public AutoArrangeUnitEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await _factory.SeedAsync(db =>
        {
            db.AcademicYears.Add(new AcademicYear
            {
                Id = YearId, Label = "2026-2027", IsCurrent = true,
                StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 8, 31),
            });

            db.Levels.Add(new Level
            {
                Id = PromotionId, Label = "Quatrième Année Pharmacie", Year = 4,
                AcademicProgram = AcademicProgram.Pharmacie,
            });

            foreach (int i in Enumerable.Range(1, 23))
                db.SeedRegistration($"E{i:D3}", "Test", academicYearId: YearId, levelId: PromotionId);
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Client() => _factory.CreateApiClient(null, Roles.Scolarite);

    private Task<HttpResponseMessage> ArrangeAsync(object body) =>
        Client().PostAsJsonAsync(Route, body);

    private Task<int> RosterCountAsync() =>
        _factory.QueryAsync(db => db.AcademicGroups.CountAsync(g => g.AcademicYearId == YearId));

    /// <summary>Le témoin : sans lui, une route qui refuse tout satisferait les deux refus ci-dessous.</summary>
    [Fact]
    public async Task One_unit_is_accepted_and_cuts_the_promotion()
    {
        var bySize = await ArrangeAsync(new { academicYearId = YearId, levelId = PromotionId, groupSize = 10 });
        bySize.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RosterCountAsync()).Should().Be(3, "⌈23 ÷ 10⌉");
    }

    /// <summary>L'autre unité, sur la même route et par le même chemin.</summary>
    [Fact]
    public async Task A_count_binds_from_the_body_and_is_honoured()
    {
        var byCount = await ArrangeAsync(new { academicYearId = YearId, levelId = PromotionId, groupCount = 5 });
        byCount.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RosterCountAsync()).Should().Be(5);

        var sizes = await _factory.QueryAsync(db => db.Registrations
            .Where(r => r.AcademicGroupId != null)
            .GroupBy(r => r.AcademicGroupId!.Value)
            .Select(g => g.Count())
            .ToListAsync());

        sizes.Sum().Should().Be(23);
        (sizes.Max() - sizes.Min()).Should().Be(1, "23 en 5 groupes : trois de 5 et deux de 4");
    }

    /// <summary>
    /// ⚠ <b>Les deux unités à la fois : refusé.</b> Le handler devrait choisir, et l'opérateur ne
    /// saurait pas laquelle — sur l'acte qui décide de la forme d'une promotion entière.
    /// </summary>
    [Fact]
    public async Task Naming_both_units_is_refused_and_writes_nothing()
    {
        var both = await ArrangeAsync(
            new { academicYearId = YearId, levelId = PromotionId, groupSize = 10, groupCount = 5 });

        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RosterCountAsync()).Should().Be(0, "un acte refusé n'écrit rien");
    }

    /// <summary>
    /// ⚠ <b>Ni l'une ni l'autre : refusé aussi.</b> Un défaut silencieux ici — « 20 » par exemple —
    /// découperait une promotion que personne n'a dimensionnée.
    /// </summary>
    [Fact]
    public async Task Naming_no_unit_at_all_is_refused()
    {
        var neither = await ArrangeAsync(new { academicYearId = YearId, levelId = PromotionId });

        neither.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RosterCountAsync()).Should().Be(0);
    }

    /// <summary>
    /// Plus de groupes que d'inscriptions : refusé, et le refus **nomme les deux nombres** — sinon
    /// l'opérateur repart deviner lequel des deux il a mal lu.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Le statut est asserté, et c'est le cœur du test.</b> <c>ErrorType.Problem</c> était le
    /// seul membre que <c>CustomResults.GetStatusCode</c> ne nommait pas : il tombait sur le bras
    /// <c>_</c> et ce refus revenait en <b>500</b>. Sa phrase voyageait bien dans <c>detail</c>, mais
    /// le client affiche « Une erreur serveur est survenue » sur un 5xx — donc un refus que le
    /// serveur avait pris la peine d'expliquer arrivait à l'écran comme un plantage. Mesuré le
    /// 11/09/2026 en pilotant la coupe sur la base réelle, et vrai des <b>18</b> refus construits avec
    /// <c>Error.Problem</c> : sauvegarde indisponible, « aucun texte ne gouverne cette promotion »…
    /// </remarks>
    [Fact]
    public async Task More_groups_than_students_is_refused_with_both_numbers()
    {
        var tooMany = await ArrangeAsync(
            new { academicYearId = YearId, levelId = PromotionId, groupCount = 400 });

        tooMany.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "un refus métier n'est pas une panne — en 5xx le client masque la phrase et n'affiche "
            + "que « Une erreur serveur est survenue »");

        var body = await tooMany.Content.ReadAsStringAsync();
        body.Should().Contain("400").And.Contain("23");

        (await RosterCountAsync()).Should().Be(0);
    }
}
