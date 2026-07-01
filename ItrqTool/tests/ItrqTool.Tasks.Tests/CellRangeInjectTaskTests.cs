using ClosedXML.Excel; // test fixture creation only
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Tasks;

namespace ItrqTool.Tasks.Tests;

public sealed class CellRangeInjectTaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-cellinject-task-tests", Guid.NewGuid().ToString("N"));

    private static CellRangeInjectTask MakeTask(
        IExcelStructureReader? reader = null,
        IExcelTemplateWriter? writer = null)
    {
        var r = reader ?? Substitute.For<IExcelStructureReader>();
        var w = writer ?? Substitute.For<IExcelTemplateWriter>();
        return new CellRangeInjectTask(r, w, NullLogger<CellRangeInjectTask>.Instance);
    }

    private static TaskExecutionContext MakeCtx(
        string dir,
        Dictionary<string, string> parameters,
        Dictionary<string, string>? inputs = null,
        string outputFile = "output.xlsx")
        => new(
            TaskId: "cri",
            InputPaths: inputs ?? new Dictionary<string, string>(),
            OutputPaths: new Dictionary<string, string> { ["output"] = Path.Combine(dir, outputFile) },
            Logger: NullLogger.Instance,
            WorkingDirectory: dir)
        {
            Parameters = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase)
        };

    private static void CreateEmptyWorkbook(string path)
    {
        using var wb = new XLWorkbook();
        wb.Worksheets.Add("Sheet1");
        wb.SaveAs(path);
    }

    // ── Happy: single-cell inject B2->G2 ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SingleCellHappy_PopulatesCorrectEntry()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(src, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B2"] = new("42", null, null, null, NativeValue: 42.0)
                });

            IReadOnlyList<CellWriteEntry>? capturedCells = null;
            var writer = Substitute.For<IExcelTemplateWriter>();
            writer.When(w => w.Populate(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>()))
                .Do(ci => capturedCells = ci.ArgAt<IReadOnlyList<CellWriteEntry>>(2));

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2",
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            capturedCells.Should().NotBeNull();
            capturedCells!.Should().ContainSingle().Which.Should().Be(
                new CellWriteEntry(Row: 2, Column: "G", Value: "42", TypedValue: 42.0));
            writer.Received(1).Populate(tpl, "Sheet1", Arg.Any<IReadOnlyList<CellWriteEntry>>(),
                Path.Combine(dir, "output.xlsx"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Happy: range-pair shift B2:B4->G2:G4 with three cells ───────────────

    [Fact]
    public async Task ExecuteAsync_RangePairShift_PopulatesThreeEntries()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(src, "Data", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B2"] = new("10", null, null, null, NativeValue: 10.0),
                    ["B3"] = new("20", null, null, null, NativeValue: 20.0),
                    ["B4"] = new("30", null, null, null, NativeValue: 30.0),
                });

            IReadOnlyList<CellWriteEntry>? capturedCells = null;
            var writer = Substitute.For<IExcelTemplateWriter>();
            writer.When(w => w.Populate(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>()))
                .Do(ci => capturedCells = ci.ArgAt<IReadOnlyList<CellWriteEntry>>(2));

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2:B4->G2:G4",
                    ["sourceSheetName"] = "Data",
                    ["targetSheetName"] = "Data",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            capturedCells.Should().NotBeNull();
            capturedCells!.Should().BeEquivalentTo(new[]
            {
                new CellWriteEntry(Row: 2, Column: "G", Value: "10", TypedValue: 10.0),
                new CellWriteEntry(Row: 3, Column: "G", Value: "20", TypedValue: 20.0),
                new CellWriteEntry(Row: 4, Column: "G", Value: "30", TypedValue: 30.0),
            }, opts => opts.WithStrictOrdering());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Happy: native type carried — numeric → double, text → string ─────────

    [Fact]
    public async Task ExecuteAsync_NativeTypesCarried_NumericAndTextPreserved()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(src, "S", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B2"] = new("3.14", null, null, null, NativeValue: 3.14),       // numeric
                    ["B3"] = new("hello", null, null, null, NativeValue: "hello"),    // text
                });

            IReadOnlyList<CellWriteEntry>? capturedCells = null;
            var writer = Substitute.For<IExcelTemplateWriter>();
            writer.When(w => w.Populate(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>()))
                .Do(ci => capturedCells = ci.ArgAt<IReadOnlyList<CellWriteEntry>>(2));

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->C2;B3->C3",
                    ["sourceSheetName"] = "S",
                    ["targetSheetName"] = "S",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            capturedCells.Should().NotBeNull();
            capturedCells![0].TypedValue.Should().Be(3.14);       // double
            capturedCells![1].TypedValue.Should().Be("hello");    // string
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: missing required parameter (mappings omitted) ───────────────

    [Fact]
    public async Task ExecuteAsync_MissingMappingsParam_FailsBeforeWrite()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var writer = Substitute.For<IExcelTemplateWriter>();
            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    // "mappings" intentionally omitted
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = "any.xlsx",
                    ["targetTemplate"] = "any.xlsx",
                });

            var result = await MakeTask(writer: writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("mappings"));
            writer.DidNotReceive().Populate(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: missing required input (source omitted) ─────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingSourceInput_FailsBeforeWrite()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var writer = Substitute.For<IExcelTemplateWriter>();
            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2",
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    // "source" intentionally omitted
                    ["targetTemplate"] = "template.xlsx",
                });

            var result = await MakeTask(writer: writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("source"));
            writer.DidNotReceive().Populate(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: malformed mapping token ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MalformedMappingToken_FailsBeforeWrite()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var writer = Substitute.For<IExcelTemplateWriter>();
            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->",   // missing target address
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = "src.xlsx",
                    ["targetTemplate"] = "tpl.xlsx",
                });

            var result = await MakeTask(writer: writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
            writer.DidNotReceive().Populate(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: dimension mismatch ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DimensionMismatch_FailsBeforeWrite()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var writer = Substitute.For<IExcelTemplateWriter>();
            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2:B4->G2:G5",  // 3×1 vs 4×1
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = "src.xlsx",
                    ["targetTemplate"] = "tpl.xlsx",
                });

            var result = await MakeTask(writer: writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
            writer.DidNotReceive().Populate(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: source file not found ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SourceFileNotFound_FailsBeforeWrite()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(tpl);

            var writer = Substitute.For<IExcelTemplateWriter>();
            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2",
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = Path.Combine(dir, "does-not-exist.xlsx"),
                    ["targetTemplate"] = tpl,
                });

            var result = await MakeTask(writer: writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
            writer.DidNotReceive().Populate(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: missing source sheet (reader throws) ────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingSourceSheet_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
                .Returns(_ => throw new ArgumentException("Sheet 'NoSuchSheet' not found."));

            var writer = Substitute.For<IExcelTemplateWriter>();
            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2",
                    ["sourceSheetName"] = "NoSuchSheet",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
            writer.DidNotReceive().Populate(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Missing source sheet logs available worksheet names at Error level ───

    [Fact]
    public async Task ExecuteAsync_MissingSourceSheet_LogsErrorWithAvailableWorksheets()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.GetWorksheetNames(src).Returns(new[] { "Sheet1", "OtherSheet" });
            reader.ReadCells(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
                .Returns(_ => throw new ArgumentException("Worksheet 'NoSuchSheet' was not found."));

            var writer = Substitute.For<IExcelTemplateWriter>();
            var logger = Substitute.For<ILogger<CellRangeInjectTask>>();

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2",
                    ["sourceSheetName"] = "NoSuchSheet",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var task = new CellRangeInjectTask(reader, writer, logger);
            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            logger.ReceivedCalls()
                .Should().Contain(call =>
                    call.GetMethodInfo().Name == "Log" &&
                    (LogLevel)call.GetArguments()[0]! == LogLevel.Error &&
                    call.GetArguments()[2]!.ToString()!.Contains("NOT FOUND") &&
                    call.GetArguments()[2]!.ToString()!.Contains("'Sheet1'") &&
                    call.GetArguments()[2]!.ToString()!.Contains("'OtherSheet'"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Target template unopenable (GetWorksheetNames throws) logs Error, fails ─

    [Fact]
    public async Task ExecuteAsync_TargetWorksheetNamesThrows_LogsErrorAndFails()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.GetWorksheetNames(src).Returns(new[] { "Sheet1" });
            reader.ReadCells(src, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B2"] = new("42", null, null, null, NativeValue: 42.0)
                });
            reader.GetWorksheetNames(tpl)
                .Returns(_ => throw new IOException("The process cannot access the file because it is being used by another process."));

            var writer = Substitute.For<IExcelTemplateWriter>();
            var logger = Substitute.For<ILogger<CellRangeInjectTask>>();

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2",
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var task = new CellRangeInjectTask(reader, writer, logger);
            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            logger.ReceivedCalls()
                .Should().Contain(call =>
                    call.GetMethodInfo().Name == "Log" &&
                    (LogLevel)call.GetArguments()[0]! == LogLevel.Error &&
                    call.GetArguments()[2]!.ToString()!.Contains("failed to open target template") &&
                    call.GetArguments()[2]!.ToString()!.Contains(tpl));
            writer.DidNotReceive().Populate(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── DV-aware gating (BL-053 P3): InjectionValueGuard wired against the target cell's DV rule ──

    // Inline List target: a conforming member injects; a non-member is skipped with an Error.
    [Fact]
    public async Task ExecuteAsync_TargetInlineListDv_ConformingInjectsNonMemberSkipsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(src, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B2"] = new("Yes", null, null, null),
                    ["B3"] = new("Maybe", null, null, null),
                });
            reader.ReadCells(tpl, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["G2"] = new(null, "List", "\"Yes,No\"", null),
                    ["G3"] = new(null, "List", "\"Yes,No\"", null),
                });

            IReadOnlyList<CellWriteEntry>? capturedCells = null;
            var writer = Substitute.For<IExcelTemplateWriter>();
            writer.When(w => w.Populate(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>()))
                .Do(ci => capturedCells = ci.ArgAt<IReadOnlyList<CellWriteEntry>>(2));

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2;B3->G3",
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            capturedCells.Should().NotBeNull();
            capturedCells!.Should().ContainSingle().Which.Should().Be(
                new CellWriteEntry(Row: 2, Column: "G", Value: "Yes", TypedValue: null));
            result.Messages.Should().ContainSingle(m =>
                m.Severity == MessageSeverity.Error
                && m.Text.StartsWith("G3:")
                && m.Text.Contains("does not conform"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // WholeNumber target (bounded 1..10): a conforming value injects; a violator is skipped.
    [Fact]
    public async Task ExecuteAsync_TargetWholeNumberDv_ConformingInjectsViolatorSkipsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(src, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B2"] = new("5", null, null, null, NativeValue: 5.0),
                    ["B3"] = new("99", null, null, null, NativeValue: 99.0),
                });
            reader.ReadCells(tpl, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["G2"] = new(null, "WholeNumber", "1", null, DataValidationOperator: "Between", DataValidationFormula2: "10"),
                    ["G3"] = new(null, "WholeNumber", "1", null, DataValidationOperator: "Between", DataValidationFormula2: "10"),
                });

            IReadOnlyList<CellWriteEntry>? capturedCells = null;
            var writer = Substitute.For<IExcelTemplateWriter>();
            writer.When(w => w.Populate(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>()))
                .Do(ci => capturedCells = ci.ArgAt<IReadOnlyList<CellWriteEntry>>(2));

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2;B3->G3",
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            capturedCells.Should().NotBeNull();
            capturedCells!.Should().ContainSingle().Which.Should().Be(
                new CellWriteEntry(Row: 2, Column: "G", Value: "5", TypedValue: 5.0));
            result.Messages.Should().ContainSingle(m =>
                m.Severity == MessageSeverity.Error
                && m.Text.StartsWith("G3:")
                && m.Text.Contains("does not conform"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // Same-sheet range-ref List target: exercises DvRangeRefResolver wiring against the target workbook.
    [Fact]
    public async Task ExecuteAsync_TargetRangeRefListDv_MemberInjectsNonMemberSkipsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(src, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B2"] = new("Alpha", null, null, null),
                    ["B3"] = new("Gamma", null, null, null),
                });
            reader.ReadCells(tpl, "Sheet1", Arg.Is<IReadOnlyList<string>>(a => a.Contains("G2") || a.Contains("G3")))
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["G2"] = new(null, "List", "K1:K2", null),
                    ["G3"] = new(null, "List", "K1:K2", null),
                });
            reader.ReadCells(tpl, "Sheet1", Arg.Is<IReadOnlyList<string>>(a => a.Contains("K1:K2")))
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["K1"] = new("Alpha", null, null, null),
                    ["K2"] = new("Beta", null, null, null),
                });

            IReadOnlyList<CellWriteEntry>? capturedCells = null;
            var writer = Substitute.For<IExcelTemplateWriter>();
            writer.When(w => w.Populate(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>()))
                .Do(ci => capturedCells = ci.ArgAt<IReadOnlyList<CellWriteEntry>>(2));

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2;B3->G3",
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            capturedCells.Should().NotBeNull();
            capturedCells!.Should().ContainSingle().Which.Should().Be(
                new CellWriteEntry(Row: 2, Column: "G", Value: "Alpha", TypedValue: null));
            result.Messages.Should().ContainSingle(m =>
                m.Severity == MessageSeverity.Error
                && m.Text.StartsWith("G3:")
                && m.Text.Contains("does not conform"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // Range-ref List target whose backing range is all-blank: UnresolvableList → Skip (never a false Inject).
    [Fact]
    public async Task ExecuteAsync_TargetRangeRefListDv_BlankBackingRange_SkipsAsUnresolvable()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(src, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B2"] = new("Alpha", null, null, null),
                });
            reader.ReadCells(tpl, "Sheet1", Arg.Is<IReadOnlyList<string>>(a => a.Contains("G2")))
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["G2"] = new(null, "List", "K1:K2", null),
                });
            reader.ReadCells(tpl, "Sheet1", Arg.Is<IReadOnlyList<string>>(a => a.Contains("K1:K2")))
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["K1"] = new(null, null, null, null),
                    ["K2"] = new(null, null, null, null),
                });

            var writer = Substitute.For<IExcelTemplateWriter>();
            IReadOnlyList<CellWriteEntry>? capturedCells = null;
            writer.When(w => w.Populate(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>()))
                .Do(ci => capturedCells = ci.ArgAt<IReadOnlyList<CellWriteEntry>>(2));

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2",
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            capturedCells.Should().NotBeNull();
            capturedCells!.Should().BeEmpty();
            result.Messages.Should().ContainSingle(m =>
                m.Severity == MessageSeverity.Warning
                && m.Text.StartsWith("G2:")
                && m.Text.Contains("could not be resolved"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // Mixed DV'd and no-DV targets in one run: proves the blind-copy path for a no-DV target cell is
    // unchanged (still injects unconditionally) alongside the new gated behaviour for a DV'd target.
    [Fact]
    public async Task ExecuteAsync_MixedDvAndNoDvTargets_GatesOnlyTheDvdCell()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(src, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B2"] = new("Yes", null, null, null),
                    ["B3"] = new("anything at all", null, null, null),
                });
            reader.ReadCells(tpl, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["G2"] = new(null, "List", "\"Yes,No\"", null),
                    // G3 intentionally absent → no DV rule on the target cell.
                });

            IReadOnlyList<CellWriteEntry>? capturedCells = null;
            var writer = Substitute.For<IExcelTemplateWriter>();
            writer.When(w => w.Populate(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>()))
                .Do(ci => capturedCells = ci.ArgAt<IReadOnlyList<CellWriteEntry>>(2));

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2;B3->G3",
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            capturedCells.Should().NotBeNull();
            capturedCells!.Should().BeEquivalentTo(new[]
            {
                new CellWriteEntry(Row: 2, Column: "G", Value: "Yes", TypedValue: null),
                new CellWriteEntry(Row: 3, Column: "G", Value: "anything at all", TypedValue: null),
            });
            result.Messages.Should().NotContain(m => m.Severity == MessageSeverity.Warning);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Cancellation propagates ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "source.xlsx");
            var tpl = Path.Combine(dir, "template.xlsx");
            CreateEmptyWorkbook(src);
            CreateEmptyWorkbook(tpl);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var ctx = MakeCtx(dir,
                new Dictionary<string, string>
                {
                    ["mappings"]        = "B2->G2",
                    ["sourceSheetName"] = "Sheet1",
                    ["targetSheetName"] = "Sheet1",
                },
                inputs: new Dictionary<string, string>
                {
                    ["source"]         = src,
                    ["targetTemplate"] = tpl,
                });

            Func<Task> act = () => MakeTask().ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
