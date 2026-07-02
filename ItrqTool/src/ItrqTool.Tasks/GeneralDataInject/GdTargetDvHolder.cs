namespace ItrqTool.Tasks.GeneralDataInject;

/// <summary>
/// Carries a v02 target answer cell's FULL data-validation rule (type, operator, both
/// formulas, and resolved List vocabulary once known) keyed by the v02 answer <c>AnchorRow</c>.
/// <para>
/// BL-053 P4b-G1 (additive, read-phase only): populated by
/// <see cref="GeneralDataInjectV01ToV02Task.BuildTargetHLookup"/> and threaded through
/// to <see cref="GdInjectMapper.Map"/>, but the mapper's decision logic still reads only
/// <see cref="Type"/> (via a local <c>tgtCat</c> derivation) — byte-equivalent to today's
/// category-only lookup. <see cref="Operator"/>, <see cref="Formula"/>, <see cref="Formula2"/>,
/// and <see cref="ListValues"/> are populated-but-unread until a later phase wires the
/// injection value guard into the answer-mapping decision.
/// </para>
/// <para>
/// GD-local mirror of <c>RlqTargetDvHolder</c> (unification across the two inject stacks is
/// backlogged), keyed by answer <c>AnchorRow</c> — the per-answer-grain analog of RLQ's
/// per-question-row keying — so <c>DvRangeRefResolver.Resolve&lt;T&gt;</c> can be reused as-is
/// for the range-ref / named-range List resolution pass.
/// </para>
/// </summary>
public sealed record GdTargetDvHolder(
    int AnchorRow,
    string? Type,
    string? Operator,
    string? Formula,
    string? Formula2,
    IReadOnlyList<string>? ListValues);
