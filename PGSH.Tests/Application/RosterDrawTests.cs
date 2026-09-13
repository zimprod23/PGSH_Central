using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PGSH.Application.AcademicGroups.Manage;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// « La composition des groupes doit être aléatoire » — demandé par la faculté le 12/09/2026.
///
/// <para>Le découpage distribuait les inscriptions dans l'ordre où la requête les lisait, c'est-à-dire
/// par nom de famille : les groupes se formaient par tranches de l'alphabet, les porteurs d'un même nom
/// partaient ensemble, et le rang alphabétique d'un étudiant décidait ses périodes, ses services et ses
/// chefs pour toute l'année. Personne n'avait choisi cette propriété — c'était l'ordre
/// de lecture devenu règle de répartition.</para>
///
/// <para>⚠ <b>Ce que le tirage ne touche pas</b> : ni le nombre de rosters, ni leur taille, qui
/// restent l'affaire de <c>RosterCut</c> — le dernier test le vérifie sur le handler, parce que c'est
/// exactement le genre de propriété qu'un mélange emporte sans le dire.</para>
/// </summary>
public class RosterDrawTests
{
    /// <summary>
    /// La propriété qui compte le plus : un tirage est une <b>permutation</b>. Perdre ou dupliquer un
    /// étudiant ici se lirait comme une promotion d'une autre taille, et rien ne le dirait.
    /// </summary>
    [Fact]
    public void A_draw_keeps_exactly_the_same_members_each_once()
    {
        var promotion = Enumerable.Range(1, 200).ToList();

        var dealt = RosterDraw.Deal(promotion, seed: 4242);

        dealt.Should().BeEquivalentTo(promotion);
        dealt.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// ⚠ <b>Reproductible, donc explicable.</b> Le numéro du tirage part au registre avec l'acte ;
    /// c'est la seule façon de répondre plus tard à « pourquoi cet étudiant dans ce groupe ? ».
    /// </summary>
    [Fact]
    public void The_same_draw_number_deals_the_same_order()
    {
        var promotion = Enumerable.Range(1, 60).ToList();

        RosterDraw.Deal(promotion, seed: 7).Should().Equal(RosterDraw.Deal(promotion, seed: 7));
        RosterDraw.Deal(promotion, seed: 7).Should().NotEqual(RosterDraw.Deal(promotion, seed: 8));
    }

    /// <summary>La liste de l'appelant n'est pas touchée : elle sert encore à compter les candidats.</summary>
    [Fact]
    public void A_draw_leaves_the_list_it_was_given_alone()
    {
        var promotion = Enumerable.Range(1, 30).ToList();

        RosterDraw.Deal(promotion, seed: 1);

        promotion.Should().Equal(Enumerable.Range(1, 30));
    }

    /// <summary>Les cas dégénérés — une promotion d'un étudiant, et aucune.</summary>
    [Fact]
    public void A_draw_of_one_or_none_is_that_same_list()
    {
        RosterDraw.Deal(new List<int>(), seed: 3).Should().BeEmpty();
        RosterDraw.Deal(new List<int> { 9 }, seed: 3).Should().Equal(9);
    }

    /// <summary>Deux actes successifs ne sont pas le même tirage.</summary>
    [Fact]
    public void Each_act_draws_its_own_number()
    {
        Enumerable.Range(0, 50)
            .Select(_ => RosterDraw.NewSeed())
            .Distinct()
            .Should().HaveCountGreaterThan(1);
    }

    /// <summary>
    /// Le découpage, de bout en bout : 200 inscriptions nommées dans l'ordre alphabétique, coupées en
    /// dix groupes. ⚠ <b>L'ancien comportement rendait exactement les dix tranches de l'alphabet</b> —
    /// Nom001-020, Nom021-040, … — donc il suffit de vérifier qu'aucun groupe n'est un intervalle
    /// contigu de rangs pour savoir qu'un tirage a eu lieu.
    /// </summary>
    [Fact]
    public async Task A_cut_no_longer_follows_the_alphabet()
    {
        await using var db = TestHarness.NewContext(nameof(A_cut_no_longer_follows_the_alphabet));
        db.SeedCatalog();

        foreach (int i in Enumerable.Range(1, 200))
            db.SeedRegistration($"Etudiant{i:D3}", $"Nom{i:D3}");

        await db.SaveChangesAsync();

        var trail = new RecordingAuditTrail();

        var result = await new AutoArrangeGroupsCommandHandler(db, trail).Handle(
            new AutoArrangeGroupsCommand(
                TestHarness.LevelId, TestHarness.CurrentYearId, GroupSize: null, GroupCount: 10),
            default);

        result.IsSuccess.Should().BeTrue();

        var placed = await db.Registrations
            .Include(r => r.Student)
            .Where(r => r.AcademicGroupId != null)
            .Select(r => new { Group = r.AcademicGroupId!.Value, Rank = r.Student.LastName })
            .ToListAsync();

        placed.Should().HaveCount(200, "un tirage est une permutation, pas un filtre");

        var rosters = placed
            .GroupBy(p => p.Group)
            .Select(g => g.Select(p => int.Parse(p.Rank[3..])).OrderBy(rank => rank).ToList())
            .ToList();

        rosters.Should().HaveCount(10);
        rosters.Should().OnlyContain(ranks => ranks.Count == 20, "la coupe reste celle de RosterCut");

        rosters.Should().Contain(
            ranks => ranks.Max() - ranks.Min() > ranks.Count - 1,
            "au moins un groupe n'est pas une tranche contiguë de l'alphabet");

        trail.Fields.Should().ContainKey("drawSeed", "sans le numéro, le tirage n'est plus explicable");
    }
}
