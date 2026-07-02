namespace ItrqTool.Tasks.ControlLevelQuestionInject;

/// <summary>
/// Carries a v01 target answer cell's FULL data-validation rule (type, operator, both
/// formulas, and resolved List vocabulary once known) keyed by the v01 <c>RowNumber</c>.
/// <para>
/// BL-053 P4c-C1 (additive, read-phase only): populated by
/// <see cref="ControlLevelQuestionInjectV02ToV01Task.BuildTargetHLookup"/> and threaded through
/// to <see cref="ClqInjectMapper.Map"/>, but the mapper's carry-forward decision is unchanged
/// this phase — all fields are populated-but-unread until a later phase wires
/// <c>InjectionValueGuard</c> into the answer-mapping decision.
/// </para>
/// <para>
/// Mirrors <c>RlqTargetDvHolder</c> (keyed by v02 <c>RowNumber</c> there; keyed by v01
/// <c>RowNumber</c> here) so <c>DvRangeRefResolver.Resolve&lt;T&gt;</c> can be reused as-is for
/// the range-ref / named-range List resolution pass.
/// </para>
/// </summary>
public sealed record ClqTargetDvHolder(
    int RowNumber,
    string? Type,
    string? Operator,
    string? Formula,
    string? Formula2,
    IReadOnlyList<string>? ListValues);
