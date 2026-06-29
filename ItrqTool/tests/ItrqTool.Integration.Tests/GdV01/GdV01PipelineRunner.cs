using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using Microsoft.Extensions.Logging.Abstractions;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Shared direct-pipeline run-helper for GD C3 tests. Calls the pipeline directly
/// (parser → patcher → aligner → <see cref="ValidationPipeline.RunFromAlignedGated{T}"/>)
/// without going through a task or serializing a report file. Matches PART C2 of the
/// 2026-06-26 prep-recon verbatim. Requires a real <see cref="ClosedXmlExcelStructureReader"/>
/// (hence living in <c>ItrqTool.Integration.Tests</c>, not <c>ItrqTool.Tasks.Tests</c>).
/// </summary>
public static class GdV01PipelineRunner
{
    /// <summary>
    /// Runs the full GD_v01 validation pipeline on the three supplied workbook files and
    /// returns the <see cref="ValidationRunResult"/> directly (no JSON serialization).
    /// </summary>
    public static ValidationRunResult Run(
        GdV01Config config,
        string currentPath,
        string templatePath,
        string previousPath)
    {
        var profile  = GdV01Profile.Build(config);
        var reader   = new ClosedXmlExcelStructureReader(
            NullLogger<ClosedXmlExcelStructureReader>.Instance);
        var messages = new List<TaskMessage>();

        // Read raw rows from each workbook.
        IReadOnlyList<ExcelRowStructure> curRows = reader.ReadRows(currentPath,  config.SheetName);
        IReadOnlyList<ExcelRowStructure> tmpRows = reader.ReadRows(templatePath, config.SheetName);
        IReadOnlyList<ExcelRowStructure> prvRows = reader.ReadRows(previousPath, config.SheetName);

        // Parse each workbook into GdV01ParseResult.
        GdV01ParseResult cur = GdV01QuestionParser.Parse(curRows, profile.Layout, config, messages);
        GdV01ParseResult tmp = GdV01QuestionParser.Parse(tmpRows, profile.Layout, config, messages);
        GdV01ParseResult prv = GdV01QuestionParser.Parse(prvRows, profile.Layout, config, messages);

        // ── GD-local section-header gate (PRE-align, fail-loud, short-circuit) ──
        // If any declared section's header diverged from its ExpectedName in any workbook, emit Fatal
        // findings and halt WITHOUT running the pipeline chain. This is the exact composition the GD-D
        // task will mirror. Uses the same (empty here) SeverityOverrides the pipeline run below uses.
        var severityOverrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal);
        var headerFindings = GdSectionHeaderGate.Verify(cur, tmp, prv, severityOverrides);
        if (headerFindings.Count > 0)
            return new ValidationRunResult(headerFindings, Halted: true);

        // Patch DV fields onto each workbook's parsed questions.
        var patchedCurrentQs  = GdDvPatcher.Patch(reader, currentPath,  config.SheetName, config, cur.Questions);
        var patchedTemplateQs = GdDvPatcher.Patch(reader, templatePath, config.SheetName, config, tmp.Questions);
        var patchedPreviousQs = GdDvPatcher.Patch(reader, previousPath, config.SheetName, config, prv.Questions);

        // Wrap patched lists back into GdV01ParseResult (preserving Malformed + header mismatches
        // from parse — though if any mismatch existed the gate above already short-circuited).
        var patchedCurrent  = new GdV01ParseResult(patchedCurrentQs,  cur.Malformed, cur.SectionHeaderMismatches);
        var patchedTemplate = new GdV01ParseResult(patchedTemplateQs, tmp.Malformed, tmp.SectionHeaderMismatches);
        var patchedPrevious = new GdV01ParseResult(patchedPreviousQs, prv.Malformed, prv.SectionHeaderMismatches);

        // Align.
        AlignmentResult<GdV01Question> alignment = GdV01Aligner.Align(
            patchedCurrent, patchedTemplate, patchedPrevious);

        // Run the gated pipeline and return the result directly.
        return ValidationPipeline.RunFromAlignedGated(
            alignment, profile,
            new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            messages, CancellationToken.None);
    }
}
