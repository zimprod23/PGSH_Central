using ClosedXML.Excel;
using FluentAssertions;
using PGSH.Application.Stages.Evaluations.Import;
using PGSH.Domain.Stages;
using PGSH.Infrastructure.Evaluations;
using Xunit;

namespace PGSH.Tests.Infrastructure;

// The spreadsheet adapter. Its job is to hand the planner what the user actually typed — including
// the mistakes, so they surface row by row in the preview instead of failing the whole upload.
public class EvaluationSheetParserTests
{
    private static readonly ClosedXmlEvaluationSheetParser Parser = new();

    private static EvaluationImportTemplate Template(
        EvaluationImportScope scope = EvaluationImportScope.WholeStage,
        EvaluationMode mode = EvaluationMode.Numeric,
        int? periodNumber = null) =>
        new("Cardiologie", scope, mode, periodNumber,
            [
                new EvaluationImportTemplateStudent("CNE001", "AP2200A", "Sara Bennani", "Groupe 10"),
                new EvaluationImportTemplateStudent("CNE002", "AP2200B", "Ali Amrani", "Groupe 10"),
            ]);

    /// <summary>Builds a sheet in memory from rows of cell values, the first row being the headers.</summary>
    private static MemoryStream SheetOf(params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Notes");
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

    [Fact]
    public void The_generated_template_reads_back_with_every_student_pre_filled()
    {
        var bytes = Parser.BuildTemplate(Template());

        using var stream = new MemoryStream(bytes);
        var rows = Parser.Parse(stream);

        rows.Should().HaveCount(2, "the identity columns are filled even though no mark is entered yet");
        rows.Select(r => r.Cne).Should().Equal("CNE001", "CNE002");
        rows.Select(r => r.Appogee).Should().Equal("AP2200A", "AP2200B");
        rows.Should().OnlyContain(r => r.Mark == null && r.Verdict == null);
    }

    [Fact]
    public void A_per_period_template_carries_the_period_number_on_every_row()
    {
        var bytes = Parser.BuildTemplate(
            Template(EvaluationImportScope.SinglePeriod, EvaluationMode.ValidatePeriod, periodNumber: 2));

        using var stream = new MemoryStream(bytes);
        var rows = Parser.Parse(stream);

        rows.Should().OnlyContain(r => r.PeriodNumber == 2);
    }

    [Fact]
    public void Headers_are_matched_whatever_their_accents_and_casing()
    {
        using var sheet = SheetOf(
            ["cne", "APOGÉE", "Période", "résultat", "NOTE", "Remarque"],
            ["CNE001", "AP2200A", 1d, null, 14.5d, "Bon stage"]);

        var rows = Parser.Parse(sheet);

        var row = rows.Should().ContainSingle().Subject;
        row.Cne.Should().Be("CNE001");
        row.Appogee.Should().Be("AP2200A");
        row.PeriodNumber.Should().Be(1);
        row.Mark.Should().Be(14.5m);
        row.Remark.Should().Be("Bon stage");
    }

    [Fact]
    public void A_mark_typed_as_text_with_a_comma_is_still_read_as_a_number()
    {
        using var sheet = SheetOf(
            ["CNE", "Note"],
            ["CNE001", "12,5"]);

        Parser.Parse(sheet).Should().ContainSingle().Which.Mark.Should().Be(12.5m);
    }

    // A cell the parser cannot make sense of becomes a null on its own row, so the planner reports
    // "note manquante" against that line instead of the upload failing with nothing to show.
    [Fact]
    public void An_unreadable_mark_leaves_the_row_intact_with_no_value()
    {
        using var sheet = SheetOf(
            ["CNE", "Note"],
            ["CNE001", "à revoir"]);

        var row = Parser.Parse(sheet).Should().ContainSingle().Subject;
        row.Cne.Should().Be("CNE001");
        row.Mark.Should().BeNull();
    }

    [Fact]
    public void Blank_lines_are_skipped_rather_than_reported_as_errors()
    {
        using var sheet = SheetOf(
            ["CNE", "Note"],
            ["CNE001", 14d],
            [null, null],
            ["CNE002", 11d]);

        Parser.Parse(sheet).Select(r => r.Cne).Should().Equal("CNE001", "CNE002");
    }

    [Fact]
    public void Each_row_remembers_its_line_number_in_the_sheet()
    {
        using var sheet = SheetOf(
            ["CNE", "Note"],
            ["CNE001", 14d],
            ["CNE002", 11d]);

        Parser.Parse(sheet).Select(r => r.SheetRow).Should().Equal(2, 3);
    }

    [Fact]
    public void A_sheet_with_only_headers_yields_no_rows()
    {
        using var sheet = SheetOf(["CNE", "Apogée", "Note"]);

        Parser.Parse(sheet).Should().BeEmpty();
    }

    [Fact]
    public void A_column_the_sheet_does_not_have_simply_reads_as_empty()
    {
        using var sheet = SheetOf(
            ["CNE", "Résultat"],
            ["CNE001", "Validé"]);

        var row = Parser.Parse(sheet).Should().ContainSingle().Subject;
        row.Verdict.Should().Be("Validé");
        row.Mark.Should().BeNull();
        row.Appogee.Should().BeNull();
    }
}
