using ClosedXML.Excel;
using FluentAssertions;
using PGSH.Infrastructure.Registrations;
using Xunit;

namespace PGSH.Tests.Infrastructure;

/// <summary>
/// The spreadsheet adapter for the faculty's réinscription roll.
///
/// <para>⚠ <b>This canvas is not one PGSH generates</b>, so there is no round-trip to lean on: the
/// only thing standing between the file and the planner is this parser reading headers somebody else
/// chose. The real 2026-2027 file is <c>Code · NOM · PRENOM · Etape 25-26 · Etape 2026/2027</c>, and
/// the two columns that carry the whole act have a year in their name that will be different next
/// September — so they are found by prefix and taken in sheet order.</para>
/// </summary>
public class ReinscriptionSheetParserTests
{
    private static readonly ClosedXmlReinscriptionSheetParser Parser = new();

    /// <summary>Builds a sheet in memory. A <c>double</c> lands in a numeric cell, everything else as text.</summary>
    private static MemoryStream SheetOf(params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Réinscription 26-27");
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c] is null) continue;
                var cell = sheet.Cell(r + 1, c + 1);
                if (rows[r][c] is double d) cell.Value = d;
                else cell.Value = rows[r][c]!.ToString();
            }

        var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>The real file's headers, verbatim.</summary>
    private static object?[] Headers() => ["Code", "NOM", "PRENOM", "Etape 25-26", "Etape 2026/2027"];

    [Fact]
    public void The_faculty_headers_are_read_as_they_are_written()
    {
        using var stream = SheetOf(
            Headers(),
            [24008386d, "ABDELLAOUI", "AYA", "MDME1", "MDME1"],
            [25019590d, "AHMED SALEM", "MELIKA", "MDME1", "MDME2"]);

        var rows = Parser.Parse(stream);

        rows.Should().HaveCount(2);
        rows[0].SheetRow.Should().Be(2, "the row number is what a refusal points the user at");
        rows.Select(r => r.Code).Should().Equal("24008386", "25019590");
        rows.Select(r => r.LastName).Should().Equal("ABDELLAOUI", "AHMED SALEM");
        rows.Select(r => r.FirstName).Should().Equal("AYA", "MELIKA");
        rows.Select(r => r.FromLevelCode).Should().Equal("MDME1", "MDME1");
        rows.Select(r => r.ToLevelCode).Should().Equal("MDME1", "MDME2");
    }

    /// <summary>
    /// ⚠ <c>Code</c> arrives as an Excel <em>number</em> in the real file. Read through
    /// <c>GetString()</c> it can come back as <c>2.4008386E7</c> or with a thousands separator,
    /// depending on the cell's format — and either way it matches no <c>Students.Appogee</c>, which
    /// holds the legacy <c>NO_ORDRE</c> as plain digits. Every one of the 6 862 rows would then read
    /// as an unknown student, which is a skip, so the file would apply cleanly and do nothing.
    /// </summary>
    [Fact]
    public void A_numeric_code_is_read_as_plain_digits()
    {
        using var stream = SheetOf(
            Headers(),
            [8004732d, "BOUKAIDI", "YOUNES", "MED07", "MED07"],
            [25030191d, "GHABED", "SOUMEYE", "MMBTM1", "MMBTM2"]);

        var rows = Parser.Parse(stream);

        rows.Select(r => r.Code).Should().Equal("8004732", "25030191");
    }

    /// <summary>A file whose Code column was saved as text reads identically.</summary>
    [Fact]
    public void A_textual_code_is_read_unchanged()
    {
        using var stream = SheetOf(Headers(), ["24008386", "ABDELLAOUI", "AYA", "MED03", "MED04"]);

        Parser.Parse(stream).Single().Code.Should().Be("24008386");
    }

    /// <summary>
    /// The year suffix changes every September, so the two level columns are matched on « Etape »
    /// alone and taken leftmost-first: the year closing, then the year opening.
    /// </summary>
    [Fact]
    public void The_level_columns_are_taken_in_sheet_order_whatever_year_they_name()
    {
        using var stream = SheetOf(
            ["Code", "NOM", "PRENOM", "Etape 2027/2028", "Etape 2028/2029"],
            [1d, "TAZI", "OMAR", "MED04", "MED05"]);

        var row = Parser.Parse(stream).Single();

        row.FromLevelCode.Should().Be("MED04");
        row.ToLevelCode.Should().Be("MED05");
    }

    /// <summary>Accents and casing are not part of the contract — « Prénom » and "PRENOM" are one column.</summary>
    [Fact]
    public void Headers_are_matched_without_accents_or_casing()
    {
        using var stream = SheetOf(
            ["code", "Nom", "Prénom", "ÉTAPE 25-26", "étape 26-27"],
            ["24008386", "ABDELLAOUI", "AYA", "MED03", "MED04"]);

        var row = Parser.Parse(stream).Single();

        row.Code.Should().Be("24008386");
        row.FirstName.Should().Be("AYA");
        row.FromLevelCode.Should().Be("MED03");
        row.ToLevelCode.Should().Be("MED04");
    }

    /// <summary>
    /// A line left completely blank is the end of the user's data, not a mistake. A line missing only
    /// its code is a mistake, and it has to reach the planner so it is reported against its own row.
    /// </summary>
    [Fact]
    public void A_blank_line_is_dropped_and_a_partly_filled_one_is_carried_through()
    {
        using var stream = SheetOf(
            Headers(),
            ["24008386", "ABDELLAOUI", "AYA", "MED03", "MED04"],
            [null, null, null, null, null],
            [null, "SANS", "CODE", "MED03", "MED04"]);

        var rows = Parser.Parse(stream);

        rows.Should().HaveCount(2);
        rows[1].Code.Should().BeNull();
        rows[1].LastName.Should().Be("SANS");
        rows[1].SheetRow.Should().Be(4, "the blank line still occupies its row number");
    }

    /// <summary>
    /// A column the file does not have becomes null on every row rather than an exception. The
    /// planner refuses the file for a missing code; a parser that threw would say only « fichier
    /// illisible », which names nothing the user can fix.
    /// </summary>
    [Fact]
    public void A_missing_column_becomes_null_rather_than_an_exception()
    {
        using var stream = SheetOf(
            ["NOM", "PRENOM", "Etape 25-26", "Etape 2026/2027"],
            ["ABDELLAOUI", "AYA", "MED03", "MED04"]);

        var row = Parser.Parse(stream).Single();

        row.Code.Should().BeNull();
        row.FromLevelCode.Should().Be("MED03");
    }

    [Fact]
    public void An_empty_workbook_yields_no_rows()
    {
        using var stream = SheetOf(Headers());

        Parser.Parse(stream).Should().BeEmpty();
    }
}
