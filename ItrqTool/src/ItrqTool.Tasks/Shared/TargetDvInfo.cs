namespace ItrqTool.Tasks.Shared;

/// <summary>
/// Unified target-cell DV payload for RLQ/GD/CLQ inject and CellRangeInject (BLG-0054); the key
/// (row number, anchor row, or A1 address) lives on each consumer's own dictionary, not here.
/// </summary>
public sealed record TargetDvInfo(
    string? Type,
    string? Operator,
    string? Formula,
    string? Formula2,
    IReadOnlyList<string>? ListValues);
