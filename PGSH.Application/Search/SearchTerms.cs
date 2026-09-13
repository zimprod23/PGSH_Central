using System.Globalization;
using System.Text;

namespace PGSH.Application.Search;

/// <summary>
/// Ce qu'un humain a tapé dans une boîte de recherche, découpé en ce qu'il faut chercher.
///
/// <para>⚠ <b>Un terme n'est pas un mot.</b> Chaque écran comparait la chaîne entière à chaque
/// colonne, si bien que « Mohamed Alami » — le nom complet, la première chose que l'on tape — ne
/// trouvait <b>personne</b> : le prénom ne le contient pas, le nom ne le contient pas, et aucune
/// autre colonne ne porte les deux. Un terme est donc une <i>conjonction de mots</i> : chaque mot
/// doit se retrouver quelque part sur la même personne, et l'ordre dans lequel ils ont été tapés ne
/// compte pas — « Alami Mohamed » est la même recherche.</para>
///
/// <para>Cette règle élargit strictement l'ancienne : si la chaîne entière figure dans une colonne,
/// chacun de ses mots y figure aussi. Rien de ce qui se trouvait avant ne cesse de se trouver.</para>
/// </summary>
internal static class SearchTerms
{
    /// <summary>
    /// Au-delà de ce nombre, les mots supplémentaires sont ignorés.
    /// </summary>
    /// <remarks>
    /// ⚠ Ignorer un mot <b>élargit</b> le résultat — une conjonction plus courte retient plus de
    /// monde — donc la borne ne peut pas cacher la personne cherchée. C'est l'inverse qui serait un
    /// défaut. Elle est là pour qu'une liste collée par accident dans la boîte de recherche ne
    /// devienne pas une requête de cent prédicats.
    /// </remarks>
    public const int MaximumWords = 5;

    /// <summary>
    /// Les séparateurs de mots. ⚠ <b>Ni le tiret ni l'apostrophe n'en sont</b> : « EL-AMRANI » et
    /// « D'ALAMI » sont des noms, et les couper chercherait des morceaux que personne ne porte. La
    /// virgule et le point-virgule en sont, parce qu'un nom recopié d'une liste arrive « ALAMI,
    /// Mohamed » et que « alami, » ne figure dans aucune colonne.
    /// </summary>
    private static readonly char[] Separators = [' ', '\t', '\n', '\r', ',', ';', '/', '|'];

    /// <summary>
    /// Les mots du terme, en minuscules, chacun avec l'orthographe sans accent quand elle diffère.
    /// </summary>
    public static IReadOnlyList<SearchWord> Split(string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return [];

        return
        [
            .. searchTerm
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.ToLowerInvariant())
                .Distinct()
                .Take(MaximumWords)
                .Select(word => new SearchWord(word, Unaccent(word)))
        ];
    }

    /// <summary>
    /// Le mot sans ses signes diacritiques — « zoubaïr » → « zoubair ».
    /// </summary>
    /// <remarks>
    /// ⚠ <b>C'est une orthographe <i>de plus</i>, jamais un remplacement.</b> La base porte les deux
    /// formes — l'import Access a saisi des noms sans accent, les saisies récentes en portent — donc
    /// replier le terme tout seul ferait perdre « BENAÏSSA » à quelqu'un qui tape « Benaïssa », ce
    /// qui marchait avant. Les deux orthographes sont cherchées, et la seconde n'est ajoutée que
    /// lorsqu'elle diffère de la première.
    ///
    /// <para>Le repli ne va que dans un sens : un terme accentué retrouve une colonne qui ne l'est
    /// pas, l'inverse demanderait de replier la <i>colonne</i>, donc <c>unaccent</c> côté PostgreSQL
    /// — une extension et une colonne calculée, pas une expression LINQ.</para>
    /// </remarks>
    private static string? Unaccent(string word)
    {
        string folded = Fold(word);
        return folded == word ? null : folded;
    }

    /// <summary>
    /// Une chaîne réduite à ce par quoi on la reconnaît : sans espaces de bord, en minuscules, sans
    /// accents. « Réanimation  » et « REANIMATION » sont le même nom de service.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Pour apparier un libellé, jamais pour chercher une personne.</b> Ici les deux
    /// orthographes ne coexistent pas : on compare un libellé saisi dans un tableur au libellé du
    /// catalogue, et c'est la <i>même</i> réduction appliquée aux deux côtés qui les fait se
    /// rencontrer. La recherche, elle, ne peut pas replier la colonne — d'où <see cref="Unaccent"/>,
    /// qui ajoute une orthographe au lieu d'en imposer une.
    /// </remarks>
    public static string Fold(string value)
    {
        string trimmed = value.Trim().ToLowerInvariant();
        var builder = new StringBuilder(trimmed.Length);

        foreach (char character in trimmed.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>
/// Un mot du terme recherché, et ses orthographes acceptées.
/// </summary>
/// <param name="Text">Le mot tel qu'il a été tapé, en minuscules.</param>
/// <param name="Unaccented">La même chose sans accents, ou <c>null</c> lorsqu'elle est identique.</param>
internal sealed record SearchWord(string Text, string? Unaccented)
{
    /// <summary>Les orthographes à chercher : une, ou deux quand le mot portait un accent.</summary>
    public IReadOnlyList<string> Spellings => Unaccented is null ? [Text] : [Text, Unaccented];
}
