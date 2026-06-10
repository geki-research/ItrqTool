using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.FeedbackChecklist;

/// <summary>
/// External configuration for the feedback-checklist assembler task. Carries the
/// checklist template location and the layout the writer needs: which worksheet to
/// populate, the first data row, and the <see cref="ChecklistColumn"/> → column-letter map.
/// </summary>
/// <remarks>
/// Deserialized with <c>JsonStringEnumConverter</c> registered, so <see cref="ColumnMap"/>
/// is keyed by the exact <see cref="ChecklistColumn"/> member name (e.g. <c>"Counter": "A"</c>).
/// A relative <see cref="TemplatePath"/> is resolved against <c>AppContext.BaseDirectory</c>
/// by the task; absolute paths are used as-is.
/// </remarks>
public sealed class FeedbackChecklistConfig
{
    public string TemplatePath { get; init; } = "";
    public string SheetName { get; init; } = "";
    public int DataStartRow { get; init; }
    public IReadOnlyDictionary<ChecklistColumn, string> ColumnMap { get; init; }
        = new Dictionary<ChecklistColumn, string>();
}
