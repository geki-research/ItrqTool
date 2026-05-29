namespace ItrqTool.Tasks.Shared;

public static class CfComparer
{
    public static bool IsCfChanged(
        string? oldType, string? oldOp, string? oldValue, string? oldValue2,
        string? newType, string? newOp, string? newValue, string? newValue2)
    {
        return !string.Equals(oldType,   newType,   StringComparison.Ordinal)
            || !string.Equals(oldOp,     newOp,     StringComparison.Ordinal)
            || !string.Equals(oldValue,  newValue,  StringComparison.Ordinal)
            || !string.Equals(oldValue2, newValue2, StringComparison.Ordinal);
    }
}
