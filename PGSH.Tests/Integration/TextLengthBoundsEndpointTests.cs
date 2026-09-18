using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PGSH.Application.Hospitals;
using PGSH.Domain.Hospitals;
using Xunit;
using Roles = PGSH.Application.Abstractions.Authentication.Roles;

namespace PGSH.Tests.Integration;

/// <summary>
/// Une valeur trop longue pour sa colonne est-elle <b>refusée en mots</b>, ou laissée à PostgreSQL&nbsp;?
///
/// <para>⚠ <b>Un validateur plus large que sa colonne ne refuse pas : il produit un 500.</b> Les
/// quatre validateurs d'hôpital et de centre autorisaient un nom de <b>200</b> caractères contre une
/// colonne <c>varchar(100)</c>, et une ville de <b>100</b> contre <c>varchar(50)</c> ; la description,
/// l'e-mail et les trois coordonnées n'étaient bornés <b>nulle part</b>. Une valeur trop longue passait
/// donc la validation, et le serveur répondait <c>22001 string_data_right_truncation</c> — une
/// <c>DbUpdateException</c>, mappée en <b>500</b>, dont le client jette le <c>detail</c> au-dessus de
/// 500 : l'écran affiche « Une erreur serveur est survenue » et l'utilisateur n'apprend jamais qu'un
/// nom est trop long.</para>
///
/// <para>C'est la règle « une suppression interroge le schéma, ou la contrainte répond à sa place »,
/// atteinte par une longueur plutôt que par une clé étrangère. <see cref="HospitalTextLengths"/> nomme
/// les largeurs une fois ; ce qui est vérifié ici est que les validateurs s'y tiennent réellement, des
/// deux côtés de chaque borne.</para>
///
/// <para>⚠ <b>Chaque refus est apparié à son contrôle</b> — la valeur qui tient exactement dans la
/// colonne doit passer. Une route qui refuserait tout satisferait la moitié « refus » de chaque cas
/// sans rien prouver.</para>
/// </summary>
public class TextLengthBoundsEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory;

    public TextLengthBoundsEndpointTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetAsync();
        // ⚠ Le centre est créé explicitement : `SeedCatalog` n'en crée aucun, et `SeedService` ne le
        // fait qu'en passant. Sans lui la création d'hôpital répond 404 — ce qu'un contrôle qui ne
        // teste que le refus ne verrait jamais, puisque la validation précède le handler.
        await _factory.SeedAsync(db =>
        {
            db.SeedCatalog();
            db.DefaultCenter();
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Client() => _factory.CreateApiClient(null, Roles.Scolarite);

    private static string Of(int length) => new('A', length);

    private Task<HttpResponseMessage> CreateHospitalAsync(
        string? name = null, string? city = null, string? description = null,
        string? email = null, string? coordinate = null) =>
        Client().PostAsJsonAsync("/api/hospitals", new
        {
            centerId = TestHarness.DefaultCenterId,
            name = name ?? "CHU Ibn Sina",
            hospitalType = nameof(HospitalType.CHU),
            city = city ?? "Rabat",
            description,
            email,
            localizationX = coordinate,
            localizationY = (string?)null,
            localizationZ = (string?)null,
        });

    private Task<HttpResponseMessage> CreateCenterAsync(string? name = null, string? city = null) =>
        Client().PostAsJsonAsync("/api/centers", new
        {
            name = name ?? "Centre Rabat",
            centerType = nameof(CenterType.CHU),
            city = city ?? "Rabat",
            localizationX = (string?)null,
            localizationY = (string?)null,
            localizationZ = (string?)null,
        });

    [Fact]
    public async Task A_hospital_name_that_fills_the_column_is_accepted()
    {
        var response = await CreateHospitalAsync(name: Of(HospitalTextLengths.Name));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_hospital_name_past_the_column_is_refused_rather_than_crashing()
    {
        var response = await CreateHospitalAsync(name: Of(HospitalTextLengths.Name + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "le validateur autorisait 200 contre une colonne de 100, donc PostgreSQL répondait à sa "
            + "place — en 500, dont l'écran ne peut rien tirer");
    }

    [Fact]
    public async Task A_hospital_city_past_the_column_is_refused_rather_than_crashing()
    {
        var response = await CreateHospitalAsync(city: Of(HospitalTextLengths.City + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>⚠ La description n'était bornée <b>nulle part</b>, ni à la création ni à la modification.</summary>
    [Fact]
    public async Task A_hospital_description_past_the_column_is_refused_rather_than_crashing()
    {
        var response = await CreateHospitalAsync(description: Of(HospitalTextLengths.Description + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>⚠ <c>EmailAddress()</c> disait que c'était une adresse, jamais qu'elle tenait dans la colonne.</summary>
    [Fact]
    public async Task A_hospital_email_past_the_column_is_refused_rather_than_crashing()
    {
        string local = Of(HospitalTextLengths.Email);
        var response = await CreateHospitalAsync(email: $"{local}@um5.ac.ma");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// ⚠ Les coordonnées n'étaient bornées sur aucun des deux validateurs d'hôpital, et le centre en
    /// bornait deux sur trois.
    /// </summary>
    [Fact]
    public async Task A_coordinate_past_the_column_is_refused_rather_than_crashing()
    {
        var response = await CreateHospitalAsync(coordinate: Of(HospitalTextLengths.Coordinate + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_center_name_that_fills_the_column_is_accepted()
    {
        var response = await CreateCenterAsync(name: Of(HospitalTextLengths.Name));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_center_name_past_the_column_is_refused_rather_than_crashing()
    {
        var response = await CreateCenterAsync(name: Of(HospitalTextLengths.Name + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_center_city_past_the_column_is_refused_rather_than_crashing()
    {
        var response = await CreateCenterAsync(city: Of(HospitalTextLengths.City + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
