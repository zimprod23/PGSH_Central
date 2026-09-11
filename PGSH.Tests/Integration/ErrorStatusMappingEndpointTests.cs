using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Domain.Registrations;
using PGSH.Domain.Stages;
using Xunit;
using AcademicProgram = PGSH.Domain.Common.Utils.AcademicProgram;
using Level = PGSH.Domain.Common.Utils.Level;

namespace PGSH.Tests.Integration;

/// <summary>
/// Un refus métier atteint l'écran avec sa phrase, ou il n'atteint rien.
///
/// <para>⚠ <b>Le défaut.</b> <c>ErrorType.Problem</c> était le seul membre que
/// <c>CustomResults.GetStatusCode</c> ne nommait pas : il tombait sur le bras <c>_</c> et sortait en
/// <b>500</b>. Or <c>errorMiddleware</c>, côté client, <i>jette</i> <c>detail</c> au-dessus de 500 et
/// affiche « Une erreur serveur est survenue. Réessayez plus tard. » — donc treize refus rédigés
/// avec soin n'arrivaient jamais à personne. Le pire de tous est celui vérifié ici.</para>
///
/// <para>⚠ <b>Pourquoi celui-là.</b> <c>AcademicYearResolver</c> est le chemin de <b>tout</b> handler
/// qui omet l'année — « omise » veut dire « celle en cours ». Une base sans année courante, qui est
/// très exactement ce qu'une désignation interrompue laissait derrière elle, ne faisait donc pas
/// échouer un écran : elle les faisait <i>tous</i> répondre 500, sans que rien ne dise quoi faire.
/// C'est un conflit entre la demande et l'état de la base, pas une panne du serveur.</para>
///
/// <para>⚠ <b>Invisible à un test de handler</b>, qui lit un <c>Result.Failure</c> et n'a pas de code
/// HTTP à regarder. La correspondance vit dans <c>CustomResults</c>, que seul un passage par le vrai
/// pipeline traverse.</para>
/// </summary>
public class ErrorStatusMappingEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const int LevelId = 3;
    private const int StageId = 1;
    private const int YearId  = 1;

    private readonly ApiFactory _factory;

    public ErrorStatusMappingEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAsync() => await _factory.SeedAsync(db =>
    {
        db.AcademicYears.Add(new AcademicYear
        {
            Id = YearId, Label = "2026-2027",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 7, 31),
            IsCurrent = true,
        });

        db.Levels.Add(new Level
        {
            Id = LevelId, Label = "Troisième Année Médecine", Year = 3,
            AcademicProgram = AcademicProgram.Medecine,
        });

        db.Stages.Add(new Stage
        {
            Id = StageId, Name = "Cardiologie", LevelId = LevelId,
            Coefficient = 1, DurationInDays = 15,
            RotationMode = StageRotationMode.SingleService,
        });
    });

    /// <summary>Ce que la désignation laisse si elle est interrompue entre ses deux écritures.</summary>
    private Task NoYearIsCurrentAsync() => _factory.SeedAsync(db =>
    {
        foreach (var year in db.AcademicYears.ToList())
            year.Relinquish();
    });

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// ⚠ <b>Le test qui mord.</b> Remettre <c>Error.Problem</c> sur <c>NoCurrentAcademicYear</c> et il
    /// échoue deux fois : sur le code, et sur la phrase — que le client jette au-dessus de 500.
    /// </summary>
    [Fact]
    public async Task A_base_with_no_current_year_refuses_by_conflict_and_says_what_to_do()
    {
        await NoYearIsCurrentAsync();
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/stages/{StageId}/schedule");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "aucune année courante est un conflit entre la demande et l'état, pas une panne du serveur");

        var problem = await ProblemAsync(response);

        problem.GetProperty("title").GetString().Should().Be("AcademicYears.NoCurrent");
        problem.GetProperty("detail").GetString().Should()
            .Contain("sélectionnez une année",
                "c'est la seule phrase qui dit quoi faire, et un 500 la remplace par « Une erreur serveur est survenue »");
    }

    /// <summary>
    /// Le témoin. Une route qui refuserait tout satisferait l'assertion ci-dessus sans rien prouver :
    /// la même lecture, l'année nommée, doit aboutir.
    /// </summary>
    [Fact]
    public async Task The_same_read_succeeds_when_the_year_is_named()
    {
        await NoYearIsCurrentAsync();
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/stages/{StageId}/schedule?academicYearId={YearId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "une année explicite n'a jamais eu besoin de la résolution");
    }

    /// <summary>
    /// Le second témoin, dans l'autre sens : la même lecture aboutit aussi dès qu'une année est
    /// courante. Sans lui, le refus ci-dessus pourrait tenir à n'importe quoi d'autre dans la requête.
    /// </summary>
    [Fact]
    public async Task The_same_read_succeeds_when_a_year_is_current()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.GetAsync($"/api/stages/{StageId}/schedule");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
