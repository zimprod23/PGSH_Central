using FluentAssertions;
using PGSH.Application.AcademicGroups.Manage;
using Xunit;

namespace PGSH.Tests.Application;

/// <summary>
/// L'arithmétique du découpage, prise seule.
///
/// <para>⚠ <b>Pourquoi elle a son propre fichier.</b> Le défaut qu'elle corrige ne se voit pas dans un
/// test de handler : celui-ci répond « 12 groupes créés, 232 étudiants assignés », ce qui est vrai
/// des deux répartitions — onze groupes de 20 plus un de 12, et quatre de 20 plus huit de 19. Ce qui
/// les sépare est la <b>forme</b>, et rien ne la regardait. Mesuré à l'écran le 10/09/2026 sur la
/// 4ᵉ Pharmacie.</para>
/// </summary>
public class RosterCutTests
{
    /// <summary>
    /// ⚠ Le cas mesuré. 232 en taille 20 : le nombre de groupes ne bouge pas — c'est la répartition
    /// des étudiants entre eux qui change.
    /// </summary>
    [Fact]
    public void The_measured_case_no_longer_leaves_a_runt()
    {
        var sizes = RosterCut.BySize(232, 20);

        sizes.Should().HaveCount(12, "⌈232 ÷ 20⌉ — le nombre de groupes est celui qu'il a toujours été");
        sizes.Should().OnlyContain(s => s <= 20, "« taille de groupe » est un maximum, et il tient");
        sizes.Count(s => s == 20).Should().Be(4);
        sizes.Sum().Should().Be(232);
    }

    /// <summary>
    /// ⚠ <b>L'ordre, et c'est lui le correctif.</b> Les quatre groupes de 20 sont <i>espacés</i>, pas
    /// posés en tête. Les tailles et leur nombre ne bougent pas — ce qui bouge est ce qu'en fait l'acte
    /// suivant, <c>PartitionAllocator.Contiguous</c>, qui découpe les partitions en blocs de numéros
    /// de roster consécutifs.
    /// </summary>
    [Fact]
    public void The_larger_rosters_are_spread_through_the_sequence_not_grouped_at_the_front()
    {
        RosterCut.BySize(232, 20).Should().Equal(
            20, 19, 19, 20, 19, 19, 20, 19, 19, 20, 19, 19);
    }

    /// <summary>
    /// ⚠ <b>Le défaut mesuré sur la base vivante le 11/09/2026, en une assertion.</b> La 3ᵉ MED — 933
    /// inscriptions, 100 rosters, 10 partitions — sortait en
    /// <c>100, 100, 100, 93, 90, 90, 90, 90, 90, 90</c> parce que les 33 rosters de 10 portaient les
    /// numéros 1 à 33, et que la partition A prend les numéros 1 à 10.
    ///
    /// <para>Une partition est une <b>colonne</b>, et une colonne est ce qu'un service tient à un
    /// instant : c'est ce qui a fait passer Santé Publique et Simulation Médicale d'une marge
    /// annoncée de +6 (100 places pour 94) à exactement <b>0</b> sur trois colonnes sur dix.</para>
    /// </summary>
    [Fact]
    public void A_contiguous_block_of_rosters_carries_a_balanced_number_of_students()
    {
        var sizes = RosterCut.ByCount(933, 100);

        var partitions = Enumerable.Range(0, 10)
            .Select(p => sizes.Skip(p * 10).Take(10).Sum())
            .ToList();

        partitions.Sum().Should().Be(933, "personne n'est perdu par le rééquilibrage");
        partitions.Should().OnlyContain(n => n == 93 || n == 94, "les colonnes valent 93 ou 94, plus 100 ou 90");
        (partitions.Max() - partitions.Min()).Should().Be(1, "l'écart mesuré était de 10");
    }

    /// <summary>
    /// La même propriété, sur toute la plage que la campagne va parcourir plutôt que sur le seul cas
    /// qui a mordu — chaque promotion de 2026-2027 par chaque découpage plausible.
    /// </summary>
    [Theory]
    [InlineData(933)] [InlineData(925)] [InlineData(842)] [InlineData(701)]
    [InlineData(1347)] [InlineData(232)] [InlineData(314)] [InlineData(212)] [InlineData(163)]
    public void No_block_of_rosters_is_ever_more_than_one_student_ahead(int members)
    {
        foreach (int partitions in new[] { 2, 3, 4, 5, 6, 9, 10 })
        {
            foreach (int perPartition in new[] { 1, 2, 5, 10 })
            {
                int count = partitions * perPartition;
                if (count > members) continue;

                var sizes = RosterCut.ByCount(members, count);

                var blocks = Enumerable.Range(0, partitions)
                    .Select(p => sizes.Skip(p * perPartition).Take(perPartition).Sum())
                    .ToList();

                blocks.Sum().Should().Be(members, $"{members} en {count} rosters");

                (blocks.Max() - blocks.Min()).Should().BeLessThanOrEqualTo(1,
                    $"{members} en {count} rosters lus par blocs de {perPartition}");
            }
        }
    }

    /// <summary>La demande de l'utilisateur : « la 5ᵉ MED en 100 groupes ».</summary>
    [Fact]
    public void A_count_is_honoured_exactly_and_split_evenly()
    {
        var sizes = RosterCut.ByCount(842, 100);

        sizes.Should().HaveCount(100);
        sizes.Sum().Should().Be(842, "personne n'est laissé de côté");
        (sizes.Max() - sizes.Min()).Should().Be(1, "également veut dire à un étudiant près");
    }

    /// <summary>
    /// Les deux propriétés qui définissent « également », vérifiées sur toute la plage qu'une
    /// promotion peut prendre — pas sur trois exemples choisis.
    /// </summary>
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(7)] [InlineData(13)] [InlineData(42)]
    [InlineData(86)] [InlineData(163)] [InlineData(232)] [InlineData(701)] [InlineData(1347)]
    public void Every_cut_of_a_real_promotion_is_whole_and_balanced(int members)
    {
        foreach (int count in new[] { 1, 2, 3, 5, 8, 12, 20, 50, 100 })
        {
            var sizes = RosterCut.ByCount(members, count);

            sizes.Sum().Should().Be(members, $"{members} en {count} groupes doit placer tout le monde");
            sizes.Should().OnlyContain(s => s > 0, "un groupe vide est une ligne que la répartition porte pour rien");
            (sizes.Max() - sizes.Min()).Should().BeLessThanOrEqualTo(1, $"{members} en {count}");
        }

        foreach (int size in new[] { 1, 2, 5, 15, 20, 40 })
        {
            var sizes = RosterCut.BySize(members, size);

            sizes.Sum().Should().Be(members);
            sizes.Should().OnlyContain(s => s > 0 && s <= size, $"{members} en taille {size}");
            (sizes.Max() - sizes.Min()).Should().BeLessThanOrEqualTo(1);
        }
    }

    /// <summary>
    /// ⚠ Plus de groupes que d'étudiants : un par étudiant, jamais de groupe vide. Le handler refuse
    /// avant d'en arriver là — ceci est le filet, pour que l'arithmétique ne puisse pas produire une
    /// ligne que personne n'habite.
    /// </summary>
    [Fact]
    public void More_groups_than_students_yields_one_each_and_never_an_empty_one()
    {
        RosterCut.ByCount(3, 10).Should().BeEquivalentTo(new[] { 1, 1, 1 });
        RosterCut.ByCount(0, 10).Should().BeEmpty();
        RosterCut.BySize(0, 20).Should().BeEmpty();
    }

    // ── L'apport entre textes CNPN ───────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ <b>Un nombre ne se divise pas une fois.</b> Les groupes ne mélangent jamais deux textes, donc
    /// « 12 » s'apporte entre eux avant qu'on ne coupe quoi que ce soit — proportionnellement, au plus
    /// grand reste.
    /// </summary>
    [Fact]
    public void A_target_count_is_shared_between_the_texts_in_proportion()
    {
        var shares = RosterCut.Apportion([600, 300], 12);

        shares.Should().BeEquivalentTo(new[] { 8, 4 });
        shares.Sum().Should().Be(12, "le total demandé est tenu quand les effectifs le permettent");
    }

    /// <summary>
    /// ⚠ <b>Un texte qui porte des étudiants reçoit toujours au moins un groupe</b> — sinon ses
    /// inscrits n'ont aucun roster, ce qu'aucun arrondi ne doit pouvoir produire.
    /// </summary>
    [Fact]
    public void A_text_with_students_is_never_rounded_out_of_existence()
    {
        var shares = RosterCut.Apportion([930, 3], 10);

        shares[1].Should().BeGreaterThan(0, "trois étudiants sont trois étudiants");
        shares.Should().OnlyContain(s => s > 0);
    }

    /// <summary>
    /// ⚠ <b>Et jamais plus de groupes qu'il n'a d'étudiants.</b> Un panier de 7 ne rend pas 12 groupes,
    /// donc le total s'écarte de ce qui a été demandé — c'est ce que le rapport doit nommer plutôt que
    /// de laisser passer l'écart pour de l'arithmétique.
    /// </summary>
    [Fact]
    public void A_small_text_caps_its_share_and_the_total_falls_short()
    {
        var shares = RosterCut.Apportion([7, 7], 30);

        shares.Should().BeEquivalentTo(new[] { 7, 7 });
        shares.Sum().Should().Be(14, "14 et non 30 — et c'est au handler de le dire");
    }

    /// <summary>Un seul texte, le cas ordinaire : tout lui revient.</summary>
    [Fact]
    public void One_text_takes_the_whole_count()
    {
        RosterCut.Apportion([232], 12).Should().BeEquivalentTo(new[] { 12 });
        RosterCut.Apportion([], 12).Should().BeEmpty();
    }
}
