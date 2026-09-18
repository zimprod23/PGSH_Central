using System.Linq.Expressions;

namespace PGSH.Application.Extensions;

/// <summary>
/// Recoud deux arbres d'expression en un seul, pour que le tout reste traduisible en SQL.
///
/// <para>Une règle partagée s'écrit sur l'entité dont elle parle — « cet étudiant correspond-il au
/// mot cherché » — alors que les écrans qui l'appliquent interrogent des inscriptions, des périodes
/// ou des cellules. ⚠ <b>Appeler le prédicat dans une autre expression ne marche pas</b> : EF refuse
/// un <c>Invoke</c> qu'il n'a pas de moyen de traduire. Le chemin vers l'entité est donc
/// <i>substitué</i> au paramètre du prédicat, ce qui produit un arbre que le fournisseur ne
/// distingue pas d'un prédicat écrit à la main sur place.</para>
/// </summary>
internal static class ExpressionComposition
{
    /// <summary>
    /// <paramref name="predicate"/>, posé sur ce que <paramref name="path"/> désigne — « cette
    /// inscription, dont l'étudiant correspond ».
    /// </summary>
    public static Expression<Func<TSource, bool>> Through<TSource, TTarget>(
        this Expression<Func<TSource, TTarget>> path,
        Expression<Func<TTarget, bool>> predicate) =>
        Expression.Lambda<Func<TSource, bool>>(
            Substitute(predicate.Body, predicate.Parameters[0], path.Body),
            path.Parameters[0]);

    /// <summary>
    /// Le prédicat inverse, recousu plutôt que réécrit — « cette colonne porte une rotation que le
    /// déplacement <i>refuserait</i> » se pose sur la règle qui dit ce qu'il accepte, jamais sur une
    /// copie de ses quatre drapeaux inversés à la main.
    /// </summary>
    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> predicate) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(predicate.Body), predicate.Parameters[0]);

    /// <summary>Les deux prédicats, dont l'un ou l'autre suffit.</summary>
    public static Expression<Func<T, bool>> Or<T>(
        this Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right) =>
        Expression.Lambda<Func<T, bool>>(
            Expression.OrElse(
                left.Body,
                Substitute(right.Body, right.Parameters[0], left.Parameters[0])),
            left.Parameters[0]);

    private static Expression Substitute(
        Expression body, ParameterExpression parameter, Expression replacement) =>
        new ParameterSubstitution(parameter, replacement).Visit(body)!;

    private sealed class ParameterSubstitution(ParameterExpression parameter, Expression replacement)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == parameter ? replacement : base.VisitParameter(node);
    }
}
