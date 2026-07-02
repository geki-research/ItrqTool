namespace ItrqTool.Tasks.RiskLevelQuestionInject;

/// <summary>
/// Carries a v02 target answer cell's FULL data-validation rule (type, operator, both
/// formulas, and resolved List vocabulary once known) keyed by the v02 <c>RowNumber</c>.
/// <para>
/// BL-053 P4b-R1 (additive, read-phase only): populated by
/// <see cref="RiskLevelQuestionInjectV01ToV02Task.BuildTargetHLookup"/> and threaded through
/// to <see cref="RlqInjectMapper.Map"/>, but the mapper's decision logic still reads only
/// <see cref="Type"/> (via a local <c>tgtCat</c> derivation) — byte-equivalent to today's
/// category-only lookup. <see cref="Operator"/>, <see cref="Formula"/>, <see cref="Formula2"/>,
/// and <see cref="ListValues"/> are populated-but-unread until R2 wires
/// <c>InjectionValueGuard</c> into the answer-mapping decision.
/// </para>
/// <para>
/// Mirrors <c>CellRangeInjectTask</c>'s private inline <c>TargetDvHolder</c> idiom (keyed by
/// A1 address there; keyed by <c>RowNumber</c> here, matching this task's existing
/// per-row-anchor lookup shape) so <c>DvRangeRefResolver.Resolve&lt;T&gt;</c> can be reused
/// as-is for the range-ref / named-range List resolution pass.
/// </para>
/// </summary>
public sealed record RlqTargetDvHolder(
    int RowNumber,
    string? Type,
    string? Operator,
    string? Formula,
    string? Formula2,
    IReadOnlyList<string>? ListValues);
