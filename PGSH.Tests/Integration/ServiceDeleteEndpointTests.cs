using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Hospitals;
using PGSH.Domain.Stages;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// <c>DELETE services/{id}</c> à travers le vrai pipeline.
///
/// <para>⚠ <b>C'est le code HTTP qui était le défaut, et un test de handler ne le voit pas.</b> Sans
/// garde, la suppression atteignait <c>SaveChanges</c> et PostgreSQL répondait 23503 : une
/// <c>DbUpdateException</c>, donc <c>GlobalExceptionHandler</c>, donc un <b>500</b> intitulé « Server
/// failure » — et <c>errorMiddleware</c>, côté client, jette <c>detail</c> au-dessus de 500. Un refus
/// typé <c>Conflict</c> est la seule forme qui arrive à l'écran avec sa phrase. Même règle que
/// <c>ErrorStatusMappingEndpointTests</c>.</para>
///
/// <para>⚠ Le magasin reste <c>UseInMemoryDatabase</c> : aucune clé étrangère n'y est tenue, donc ce
/// fichier vérifie que la <b>garde</b> refuse, jamais que la base l'aurait fait.</para>
/// </summary>
public class ServiceDeleteEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int LevelId = 3;
    private const int StageId = 1;
    private const int HospitalId = 1;
    private const int HeldId = 10;
    private const int FreeId = 11;

    private readonly ApiFactory _factory;

    public ServiceDeleteEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Deux services : l'un autorisé par un stage, l'autre que rien ne nomme. ⚠ Le second est le
    /// témoin — une route qui refuse tout satisfait n'importe quelle assertion de refus.
    /// </summary>
    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.Levels.Add(new Level
        {
            Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.Stages.Add(new Stage
        {
            Id = StageId, Name = "Chirurgie", LevelId = LevelId,
            Coefficient = 2, DurationInDays = 44,
        });

        var hospital = new Hospital { Id = HospitalId, Name = "CHU Ibn Sina", City = "Rabat" };
        db.Hospitals.Add(hospital);

        foreach (var (id, name) in new[] { (HeldId, "Cardiologie"), (FreeId, "Rhumatologie") })
        {
            db.Services.Add(new Service
            {
                Id = id, Name = name, Description = "",
                HospitalId = hospital.Id, Hospital = hospital, Capacity = 20,
            });
        }

        db.StageAllowedServices.Add(new StageAllowedService
        {
            StageId = StageId, ServiceId = HeldId, Rank = 1,
        });
    });

    private static string Url(int serviceId) => $"/api/services/{serviceId}";

    private Task<int> ServiceCountAsync() => _factory.QueryAsync(db => db.Services.CountAsync());

    /// <summary>
    /// ⚠ <b>Le test qui mord.</b> Retirer la garde et il échoue sur le code — 500, ou 204 tant qu'aucune
    /// clé étrangère n'est tenue — puis sur la phrase, que le client jette au-dessus de 500.
    /// </summary>
    [Fact]
    public async Task A_held_service_is_refused_with_a_sentence_not_a_server_failure()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync(Url(HeldId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("detail").GetString()
            .Should().Contain("Chirurgie", "le refus nomme le stage qui retient le service");

        (await ServiceCountAsync()).Should().Be(2, "un refus ne détruit rien");
        (await _factory.QueryAsync(db => db.AuditLogs.CountAsync())).Should().Be(0,
            "le registre enregistre les actes, pas les tentatives");
    }

    /// <summary>
    /// Le témoin : rien ne retient ce service, il part — et l'entrée du registre est validée par le
    /// <c>SaveChanges</c> du handler, ce que seul un passage par le vrai pipeline vérifie.
    /// </summary>
    [Fact]
    public async Task A_free_service_is_deleted_and_the_act_is_on_the_register()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync(Url(FreeId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ServiceCountAsync()).Should().Be(1);

        var entry = await _factory.QueryAsync(db => db.AuditLogs
            .SingleOrDefaultAsync(a => a.Action == "SERVICE_DELETED"));

        entry.Should().NotBeNull();
        entry!.EntityId.Should().Be(FreeId.ToString());
        JsonDocument.Parse(entry.Metadata!).RootElement
            .GetProperty("serviceName").GetString().Should().Be("Rhumatologie");
    }

    /// <summary>Un service qui n'existe pas est un 404 — pas un 500, pas un succès muet.</summary>
    [Fact]
    public async Task An_unknown_service_is_a_not_found()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.DeleteAsync(Url(4242));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
