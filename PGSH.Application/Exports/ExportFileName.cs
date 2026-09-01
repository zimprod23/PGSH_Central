using System.Globalization;
using System.Text;

namespace PGSH.Application.Exports;

/// <summary>
/// Builds the downloaded file's name from the scope it was cut for, so two exports of two promotions
/// do not land in the same folder as <c>export.xlsx</c> and <c>export (1).xlsx</c>.
/// </summary>
public static class ExportFileName
{
    public static string Build(string prefix, params string?[] parts)
    {
        var segments = new List<string> { Slug(prefix) };
        segments.AddRange(parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Slug(p!)));

        return string.Join('-', segments.Where(s => s.Length > 0)) + ".xlsx";
    }

    /// <summary>
    /// Accents folded rather than replaced by dashes: « 3ᵉ année Médecine » has to stay readable as
    /// <c>3e-annee-medecine</c>, not <c>3-ann-e-m-decine</c>.
    /// </summary>
    private static string Slug(string value)
    {
        string folded = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(folded.Length);

        foreach (char c in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        return string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
