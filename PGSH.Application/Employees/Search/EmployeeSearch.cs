using System.Linq.Expressions;
using PGSH.Application.Extensions;
using PGSH.Application.Search;
using PGSH.Domain.Employees;

namespace PGSH.Application.Employees.Search;

/// <summary>
/// « Trouver un professeur » — le pendant de <c>StudentSearch</c>, et le même défaut : la boîte
/// d'où l'on nomme le chef d'un service ne trouvait rien sur « Pr Alami Mohamed », parce qu'aucune
/// colonne ne porte le nom et le prénom à la fois.
/// </summary>
internal static class EmployeeSearch
{
    /// <summary>
    /// Restreint <paramref name="source"/> aux lignes dont l'employé — désigné par
    /// <paramref name="employee"/> — correspond au terme. Sans terme, la requête est rendue telle
    /// quelle.
    /// </summary>
    public static IQueryable<TSource> WhereEmployeeMatches<TSource>(
        this IQueryable<TSource> source,
        string? searchTerm,
        Expression<Func<TSource, Employee>> employee)
    {
        foreach (var word in SearchTerms.Split(searchTerm))
            source = source.Where(employee.Through(Matches(word)));

        return source;
    }

    /// <summary>Un employé sur qui ce mot se retrouve, dans l'une de ses orthographes.</summary>
    internal static Expression<Func<Employee, bool>> Matches(SearchWord word) =>
        word.Spellings
            .Select(Carries)
            .Aggregate((left, right) => left.Or(right));

    private static Expression<Func<Employee, bool>> Carries(string spelling) =>
        employee =>
            (employee.LastName ?? "").ToLower().Contains(spelling)
         || (employee.FirstName ?? "").ToLower().Contains(spelling)
         || (employee.PPR ?? "").ToLower().Contains(spelling)
         || (employee.CIN ?? "").ToLower().Contains(spelling)
         || (employee.Email ?? "").ToLower().Contains(spelling);
}
