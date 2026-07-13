namespace ItrqTool.Tasks.Shared;

/// <summary>Carries a target-DV key and its payload as one element through DvRangeRefResolver,
/// so key and payload cannot be derived from separate enumerations (BLG-0054).</summary>
public sealed record KeyedTargetDv<TKey>(TKey Key, TargetDvInfo Info) where TKey : notnull;
