using System.Data.Common;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PGSH.API.Infrastructure;
using PGSH.Infrastructure.Exceptions;
using Xunit;

namespace PGSH.Tests.Api;

/// <summary>
/// « La base est injoignable » et « cette requête est fautive » ne sont pas le même événement.
///
/// <para>Vécu le 13/09/2026 : WSL s'est mis à jour de lui-même, la distribution Docker s'est
/// arrêtée, PostgreSQL avec elle — et <b>chaque écran</b> a répondu <c>500 Server failure</c> avec
/// une trace de pile partant de <c>SyncUserMiddleware</c>. Lu de l'écran, une panne d'infrastructure
/// est alors indistinguable d'un défaut de l'application : l'opérateur cherche un bug là où il n'y a
/// qu'un serveur à redémarrer.</para>
///
/// <para>⚠ <b>Le risque de la correction est l'inverse</b> : ranger un vrai défaut sous « service
/// indisponible » le rendrait invisible. Les deux moitiés sont donc couvertes ici — ce qui devient
/// 503, et surtout ce qui doit <b>rester</b> un 500.</para>
/// </summary>
public class DatabaseOutageTests
{
    /// <summary>
    /// Ce que lève une connexion qui n'aboutit pas : le pilote enveloppe l'échec réseau, et se
    /// déclare transitoire.
    /// </summary>
    private sealed class ConnectionRefused(Exception inner)
        : DbException("Failed to connect to 127.0.0.1:5432", inner)
    {
        public override bool IsTransient => true;
    }

    /// <summary>
    /// Ce que lève un serveur qui a répondu — une contrainte violée, une colonne absente. Le serveur
    /// est joignable ; c'est la requête qui est fautive.
    /// </summary>
    private sealed class ServerRefusedTheStatement(string message) : DbException(message)
    {
        public override bool IsTransient => false;
    }

    [Fact]
    public void A_connection_that_never_reached_the_server_is_an_outage()
    {
        var exception = new ConnectionRefused(new SocketException(10061));

        DatabaseOutage.IsReported(exception).Should().BeTrue();
    }

    /// <summary>
    /// EF enveloppe : <c>DbUpdateException</c> n'est pas une <c>DbException</c>, et ne suivre que le
    /// niveau du dessus est la façon de manquer la seule qui parlait.
    /// </summary>
    [Fact]
    public void An_outage_is_found_however_deep_it_is_wrapped()
    {
        var wrapped = new InvalidOperationException(
            "An exception occurred while saving",
            new ConnectionRefused(new SocketException(10061)));

        DatabaseOutage.IsReported(wrapped).Should().BeTrue();

        var aggregated = new AggregateException(
            new InvalidOperationException("something else"),
            wrapped);

        DatabaseOutage.IsReported(aggregated).Should().BeTrue(
            "une AggregateException en porte plusieurs, et n'en suivre qu'une les manque");
    }

    /// <summary>
    /// ⚠ <b>Le témoin qui compte le plus.</b> Un serveur qui répond « je refuse » est un défaut, et
    /// un défaut doit continuer de se voir comme tel : rangé en 503, il deviendrait un incident
    /// d'exploitation que personne ne corrigerait jamais.
    /// </summary>
    [Fact]
    public void A_statement_the_server_refused_is_not_an_outage()
    {
        var exception = new ServerRefusedTheStatement("duplicate key value violates unique constraint");

        DatabaseOutage.IsReported(exception).Should().BeFalse();
    }

    /// <summary>
    /// ⚠ <b>Et rien en dehors de la couche données ne peut déclarer la panne.</b> Un export qui
    /// n'arrive pas à écrire son fichier lève un <c>IOException</c> ; le faire passer pour une base
    /// injoignable enverrait chercher la panne là où elle n'est pas.
    /// </summary>
    [Fact]
    public void An_io_failure_outside_the_data_layer_is_not_an_outage()
    {
        DatabaseOutage.IsReported(new IOException("le disque est plein")).Should().BeFalse();
        DatabaseOutage.IsReported(new SocketException(10061)).Should().BeFalse();
        DatabaseOutage.IsReported(new InvalidOperationException("nullref, quelque part")).Should().BeFalse();
    }

    private static async Task<(int Status, JsonElement Body)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        bool handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, default);

        handled.Should().BeTrue();

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(context.Response.Body);

        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    /// <summary>
    /// ⚠ <b>La phrase voyage dans <c>detail</c></b>, parce que c'est là que le client la lit — et
    /// que 503 est le seul ≥ 500 dont il ne masque pas le détail. Un 503 sans phrase ne vaudrait pas
    /// mieux que le 500 qu'il remplace.
    /// </summary>
    [Fact]
    public async Task An_outage_answers_503_with_a_sentence_that_says_where_to_act()
    {
        var (status, body) = await HandleAsync(new ConnectionRefused(new SocketException(10061)));

        status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        body.GetProperty("title").GetString().Should().Be("Base de données injoignable");

        string detail = body.GetProperty("detail").GetString()!;
        detail.Should().Contain("n'a rien enregistré");
        detail.Should().Contain("serveur de base de données");

        body.GetProperty("type").GetString().Should().EndWith("6.6.4", "le 503 a son propre paragraphe");
    }

    /// <summary>Le témoin : un défaut garde son 500, et ne raconte rien de son intérieur.</summary>
    [Fact]
    public async Task A_fault_still_answers_500_and_says_nothing_about_itself()
    {
        var (status, body) = await HandleAsync(new InvalidOperationException("index hors bornes"));

        status.Should().Be(StatusCodes.Status500InternalServerError);
        body.GetProperty("title").GetString().Should().Be("Server failure");
        bool saysMore = body.TryGetProperty("detail", out var detail)
                     && detail.ValueKind != JsonValueKind.Null;

        saysMore.Should().BeFalse("un défaut non identifié ne raconte rien de son intérieur");
    }

    /// <summary>Et un refus métier passe toujours par son propre code — rien de tout cela ne le touche.</summary>
    [Fact]
    public async Task A_domain_refusal_keeps_its_own_status()
    {
        var (status, body) = await HandleAsync(
            new UserProfileNotFoundException(Guid.NewGuid(), "inconnu@um5.ac.ma"));

        status.Should().Be(StatusCodes.Status403Forbidden);
        body.GetProperty("title").GetString().Should().Be("Profile Not Found");
        body.GetProperty("type").GetString().Should().EndWith("6.5.3", "le 403 a le sien aussi");
    }
}
