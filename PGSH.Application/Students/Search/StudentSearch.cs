using System.Linq.Expressions;
using PGSH.Application.Extensions;
using PGSH.Application.Search;
using PGSH.Domain.Students;

namespace PGSH.Application.Students.Search;

/// <summary>
/// « Trouver un étudiant » — une seule règle, partagée par tous les écrans qui en cherchent un.
///
/// <para>Ils étaient sept à la réécrire, et ils n'étaient d'accord ni sur les colonnes ni sur ce
/// qu'un terme veut dire : la liste des étudiants cherchait dans six colonnes, le détail d'un groupe
/// dans cinq, les occupants d'un service dans trois — un étudiant trouvé sur son Apogée depuis la
/// liste ne l'était pas depuis le service où il se tient. ⚠ <b>Et tous les sept comparaient le terme
/// entier</b>, si bien que taper un nom complet ne trouvait rien du tout. Voir
/// <see cref="SearchTerms"/> pour ce qu'un terme est.</para>
/// </summary>
/// <remarks>
/// ⚠ <b>Les colonnes sont les mêmes partout, et c'est le point.</b> Un écran qui cherche dans moins
/// de colonnes qu'un autre ne rend pas moins de monde : il rend « aucun résultat » sur une saisie
/// qui marche à côté, ce qui se lit comme un étudiant absent de la liste et non comme une recherche
/// plus étroite.
/// </remarks>
internal static class StudentSearch
{
    /// <summary>
    /// Restreint <paramref name="source"/> aux lignes dont l'étudiant — désigné par
    /// <paramref name="student"/> — correspond au terme. Sans terme, la requête est rendue telle
    /// quelle.
    /// </summary>
    /// <remarks>
    /// Un <c>Where</c> par mot, donc une conjonction : chaque mot doit se retrouver sur le
    /// <i>même</i> étudiant. Composer les mots dans un seul prédicat donnerait le même SQL ; les
    /// empiler est ce qui garde chaque étage lisible dans <c>ToQueryString()</c>.
    /// </remarks>
    public static IQueryable<TSource> WhereStudentMatches<TSource>(
        this IQueryable<TSource> source,
        string? searchTerm,
        Expression<Func<TSource, Student>> student)
    {
        foreach (var word in SearchTerms.Split(searchTerm))
            source = source.Where(student.Through(Matches(word)));

        return source;
    }

    /// <summary>Un étudiant sur qui ce mot se retrouve, dans l'une de ses orthographes.</summary>
    internal static Expression<Func<Student, bool>> Matches(SearchWord word) =>
        word.Spellings
            .Select(Carries)
            .Aggregate((left, right) => left.Or(right));

    /// <summary>
    /// Les colonnes sur lesquelles un étudiant se reconnaît : son nom, ses identifiants, son adresse.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Chaque colonne est abaissée, pas seulement la première.</b> <c>Appogee</c> a été
    /// sensible à la casse pendant des mois pour avoir été laissé tel quel d'un côté de la
    /// comparaison, donc « ap2200a » ne trouvait jamais AP2200A. Et chacune est protégée du
    /// <c>null</c> : le CNE est facultatif sur 46 % de la base, et le fournisseur en mémoire lève une
    /// exception là où PostgreSQL rend simplement « pas vrai ».
    /// </remarks>
    private static Expression<Func<Student, bool>> Carries(string spelling) =>
        student =>
            (student.LastName ?? "").ToLower().Contains(spelling)
         || (student.FirstName ?? "").ToLower().Contains(spelling)
         || (student.CNE ?? "").ToLower().Contains(spelling)
         || (student.Appogee ?? "").ToLower().Contains(spelling)
         || (student.CIN ?? "").ToLower().Contains(spelling)
         || (student.Email ?? "").ToLower().Contains(spelling);
}
