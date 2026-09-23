using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.Hospitals.Services.Chef;
using PGSH.Domain.Employees;
using Xunit;

namespace PGSH.Tests.Postgres;

/// <summary>
/// « Un service n'a qu'une tenure de chef ouverte à la fois » — une règle que <b>seul</b> PostgreSQL
/// peut faire respecter ici, et dont la violation a rendu deux services impossibles à pourvoir.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Aucun autre palier ne voit ce défaut.</b> Le fournisseur en mémoire recolle les
/// navigations depuis le change tracker, donc une collection <i>non incluse</i> y paraît pleine et
/// la tenure ouverte se clôt comme il faut — le test passe sur le code cassé. SQLite ignore les
/// index <b>filtrés</b>, donc il refuserait deux tenures du même service même correctement closes,
/// et le test échouerait sur le code juste. Seul l'index réel — <c>UNIQUE (ServiceId) WHERE
/// "EndDate" IS NULL</c> — pose la question telle qu'elle se pose en production.</para>
///
/// <para><b>L'incident, mesuré sur la base vivante le 23/09/2026.</b> Ni
/// <c>AssignChefCommandHandler</c> ni <c>RemoveChefCommandHandler</c> n'incluaient
/// <c>Service.ChefHistory</c>, que l'agrégat pourtant <i>modifie</i>. Retirer un chef mettait donc le
/// pointeur à null, ne clôturait rien, et <b>réussissait</b> : le service se retrouvait sans chef
/// mais avec une tenure ouverte. L'affectation suivante ajoutait une seconde ligne ouverte et
/// butait sur l'index — <c>23505</c>, rendu à l'écran en « Une erreur serveur est survenue ».
/// Pédiatrie1 et Pédiatrie2 étaient dans cet état.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ServiceChefTenureTests(PostgresFixture postgres)
{
    private const int ServiceId = 70;

    /// <summary>Un service, deux employés en poste de chef dans son personnel, aucun nommé encore.</summary>
    private static async Task<(Guid FirstChefId, Guid SecondChefId)> SeedAsync(PostgresDatabase database)
    {
        await using var db = database.Connect();

        db.SeedCatalog();
        var service = db.SeedService(ServiceId, "Pédiatrie1");

        var first = db.SeedChef(Guid.NewGuid(), "youssef.alaoui@pgsh.ma");
        var second = db.SeedChef(Guid.NewGuid(), "samira.benali@pgsh.ma");

        service.AddStaff(first);
        service.AddStaff(second);

        await db.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    /// <summary>
    /// ⚠ <b>Le cas qui a échoué en production.</b> Nommer, retirer, renommer — trois actes ordinaires
    /// qui laissaient le service définitivement sans chef possible.
    /// </summary>
    [PostgresFact]
    public async Task A_chef_can_be_named_again_after_one_has_been_removed()
    {
        var database = await postgres.NewDatabaseAsync();
        var (first, second) = await SeedAsync(database);

        // ⚠ Un contexte neuf par acte : c'est ce qui force la lecture à passer par l'Include plutôt
        // que par le change tracker, comme en production où chaque requête a le sien.
        await using (var db = database.Connect())
            (await new AssignChefCommandHandler(db).Handle(
                new AssignChefCommand(ServiceId, first), default)).IsSuccess.Should().BeTrue();

        await using (var db = database.Connect())
            (await new RemoveChefCommandHandler(db).Handle(
                new RemoveChefCommand(ServiceId), default)).IsSuccess.Should().BeTrue();

        await using (var db = database.Connect())
        {
            var result = await new AssignChefCommandHandler(db).Handle(
                new AssignChefCommand(ServiceId, second), default);

            result.IsSuccess.Should().BeTrue(
                "removing a chef must close the open tenure, or the unique index refuses the next one");
        }

        await using var check = database.Connect();

        var tenures = await check.Set<PGSH.Domain.Hospitals.ServiceChefAssignment>()
            .Where(h => h.ServiceId == ServiceId)
            .ToListAsync();

        tenures.Should().HaveCount(2, "one closed, one open — the history is kept, not overwritten");
        tenures.Count(h => h.EndDate is null).Should().Be(1);
        tenures.Single(h => h.EndDate is null).EmployeeId.Should().Be(second);

        (await check.Services.SingleAsync(s => s.Id == ServiceId))
            .ServiceChefId.Should().Be(second, "the pointer and the open tenure name the same person");
    }

    /// <summary>
    /// ⚠ <b>Le retrait se vérifie pour lui-même, et il le faut.</b> Le parcours complet ci-dessus ne
    /// prouve rien sur <c>RemoveChef</c> : l'affectation qui suit clôt de toute façon la tenure
    /// qu'elle trouve ouverte, donc elle <i>répare</i> l'oubli du retrait et le test passe sur le
    /// code cassé — vérifié en remettant le défaut le 23/09/2026. Ce qui distingue les deux est
    /// l'état laissé <b>immédiatement après</b> le retrait, et rien d'autre ne le regarde.
    ///
    /// <para>C'est aussi l'état exact qu'on a trouvé en base : un service sans chef, avec une tenure
    /// ouverte — dont personne ne pouvait plus sortir.</para>
    /// </summary>
    [PostgresFact]
    public async Task Removing_a_chef_closes_his_tenure_there_and_then()
    {
        var database = await postgres.NewDatabaseAsync();
        var (first, _) = await SeedAsync(database);

        await using (var db = database.Connect())
            await new AssignChefCommandHandler(db).Handle(new AssignChefCommand(ServiceId, first), default);

        await using (var db = database.Connect())
            (await new RemoveChefCommandHandler(db).Handle(
                new RemoveChefCommand(ServiceId), default)).IsSuccess.Should().BeTrue();

        await using var check = database.Connect();

        (await check.Set<PGSH.Domain.Hospitals.ServiceChefAssignment>()
            .CountAsync(h => h.ServiceId == ServiceId && h.EndDate == null))
            .Should().Be(0, "a service with no chef must not keep an open tenure");

        (await check.Services.SingleAsync(s => s.Id == ServiceId))
            .ServiceChefId.Should().BeNull("the pointer and the history must say the same thing");
    }

    /// <summary>
    /// ⚠ Le second sens du même défaut : remplacer un chef sans passer par « retirer ». L'agrégat
    /// clôt la tenure lui-même, mais seulement s'il la voit.
    /// </summary>
    [PostgresFact]
    public async Task Replacing_a_chef_directly_closes_the_tenure_it_replaces()
    {
        var database = await postgres.NewDatabaseAsync();
        var (first, second) = await SeedAsync(database);

        await using (var db = database.Connect())
            await new AssignChefCommandHandler(db).Handle(new AssignChefCommand(ServiceId, first), default);

        await using (var db = database.Connect())
        {
            var result = await new AssignChefCommandHandler(db).Handle(
                new AssignChefCommand(ServiceId, second), default);

            result.IsSuccess.Should().BeTrue();
        }

        await using var check = database.Connect();

        (await check.Set<PGSH.Domain.Hospitals.ServiceChefAssignment>()
            .CountAsync(h => h.ServiceId == ServiceId && h.EndDate == null))
            .Should().Be(1);
    }

    /// <summary>
    /// Le contrôle : nommer deux fois le même n'ouvre pas une seconde tenure. Sans lui, un handler
    /// qui clôturerait tout et rouvrirait à chaque appel satisferait les deux tests ci-dessus tout en
    /// hachant l'historique en tranches d'un jour.
    /// </summary>
    [PostgresFact]
    public async Task Naming_the_same_chef_twice_changes_nothing()
    {
        var database = await postgres.NewDatabaseAsync();
        var (first, _) = await SeedAsync(database);

        await using (var db = database.Connect())
            await new AssignChefCommandHandler(db).Handle(new AssignChefCommand(ServiceId, first), default);

        await using (var db = database.Connect())
            (await new AssignChefCommandHandler(db).Handle(
                new AssignChefCommand(ServiceId, first), default)).IsSuccess.Should().BeTrue();

        await using var check = database.Connect();

        (await check.Set<PGSH.Domain.Hospitals.ServiceChefAssignment>()
            .CountAsync(h => h.ServiceId == ServiceId))
            .Should().Be(1, "the same chef named again is not a new tenure");
    }
}
