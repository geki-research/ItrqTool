namespace ItrqTool.Tasks.QuestionnaireValidation;

using ItrqTool.Domain;                                   // IExcelStructureReader, ExcelCellStructure
using ItrqTool.Domain.Validation;                        // ValidationFinding, FindingEvaluation
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;  // AlignmentResult<T>, IAlignmentIdentity
using ItrqTool.Tasks.QuestionnaireValidation.Checks;     // IExtensionCheck<T>
using ItrqTool.Tasks.QuestionnaireValidation.Findings;   // FindingDescriptor, FindingEmitter
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;    // QuestionnaireLayout, QuestionRowContext

// The version-specific composition the generic pipeline cannot know itself. Built by the
// per-version task (chunk G) from its typed config. Carries NO reader, NO SeverityOverrides,
// NO TaskType/output path. The baseline is bridged via descriptors + a runner delegate so the
// generic pipeline never references ClqBaselineFindings / ClqBaselineRoleMap / IClqBaselineConfig.
public sealed record ValidationPipelineProfile<T>(
    string SheetName,
    QuestionnaireLayout Layout,
    Func<QuestionRowContext, T> RecordFactory,
    IReadOnlyList<(string Column, Func<T, ExcelCellStructure, T> ApplyDv)> DvRoles,
    IReadOnlyList<FindingDescriptor> BaselineDescriptors,
    Func<AlignmentResult<T>, FindingEmitter, IReadOnlyList<ValidationFinding>> RunBaseline,
    IReadOnlyList<IExtensionCheck<T>> Extensions
) where T : class, IAlignmentIdentity;
