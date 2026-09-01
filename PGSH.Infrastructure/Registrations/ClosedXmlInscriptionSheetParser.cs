using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using PGSH.Application.Students.Registrations.Inscription;

namespace PGSH.Infrastructure.Registrations;

/// <summary>
/// The .xlsx side of the inscription import. Deliberately dumb, like the déliberation's: it locates
/// the columns by header and hands every cell on as text. Anything it cannot make sense of becomes a
/// raw string on that row rather than an exception, so one bad cell is reported against its own line
/// in the preview instead of failing the whole upload with nothing to show for it.
///
/// <para>The one thing it does interpret is a <b>date cell</b>. A workbook that holds a real date
/// gives <c>GetString()</c> an OLE serial number — "45 292" — which no parser downstream can read
/// back as a birthday, so dates are rendered in the invariant form the planner accepts. Everything
/// else stays exactly as typed.</para>
/// </summary>
internal sealed class ClosedXmlInscriptionSheetParser : IInscriptionSheetParser
{
    private const string CneHeader = "cne";
    private const string AppogeeHeader = "apogee";
    private const string LastNameHeader = "nom";
    private const string FirstNameHeader = "prenom";
    private const string CinHeader = "cin";
    private const string EmailHeader = "e-mail";
    private const string GenderHeader = "sexe";
    private const string BirthDateHeader = "date de naissance";
    private const string BirthPlaceHeader = "lieu de naissance";
    private const string BacYearHeader = "annee du bac";
    private const string BacSeriesHeader = "serie du bac";
    private const string AccessGradeHeader = "note d'acces";
    private const string AgreementHeader = "convention";
    private const string OriginInstitutionHeader = "etablissement d'origine";
    private const string OriginCountryHeader = "pays d'origine";
    private const string OriginLastYearHeader = "derniere annee suivie";
    private const string EquivalenceReferenceHeader = "reference d'equivalence";
    private const string EquivalenceDateHeader = "date d'equivalence";

    private static readonly string[] TemplateHeaders =
    [
        "CNE", "Apogée", "Nom", "Prénom", "CIN", "E-mail", "Sexe", "Date de naissance",
        "Lieu de naissance", "Année du bac", "Série du bac", "Note d'accès", "Convention",
        "Établissement d'origine", "Pays d'origine", "Dernière année suivie",
        "Référence d'équivalence", "Date d'équivalence",
    ];

    /// <summary>The first column of the provenance block, 1-based — where the required-above-first-year
    /// highlighting starts.</summary>
    private const int OriginFirstColumn = 14;

    /// <summary>Offered as dropdowns so the common case never produces an unrecognised word. The
    /// planner accepts more spellings than these — a hand-built file is still readable.</summary>
    private static readonly string[] Genders = ["M", "F"];

    private static readonly string[] BacSeriesValues =
        ["SVT", "PC", "Math A", "Math B", "Bac Français", "Bac Mission", "Étranger"];

    private static readonly string[] Agreements = ["Aucune", "Payée amie", "International", "Autre"];

    /// <summary>How far down the blank sheet the dropdowns and borders reach. Past it the file still
    /// parses — the validation is a convenience, not the contract.</summary>
    private const int BlankRows = 400;

    public IReadOnlyList<InscriptionRow> Parse(Stream sheet)
    {
        using var workbook = new XLWorkbook(sheet);
        var worksheet = workbook.Worksheets.First();
        var used = worksheet.RangeUsed();
        if (used is null)
            return [];

        var rows = used.RowsUsed().ToList();
        if (rows.Count == 0)
            return [];

        var columns = MapHeaders(rows[0]);
        var parsed = new List<InscriptionRow>();

        foreach (var row in rows.Skip(1))
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string header in columns.Keys)
                values[header] = Text(row, columns, header);

            // A line the user left completely blank is not a mistake — it is the end of their data.
            if (values.Values.All(v => v is null))
                continue;

            parsed.Add(new InscriptionRow(
                row.RowNumber(),
                Cne: values.GetValueOrDefault(CneHeader),
                Appogee: values.GetValueOrDefault(AppogeeHeader),
                LastName: values.GetValueOrDefault(LastNameHeader),
                FirstName: values.GetValueOrDefault(FirstNameHeader),
                Cin: values.GetValueOrDefault(CinHeader),
                Email: values.GetValueOrDefault(EmailHeader),
                Gender: values.GetValueOrDefault(GenderHeader),
                DateOfBirth: values.GetValueOrDefault(BirthDateHeader),
                PlaceOfBirth: values.GetValueOrDefault(BirthPlaceHeader),
                BacYear: values.GetValueOrDefault(BacYearHeader),
                BacSeries: values.GetValueOrDefault(BacSeriesHeader),
                AccessGrade: values.GetValueOrDefault(AccessGradeHeader),
                Agreement: values.GetValueOrDefault(AgreementHeader),
                OriginInstitution: values.GetValueOrDefault(OriginInstitutionHeader),
                OriginCountry: values.GetValueOrDefault(OriginCountryHeader),
                OriginLastYearCompleted: values.GetValueOrDefault(OriginLastYearHeader),
                EquivalenceReference: values.GetValueOrDefault(EquivalenceReferenceHeader),
                EquivalenceDate: values.GetValueOrDefault(EquivalenceDateHeader)));
        }

        return parsed;
    }

    public byte[] BuildTemplate(InscriptionTemplate template)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Inscription");

        for (int i = 0; i < TemplateHeaders.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = TemplateHeaders[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xF1, 0xF5, 0xF9);
        }

        // Above the first year the provenance block stops being optional, so it is coloured as what
        // it is rather than explained only in a tab nobody opens.
        var origin = sheet.Range(1, OriginFirstColumn, 1, TemplateHeaders.Length);
        origin.Style.Fill.BackgroundColor = template.OriginRequired
            ? XLColor.FromArgb(0xFE, 0xF3, 0xC7)
            : XLColor.FromArgb(0xF1, 0xF5, 0xF9);

        if (template.OriginRequired)
            origin.Style.Font.FontColor = XLColor.FromArgb(0xB4, 0x53, 0x09);

        int lastRow = 1 + BlankRows;
        sheet.Range(2, 1, lastRow, TemplateHeaders.Length).Style.Border.OutsideBorder =
            XLBorderStyleValues.Hair;

        // Text format on the identifier columns: a CNE beginning with a zero, typed into a General
        // cell, comes back as a number with the zero gone — and that is a student nobody can match.
        sheet.Column(1).Style.NumberFormat.Format = "@";
        sheet.Column(2).Style.NumberFormat.Format = "@";
        sheet.Column(5).Style.NumberFormat.Format = "@";

        Dropdown(sheet, 7, lastRow, Genders);
        Dropdown(sheet, 11, lastRow, BacSeriesValues);
        Dropdown(sheet, 13, lastRow, Agreements);

        AddInstructions(workbook, template);

        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return buffer.ToArray();
    }

    private static void Dropdown(IXLWorksheet sheet, int column, int lastRow, string[] values) =>
        sheet.Range(2, column, lastRow, column)
            .CreateDataValidation()
            .List($"\"{string.Join(",", values)}\"", true);

    private static void AddInstructions(XLWorkbook workbook, InscriptionTemplate template)
    {
        var sheet = workbook.AddWorksheet("Mode d'emploi");

        var lines = new List<string>
        {
            $"Inscription — {template.LevelLabel}, année universitaire {template.AcademicYearLabel}",
            "",
            "Cette feuille sert à inscrire les étudiants que la réinscription ne peut pas reporter,",
            "parce qu'ils ne portent aucune inscription l'année précédente :",
            "",
            "    • les nouveaux inscrits de 1ʳᵉ année ;",
            "    • les transferts venus d'une autre faculté, entrant en cours de cursus ;",
            "    • les étudiants de retour après une interruption ;",
            "    • les réorientations d'un programme vers un autre.",
            "",
            "Un redoublant n'est PAS concerné : il est reporté par la réinscription depuis sa décision",
            "de déliberation. Un étudiant déjà inscrit cette année est simplement ignoré, de sorte que",
            "le fichier peut être renvoyé complété des arrivées tardives.",
            "",
            "Colonnes obligatoires : CNE (ou Apogée), Nom, Prénom.",
            "",
            "    CNE          absent, un code provisoire « SANS-CNE-… » est attribué et signalé.",
            "    E-mail       absente, une adresse prenom_nom@um5.ac.ma est générée et signalée.",
            "                 Renseignez-la si l'étudiant en a déjà une : c'est son identifiant de",
            "                 connexion.",
            "    Sexe         M ou F.",
            "    Dates        jj/mm/aaaa.",
            "    Note d'accès 14,25 ou 14.25, indifféremment.",
            "    Convention   Payée amie, International, Autre — ou vide.",
            "",
        };

        lines.AddRange(template.OriginRequired
            ? [
                $"⚠ PROVENANCE — OBLIGATOIRE pour cette promotion ({template.LevelYear}ᵉ année).",
                "",
                "    Un étudiant inconnu de la faculté qui entre au-dessus de la 1ʳᵉ année a suivi les",
                "    années précédentes ailleurs. L'établissement d'origine, la dernière année qui y a",
                "    été suivie et la référence de la décision d'équivalence sont requis ensemble : ils",
                "    forment l'équivalence, et sans elle son dossier s'ouvre au milieu d'un cursus sans",
                "    rien qui dise que les années du dessous ont été reconnues.",
                "",
                "    Les étudiants déjà connus de la faculté (retour, réorientation) n'en ont pas",
                "    besoin — sauf s'ils ont effectivement étudié ailleurs entre-temps, auquel cas",
                "    renseignez-la.",
                "",
              ]
            : [
                "PROVENANCE — facultative en 1ʳᵉ année.",
                "",
                "    À renseigner seulement pour un étudiant venu d'un autre établissement.",
                "",
              ]);

        lines.AddRange([
            "Ne modifiez pas les en-têtes.",
            "Une ligne laissée entièrement vide est ignorée.",
            "L'import est appliqué en totalité ou pas du tout : une seule ligne en erreur l'annule.",
            "",
            "La simulation est obligatoire, et elle indique combien d'ÉTUDIANTS seront créés dans la",
            "base. Ce nombre doit être confirmé pour appliquer : une inscription crée des identités",
            "(CNE, numéro Apogée, adresse de connexion) et rien ne les retire ensuite.",
        ]);

        for (int i = 0; i < lines.Count; i++)
            sheet.Cell(i + 1, 1).Value = lines[i];

        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
    }

    /// <summary>Header text → column number, matched loosely so accents and casing do not matter.</summary>
    private static Dictionary<string, int> MapHeaders(IXLRangeRow header)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in header.Cells())
        {
            string? key = Fold(cell.GetString());
            if (key is not null && !columns.ContainsKey(key))
                columns[key] = cell.Address.ColumnNumber;
        }
        return columns;
    }

    private static string? Text(IXLRangeRow row, IReadOnlyDictionary<string, int> columns, string header)
    {
        if (!columns.TryGetValue(header, out int column))
            return null;

        var cell = row.Worksheet.Cell(row.RowNumber(), column);

        // A real date cell renders as an OLE serial through GetString(); nothing downstream could read
        // "45 292" back as a birthday.
        string value = cell.DataType == XLDataType.DateTime && cell.TryGetValue(out DateTime date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : cell.GetString().Trim();

        return value.Length == 0 ? null : value;
    }

    /// <summary>Lower-cases, trims and strips accents so "Prénom", "PRENOM" and "prenom" are one header.</summary>
    private static string? Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var stripped = new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        return stripped.Normalize(NormalizationForm.FormC);
    }
}
