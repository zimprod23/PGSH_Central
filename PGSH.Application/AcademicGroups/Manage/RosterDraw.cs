namespace PGSH.Application.AcademicGroups.Manage;

/// <summary>
/// Dans quel ordre une promotion est distribuée dans ses rosters — un <b>tirage</b>, pas un tri.
///
/// <para>Le découpage lisait les inscriptions par nom de famille et les distribuait dans cet ordre,
/// si bien que les groupes se remplissaient par tranches de l'alphabet : les porteurs d'un même nom se
/// suivaient par construction et partaient donc dans le même groupe, et le rang alphabétique d'un
/// étudiant décidait toute son année, c'est-à-dire ses périodes, ses services et ses chefs. Ce n'est pas une propriété que
/// quiconque a choisie ; c'est l'ordre de lecture de la requête qui s'était installé en règle de
/// répartition. Demandé par la faculté le 12/09/2026 : la composition des groupes doit être
/// aléatoire.</para>
///
/// <para>⚠ <b>Le tirage ne change ni le nombre de rosters ni leur taille.</b> Celles-là restent
/// l'affaire de <see cref="RosterCut"/>, qui est pure et le reste : ce qui bouge ici est
/// uniquement <i>qui</i> va dans quel roster, jamais combien. Les deux classes se lisent ensemble —
/// l'une dit la forme de la coupe, l'autre l'ordre dans lequel on y dépose les gens.</para>
/// </summary>
/// <remarks>
/// <para>⚠ <b>Le tirage est reproductible, et c'est ce qui le rend auditable.</b> Un acte qui
/// mélange sans rien dire ne peut plus jamais être expliqué — « pourquoi cet étudiant dans le groupe
/// 41 ? » n'a alors aucune réponse. Le numéro du tirage est donc déposé au registre par le handler
/// (<c>drawSeed</c>), et le même numéro sur la même liste de candidats redonne exactement la même
/// distribution. Ce n'est pas une promesse de rejouabilité — les candidats d'un jour ne sont pas
/// ceux du lendemain — c'est la trace de ce qui a été tiré.</para>
///
/// <para>La liste d'entrée est lue dans un ordre <i>total</i> (nom, puis identifiant) pour que cette
/// propriété tienne : à départager les homonymes au hasard du plan d'exécution, le numéro ne
/// désignerait plus rien.</para>
/// </remarks>
internal static class RosterDraw
{
    /// <summary>Le numéro d'un nouveau tirage, à déposer au registre avec l'acte.</summary>
    public static int NewSeed() => Random.Shared.Next();

    /// <summary>
    /// <paramref name="members"/> dans l'ordre du tirage <paramref name="seed"/> — une permutation,
    /// donc exactement les mêmes personnes, chacune une fois.
    /// </summary>
    /// <remarks>
    /// Fisher–Yates, et la liste d'origine n'est pas touchée : l'appelant garde la sienne pour
    /// compter ce qu'il a reçu.
    /// </remarks>
    public static List<T> Deal<T>(IReadOnlyList<T> members, int seed)
    {
        var dealt = members.ToList();
        var draw = new Random(seed);

        for (int i = dealt.Count - 1; i > 0; i--)
        {
            int j = draw.Next(i + 1);
            (dealt[i], dealt[j]) = (dealt[j], dealt[i]);
        }

        return dealt;
    }
}
