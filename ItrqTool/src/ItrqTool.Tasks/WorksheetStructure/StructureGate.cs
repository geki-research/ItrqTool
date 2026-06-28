using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.WorksheetStructure;

public sealed record StructureGateResult(
    bool AssetFailed,
    string? AssetErrorReason,
    IReadOnlyList<ValidationFinding> StructureFindings);

public static class StructureGate
{
    /// Each input = a path tagged with the (q,v) it must match. ANY AssetError short-circuits
    /// (first wins) → AssetFailed; otherwise ALL Mismatch findings are collected. Reader/IO
    /// exceptions from mediator.Verify PROPAGATE unchanged (the task's existing concern).
    public static StructureGateResult VerifyAll(
        IWorksheetStructureMediator mediator,
        IEnumerable<(string Path, WorksheetSchemaRef Schema)> inputs)
    {
        var findings = new List<ValidationFinding>();
        foreach (var (path, schema) in inputs)
        {
            var r = mediator.Verify(path, schema);
            if (r.Outcome == WorksheetStructureOutcome.AssetError)
                return new StructureGateResult(true, r.AssetErrorReason, []);   // first asset error wins
            if (r.Outcome == WorksheetStructureOutcome.Mismatch)
                findings.AddRange(r.Findings);                                   // collect ALL mismatches
        }
        return new StructureGateResult(false, null, findings);
    }
}
