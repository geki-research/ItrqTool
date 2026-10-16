using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;
using ItrqTool.Domain;
using static ItrqTool.Integration.Tests.RlqEDInject.RlqEDWorkbookBuilder;

namespace ItrqTool.Integration.Tests.RlqEDInject;

/// <summary>
/// Closing end-to-end test of the RLQ ED inject answer policy with the REAL ClosedXML reader and
/// writer, the production structure mediator, and the committed ED configs and schemas. One rich
/// fixture, one run (mirrors the standard RLQ "all policy arms" e2e). The task is constructed
/// directly (as InjectWrongVersionTests does), so the inject task's messages ARE the union of all
/// task messages in the run, and every file lives in an isolated temp directory.
///
/// Questions (section 1, rows 4–21; XrefId shared by v01 and v02, distinct texts):
///   P1 row 4  "p1"    drift: v01 "…in 2024?" vs v02 "…in 2025?"; J/K/L = 3/1/2 → G/H/I; S → T.
///   P2 row 5  "p2"    widen: v01 J = 5 (WholeNumber DV); v02 J Decimal DV → G = 5 + Info.
///   P3 row 6  "p3p4"  Holding: v01 K = 3.5; v02 K WholeNumber DV (rejects) while v02 H (write
///                     column) has a Decimal DV (would accept) → H skipped + Error.
///   P4 row 6  "p3p4"  EBOe: v01 L = 3.5; v02 L no DV while v02 I (write column) has a WholeNumber
///                     DV (would reject) → I = 3.5.
///   P5 row 7  "p5p6"  Holding: v01 K = "x"; v02 K list DV = range reference Lists!$A$1:$A$2
///                     (a, b) → resolved → H skipped + Error (unresolved would be a Warning).
///   P6 row 7  "p5p6"  EBOe: v01 L = "b"; v02 L inline list "a,b,c" → I = "b".
///   P7 row 8  "p7"    blank answer arm: v01 J blank; K/L = 4/6 → G empty, H/I = 4/6.
///   P8 rows 9–11 (v01) / 9–10 (v02) "p8": 3 vs 2 explanation rows → N9/N10 + one Warning; S → T.
/// </summary>
public sealed class RlqEDInjectPolicyEndToEndTests
{
    // ED v01 letters (configs/rlq-ed-v01-columns-config.json + rlq-ed-inject-config.json source letters).
    private const string V01Number = "C", V01Text = "D", V01Answer = "J", V01Holding = "K", V01EBOe = "L",
                         V01CurExpl = "O", V01ProvidedBy = "S", V01Xref = "U";

    // ED v02 letters (configs/rlq-ed-v02-columns-config.json + rlq-ed-inject-config.json target letters).
    private const string V02Number = "C", V02Text = "D", WriteAnswer = "G", WriteHolding = "H", WriteEBOe = "I",
                         CurAnswer = "J", CurHolding = "K", CurEBOe = "L", WriteExpl = "N",
                         WriteProvidedBy = "T", V02Xref = "V";

    private const int FirstRow = 4, LastRow = 11;

    [Fact]
    public async Task AllPolicyArms_RealAdapters_ExactCellsAndExactMessages()
    {
        var dir = NewWorkDir("policy");
        try
        {
            var previousPath = Path.Combine(dir, "ed_previous_v01.xlsx");
            var currentPath  = Path.Combine(dir, "ed_current_v02.xlsx");
            var outputPath   = Path.Combine(dir, "ed_injected.xlsx");
            WritePreviousV01(previousPath);
            WriteCurrentV02(currentPath);

            var result = await EdTask().ExecuteAsync(
                Context(dir, previousPath, currentPath, outputPath, EdInjectConfigPath()), CancellationToken.None);

            result.Succeeded.Should().BeTrue("policy skips never fail the task; messages: {0}",
                string.Join(" | ", result.Messages.Select(m => $"{m.Severity}: {m.Text}")));

            // ── Exact message set (ordered: question order, then the summary) and total count ──
            var msgs = result.Messages;
            msgs.Should().HaveCount(6, "messages: {0}", string.Join(" | ", msgs.Select(m => $"{m.Severity}: {m.Text}")));

            // P1 drift — one unprefixed Warning for row 4.
            msgs[0].Severity.Should().Be(MessageSeverity.Warning);
            msgs[0].Text.Should().StartWith("Row 4 (xref p1): question text changed since the previous year (similarity ");
            msgs[0].Text.Should().Contain("previous values carried forward.");
            msgs[0].Text.Should().Contain("\"How is the IT risk assessed in 2024?\"");
            msgs[0].Text.Should().Contain("\"How is the IT risk assessed in 2025?\"");

            // P2 widen — one Info on the answer arm.
            msgs[1].Severity.Should().Be(MessageSeverity.Info);
            msgs[1].Text.Should().Be(
                "[answer] Row 5 (xref p2): answer type widened (WholeNumber → Decimal) — value written as-is.");

            // P3 — Holding judged against v02 K (WholeNumber), not H (Decimal).
            msgs[2].Severity.Should().Be(MessageSeverity.Error);
            msgs[2].Text.Should().StartWith("[answer (Holding)] Row 6 (xref p3p4): value '");
            msgs[2].Text.Should().Contain("' does not conform to the target data-validation rule");

            // P5 — range-reference list resolved to (a, b): "x" is not a member → Error, not Warning.
            msgs[3].Severity.Should().Be(MessageSeverity.Error);
            msgs[3].Text.Should().StartWith(
                "[answer (Holding)] Row 7 (xref p5p6): value 'x' does not conform to the target data-validation rule");

            // P8 — explanation row-count mismatch, one unprefixed Warning.
            msgs[4].Severity.Should().Be(MessageSeverity.Warning);
            msgs[4].Text.Should().Be(
                "Row 9 (xref p8): explanation row count mismatch (source 3, target 2) — overlap written.");

            // Summary — 6 Agree; 12 writes (P1 G/H/I/T = 4, P2 G = 1, P4 I = 1, P6 I = 1,
            // P7 H/I = 2, P8 N9/N10/T = 3).
            msgs[5].Severity.Should().Be(MessageSeverity.Info);
            msgs[5].Text.Should().Be(
                "Inject complete: 6 confident match(es) injected, 0 ambiguous (left untouched), " +
                "0 unmatched. Wrote 12 cell(s) to ed_injected.xlsx.");

            // ── Output workbook ──────────────────────────────────────────────────
            File.Exists(outputPath).Should().BeTrue();
            using var wb = new XLWorkbook(outputPath);
            var ws = wb.Worksheet(SheetName);

            // Every target cell (G/H/I/N/T, rows 4–11) is either exactly the expected write or empty.
            var expected = new Dictionary<string, object>
            {
                ["G4"] = 3.0, ["H4"] = 1.0, ["I4"] = 2.0, ["T4"] = "OU-P1",   // P1 (+ P9)
                ["G5"] = 5.0,                                                  // P2
                ["I6"] = 3.5,                                                  // P4 (P3: H6 empty)
                ["I7"] = "b",                                                  // P6 (P5: H7 empty)
                ["H8"] = 4.0, ["I8"] = 6.0,                                    // P7 (G8 empty)
                ["N9"] = "x8a", ["N10"] = "x8b", ["T9"] = "OU-P8",             // P8 (+ P9)
            };

            foreach (var col in new[] { WriteAnswer, WriteHolding, WriteEBOe, WriteExpl, WriteProvidedBy })
                for (var row = FirstRow; row <= LastRow; row++)
                {
                    var address = $"{col}{row}";
                    var cell = ws.Cell(address);
                    if (!expected.TryGetValue(address, out var value))
                    {
                        cell.IsEmpty().Should().BeTrue($"{address} must not be written");
                        continue;
                    }

                    if (value is double number)
                    {
                        cell.DataType.Should().Be(XLDataType.Number, $"{address} is a typed numeric write");
                        cell.GetValue<double>().Should().Be(number, address);
                    }
                    else
                    {
                        cell.DataType.Should().Be(XLDataType.Text, $"{address} is a text write");
                        cell.GetString().Should().Be((string)value, address);
                    }
                }
            expected.Should().HaveCount(12, "the expected write set matches the summary's 'Wrote 12 cell(s)'");

            // Never written: v02 J/K/L/O/P/Q keep the template's (blank) values on every row.
            foreach (var col in new[] { CurAnswer, CurHolding, CurEBOe, "O", "P", "Q" })
                for (var row = FirstRow; row <= LastRow; row++)
                    ws.Cell($"{col}{row}").IsEmpty().Should().BeTrue($"{col}{row} is never written");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Fixture builders ────────────────────────────────────────────────────────

    // ED v01 previous response: section header at row 3, eight questions' worth of rows 4–11.
    private static void WritePreviousV01(string path)
    {
        using var wb = NewStampedWorkbook("rlq-ed", "v01", out var ws);
        ws.Cell(3, V01Text).Value = "Test Section";

        // P1 — drift (2024 vs 2025), all three arms whole numbers, provided-by.
        V01Anchor(ws, 4, "p1", "How is the IT risk assessed in 2024?");
        V01Whole(ws, 4, V01Answer, 3);
        V01Whole(ws, 4, V01Holding, 1);
        V01Whole(ws, 4, V01EBOe, 2);
        ws.Cell(4, V01ProvidedBy).Value = "OU-P1";

        // P2 — widen: answer 5 under a WholeNumber DV (target J is Decimal).
        V01Anchor(ws, 5, "p2", "Which IT risk appetite statement applies?");
        V01Whole(ws, 5, V01Answer, 5);

        // P3 / P4 — Holding 3.5 and EBOe 3.5 (Decimal DV); answer blank.
        V01Anchor(ws, 6, "p3p4", "How many critical IT incidents occurred?");
        V01Decimal(ws, 6, V01Holding, 3.5);
        V01Decimal(ws, 6, V01EBOe, 3.5);

        // P5 / P6 — Holding text "x" (no DV), EBOe text "b" (no DV); answer blank.
        V01Anchor(ws, 7, "p5p6", "Which IT outsourcing model is used?");
        ws.Cell(7, V01Holding).Value = "x";
        ws.Cell(7, V01EBOe).Value = "b";

        // P7 — answer blank, Holding 4, EBOe 6.
        V01Anchor(ws, 8, "p7", "How many IT security staff are employed?");
        V01Whole(ws, 8, V01Holding, 4);
        V01Whole(ws, 8, V01EBOe, 6);

        // P8 — three explanation rows (9–11), provided-by; answers blank.
        V01Anchor(ws, 9, "p8", "Describe the IT continuity arrangements.");
        ws.Cell(9, V01CurExpl).Value = "x8a";
        ws.Cell(9, V01ProvidedBy).Value = "OU-P8";
        ws.Cell(10, V01Xref).Value = "p8";
        ws.Cell(10, V01CurExpl).Value = "x8b";
        ws.Cell(11, V01Xref).Value = "p8";
        ws.Cell(11, V01CurExpl).Value = "x8c";

        wb.SaveAs(path);
    }

    // ED v02 current template: same questions (P1's text drifted), DVs on the current columns
    // J/K/L (and, for P3/P4, decoy DVs on the write columns H/I). No values in any target column.
    private static void WriteCurrentV02(string path)
    {
        using var wb = NewStampedWorkbook("rlq-ed", "v02", out var ws);
        var lists = wb.Worksheets.Add("Lists");
        lists.Cell("A1").Value = "a";
        lists.Cell("A2").Value = "b";

        ws.Cell(3, V02Text).Value = "Test Section";

        // P1 — drifted text; permissive WholeNumber DVs on J/K/L.
        V02Anchor(ws, 4, "p1", "How is the IT risk assessed in 2025?");
        foreach (var col in new[] { CurAnswer, CurHolding, CurEBOe })
            ws.Cell(4, col).CreateDataValidation().WholeNumber.Between(0, 1000);

        // P2 — Decimal DV on J (widen target).
        V02Anchor(ws, 5, "p2", "Which IT risk appetite statement applies?");
        ws.Cell(5, CurAnswer).CreateDataValidation().Decimal.EqualOrGreaterThan(0);

        // P3 — K WholeNumber (rejects 3.5); decoy Decimal on H (would accept).
        // P4 — L no DV (accepts 3.5); decoy WholeNumber on I (would reject).
        V02Anchor(ws, 6, "p3p4", "How many critical IT incidents occurred?");
        ws.Cell(6, CurHolding).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(6, WriteHolding).CreateDataValidation().Decimal.EqualOrGreaterThan(0);
        ws.Cell(6, WriteEBOe).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);

        // P5 — K list DV from a range reference on another sheet (Lists!$A$1:$A$2 = a, b).
        // P6 — L inline list a,b,c.
        V02Anchor(ws, 7, "p5p6", "Which IT outsourcing model is used?");
        ws.Cell(7, CurHolding).CreateDataValidation().List(lists.Range("A1:A2"));
        ws.Cell(7, CurEBOe).CreateDataValidation().List("\"a,b,c\"");

        // P7 — permissive WholeNumber DVs on J/K/L.
        V02Anchor(ws, 8, "p7", "How many IT security staff are employed?");
        foreach (var col in new[] { CurAnswer, CurHolding, CurEBOe })
            ws.Cell(8, col).CreateDataValidation().WholeNumber.Between(0, 1000);

        // P8 — two explanation rows (9–10).
        V02Anchor(ws, 9, "p8", "Describe the IT continuity arrangements.");
        ws.Cell(10, V02Xref).Value = "p8";

        wb.SaveAs(path);
    }

    private static void V01Anchor(IXLWorksheet ws, int row, string xref, string text)
    {
        ws.Cell(row, V01Number).Value = xref;
        ws.Cell(row, V01Text).Value   = text;
        ws.Cell(row, V01Xref).Value   = xref;
    }

    private static void V02Anchor(IXLWorksheet ws, int row, string xref, string text)
    {
        ws.Cell(row, V02Number).Value = xref;
        ws.Cell(row, V02Text).Value   = text;
        ws.Cell(row, V02Xref).Value   = xref;
    }

    private static void V01Whole(IXLWorksheet ws, int row, string col, int value)
    {
        ws.Cell(row, col).Value = value;
        ws.Cell(row, col).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
    }

    private static void V01Decimal(IXLWorksheet ws, int row, string col, double value)
    {
        ws.Cell(row, col).Value = value;
        ws.Cell(row, col).CreateDataValidation().Decimal.EqualOrGreaterThan(0);
    }
}
