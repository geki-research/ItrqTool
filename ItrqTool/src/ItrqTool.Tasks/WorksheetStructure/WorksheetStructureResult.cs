using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.WorksheetStructure;

/// <summary>
/// The three deliberately-distinct outcomes of a structure verification (do NOT collapse — lesson 149):
/// <list type="bullet">
/// <item><see cref="Match"/> — the worksheet IS the expected version (no finding).</item>
/// <item><see cref="Mismatch"/> — the worksheet deviates: a Fatal structure finding (this is DATA — the
/// consuming task still returns Succeeded:true).</item>
/// <item><see cref="AssetError"/> — the shipped schema is missing/unreadable or its schemaFormatVersion is
/// unsupported: a DEPLOYMENT defect that drives the consuming task to Succeeded:false.</item>
/// </list>
/// </summary>
public enum WorksheetStructureOutcome { Match, Mismatch, AssetError }

/// <summary>
/// Result of <see cref="IWorksheetStructureMediator.Verify"/>. On <see cref="WorksheetStructureOutcome.Mismatch"/>,
/// <see cref="Findings"/> carries the Fatal structure finding(s); otherwise it is empty. On
/// <see cref="WorksheetStructureOutcome.AssetError"/>, <see cref="AssetErrorReason"/> describes the deployment
/// defect; otherwise it is null.
/// </summary>
public sealed record WorksheetStructureResult(
    WorksheetStructureOutcome Outcome,
    IReadOnlyList<ValidationFinding> Findings,
    string? AssetErrorReason)
{
    private static readonly IReadOnlyList<ValidationFinding> None = [];

    public static WorksheetStructureResult Match() =>
        new(WorksheetStructureOutcome.Match, None, null);

    public static WorksheetStructureResult Mismatch(IReadOnlyList<ValidationFinding> findings) =>
        new(WorksheetStructureOutcome.Mismatch, findings, null);

    public static WorksheetStructureResult AssetError(string reason) =>
        new(WorksheetStructureOutcome.AssetError, None, reason);
}
