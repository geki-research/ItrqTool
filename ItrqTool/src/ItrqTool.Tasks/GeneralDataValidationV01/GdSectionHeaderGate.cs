using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;  // ValidationWorkbook
using ItrqTool.Tasks.QuestionnaireValidation.Findings;   // FindingDescriptor, FindingCatalogue, FindingEmitter

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// ── GdSectionHeaderGate — the GD-local, PRE-align, fail-loud section-header gate ──
//
// The GD analogue of the identity-integrity gate (MalformedKeyCheck), but at GD-composition altitude
// rather than inside the shared ValidationPipeline (which is frozen and has a single gate slot, taken
// by MalformedKeyCheck). The GD composition (GdV01PipelineRunner now, the GD-D task later) calls
// Verify AFTER parse and BEFORE align: if ANY declared section's actual column-D header diverged from
// its configured ExpectedName, the composition returns ValidationRunResult(findings, Halted:true)
// WITHOUT running the pipeline chain — a wrong section header invalidates the section-anchored L/qid
// semantics, so proceeding would produce unreliable downstream findings.
//
// The descriptor is ALSO registered in the pipeline catalogue via GdV01Profile.BaselineDescriptors
// (the same concat path the identity-gate descriptor uses), so the id resolves and SeverityOverrides
// can target it even on the clean path where the gate does not fire. It is NOT a profile Extension —
// the GD extension count stays 10.
public static class GdSectionHeaderGate
{
    public const string MismatchId = "structure.section-header-mismatch";

    /// <summary>The single GD section-header finding descriptor. Default severity Fatal (the gate
    /// halts), ValidationCheck.Structure (the workbook is the untrusted artifact deviating).</summary>
    public static FindingDescriptor Descriptor { get; } = new(
        MismatchId, FindingEvaluation.Fatal, ValidationCheck.Structure,
        "A declared section's header text in a workbook does not match its expected name, so the " +
        "section cannot be reliably anchored within or across years.");

    /// <summary>
    /// Produces one Fatal <see cref="ValidationFinding"/> per declared-section header mismatch across
    /// all three parsed workbooks (current, then template, then previous; row order within each). The
    /// mismatches were detected at parse time and carried in each
    /// <see cref="GdV01ParseResult.SectionHeaderMismatches"/>. Returns empty when every declared header
    /// matched — the GD composition then proceeds to the pipeline.
    /// </summary>
    public static IReadOnlyList<ValidationFinding> Verify(
        GdV01ParseResult current,
        GdV01ParseResult template,
        GdV01ParseResult previous,
        IReadOnlyDictionary<string, FindingEvaluation> severityOverrides)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(severityOverrides);

        var emitter = new FindingEmitter(severityOverrides, new FindingCatalogue([Descriptor]));

        var findings = new List<ValidationFinding>();
        EmitFor(findings, emitter, current,  ValidationWorkbook.CurrentResponse);
        EmitFor(findings, emitter, template, ValidationWorkbook.EmptyTemplate);
        EmitFor(findings, emitter, previous, ValidationWorkbook.PreviousResponse);
        return findings;
    }

    private static void EmitFor(
        List<ValidationFinding> findings, FindingEmitter emitter,
        GdV01ParseResult parsed, ValidationWorkbook workbook)
    {
        foreach (var m in parsed.SectionHeaderMismatches)
        {
            var cell = $"{m.NameColumn}{m.HeaderRow}";
            var actual = m.ActualName is null ? "blank" : $"'{m.ActualName}'";
            findings.Add(emitter.Emit(MismatchId,
                cell, questionNumber: null, questionText: null, requestedData: null, providedBy: null,
                $"Section header in {WorkbookName(workbook)} at {cell} is {actual}; expected " +
                $"'{m.ExpectedName}'. The section cannot be reliably anchored."));
        }
    }

    private static string WorkbookName(ValidationWorkbook w) => w switch
    {
        ValidationWorkbook.CurrentResponse  => "the current response",
        ValidationWorkbook.EmptyTemplate    => "the empty template",
        ValidationWorkbook.PreviousResponse => "the previous response",
        _ => w.ToString()
    };
}
