using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PGSH.Tests.Integration;

/// <summary>
/// Routes that were deliberately removed, and must stay removed.
/// </summary>
/// <remarks>
/// <para>⚠ <b>Hiding a route is not disabling it.</b> <c>groups/generate-schedule</c> was deprecated on
/// 14/09/2026 and taken out of Scalar and Swagger with <c>ExcludeFromDescription</c> — its only
/// clickable surface, since no screen called it. It still answered a direct HTTP call for four days,
/// and what it did on being called was measured: it read <c>StageSlot</c> with <b>no year predicate</b>
/// against a table keyed <c>(StageId, AcademicYearId, PeriodNumber)</c>, so it attached one promotion's
/// cells to another promotion's column <i>without error</i>; it created a <c>StageSlot</c> with no
/// <c>AcademicYearId</c> (0, against a <c>Restrict</c> FK) and threw inside its own loop, after earlier
/// stages had already been committed; replaying it doubled every cohorte, there being no unique index
/// on <c>Cohort(StageId, AcademicGroupId)</c>; and it bypassed <c>SlotOverlapGuard</c>,
/// <c>GroupScheduleConflictGuard</c>, published cells, pinned cells and the intake calculator, while
/// writing no register entry at all.</para>
///
/// <para><b>Why a test rather than nothing.</b> Deleting the four files is what makes the route gone;
/// this is what keeps it gone. A route is easy to reintroduce by copying a neighbouring endpoint, and
/// the thing that made the original dangerous — that nobody could see it — would be true again
/// immediately.</para>
///
/// <para>⚠ <b>404 and 401 are the two answers that must not be confused</b>, and that is the whole
/// mechanism here: a route that still exists behind <c>RequireAuthorization()</c> answers <b>401</b> to
/// an anonymous caller, so asserting « it is refused » would pass on a route that is very much alive.
/// The control below pins that distinction rather than assuming it.</para>
/// </remarks>
public class RemovedRouteEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory;

    public RemovedRouteEndpointTests(ApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// ⚠ <b>« Gone » is asserted against a path that never existed, not against 404.</b>
    ///
    /// <para>Measured 18/09/2026: this application answers <b>405</b>, not 404, to any unmapped path
    /// under <c>/api/groups/</c> — <c>/api/groups/definitely-not-a-route-xyz</c> gives 405 while
    /// <c>/api/totally/unknown/path</c> gives 404. Routing treats the two-segment shape as a candidate
    /// because <c>groups/{id:int}</c> is mapped for other verbs, and answers « method not allowed »
    /// rather than « no such thing ». A test hard-coding 404 would have failed for a reason having
    /// nothing to do with the deletion, and one hard-coding 405 would rot the day that changes.</para>
    ///
    /// <para>⚠ Worth carrying beyond this file: the check used in several <c>SMOKE-TEST.md</c> sections
    /// — « the route is absent if it answers 404 » — <b>is path-shaped</b>, and is wrong here.</para>
    /// </summary>
    [Fact]
    public async Task The_deprecated_schedule_generator_answers_exactly_as_a_path_that_never_existed()
    {
        using var client = _factory.CreateAnonymousClient();

        var neverMapped = await client.PostAsJsonAsync(
            "/api/groups/a-path-that-was-never-mapped", new { });

        var removed = await client.PostAsJsonAsync(
            "/api/groups/generate-schedule", new { academicYearId = 1, levelId = 3 });

        removed.StatusCode.Should().Be(neverMapped.StatusCode,
            "a removed route must be indistinguishable from one that never existed");

        removed.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "401 is what a live route behind RequireAuthorization answers — the state this one spent "
            + "four days in after being hidden from Scalar but left mapped");
    }

    /// <summary>
    /// ⚠ <b>The control, and it is what gives the case above any meaning.</b> If the factory mapped
    /// nothing at all, every request would answer alike and the comparison would hold vacuously.
    /// </summary>
    /// <summary>
    /// ⚠ <b>La pause par étape est partie, et elle avait un écran — ce qui est la différence avec le
    /// cas ci-dessus.</b> <c>groups/generate-schedule</c> n'était appelé par rien ; ces deux routes-ci
    /// étaient câblées à deux boutons de « Suivi des affectations », donc les rejouer par un client
    /// resté en cache est un geste plausible, pas théorique.
    ///
    /// <para><b>Ce que l'acte faisait, mesuré le 18/09/2026 avant de le retirer :</b> la reprise
    /// allongeait la période de <c>date − début_de_pause</c> en <b>jours calendaires</b>, si bien qu'un
    /// week-end pris dans la fenêtre comptait comme deux jours perdus et rallongeait le stage d'autant ;
    /// elle poussait les périodes suivantes du même delta <b>sans la garde <c>Movable</c></b>, donc
    /// par-dessus des journées de présence déjà pointées ; elle ne touchait ni les créneaux ni les
    /// cellules, dont <c>ServiceOccupancyCalculator</c> tire l'occupation, si bien que la grille
    /// affichait l'ancienne fenêtre et le dossier la nouvelle ; elle ne levait aucun événement de
    /// domaine ; elle n'écrivait <b>rien</b> au registre, n'étant pas <c>IAuditableCommand</c> ; et,
    /// <c>DateTime.UtcNow</c> étant sa seule source de date, il fallait être là le matin même — pour
    /// poser la pause comme pour la lever. Rejouée, elle déplaçait deux fois.</para>
    ///
    /// <para><b>Et ce qui la remplace n'est pas un correctif, c'est une autre forme.</b> Une fenêtre
    /// d'examens se <b>déclare</b> (<c>PromotionPause</c> : aucune date écrite, révocable, corrigeable),
    /// et les colonnes qu'elle coupe se déplacent par <c>InternshipAssignment.Reschedule</c>, qui écrit
    /// des dates <i>absolues</i> — donc rejouable sans dériver. Réparer l'ancienne aurait voulu dire la
    /// réécrire entièrement en gardant son nom.</para>
    /// </summary>
    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task The_retired_stage_pause_answers_exactly_as_a_path_that_never_existed(string act)
    {
        using var client = _factory.CreateAnonymousClient();

        var neverMapped = await client.PostAsJsonAsync(
            "/api/stages/1/schedule/a-path-that-was-never-mapped", new { });

        var removed = await client.PostAsJsonAsync(
            $"/api/stages/1/schedule/{act}", new { academicYearId = 1 });

        removed.StatusCode.Should().Be(neverMapped.StatusCode,
            "a removed route must be indistinguishable from one that never existed");

        removed.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "401 is what a live route behind RequireAuthorization answers — and this pair had two "
            + "buttons wired to it, so a cached client will reach for it");
    }

    /// <summary>
    /// ⚠ <b>Le contrôle de la paire ci-dessus, et il est indispensable ici.</b> Les quatre actes de
    /// cycle de vie d'une étape partagent un préfixe : si <c>schedule/start</c> avait disparu avec la
    /// pause — par une coupe trop large dans le même fichier — le cas ci-dessus passerait toujours,
    /// puisqu'il ne compare qu'à un chemin jamais mappé.
    /// </summary>
    [Theory]
    [InlineData("start")]
    [InlineData("complete")]
    public async Task The_neighbouring_lifecycle_acts_are_still_mapped(string act)
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync($"/api/stages/1/schedule/{act}", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "démarrer et clôturer n'ont pas été retirés — seule la pause l'a été");
    }

    [Fact]
    public async Task A_sibling_route_that_still_exists_answers_401_to_the_same_caller()
    {
        using var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            "/api/groups/assign-partitions", new { academicYearId = 1, levelId = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a live route under RequireAuthorization refuses an anonymous caller rather than "
            + "disappearing — which is exactly what the removed one must no longer do");
    }
}
