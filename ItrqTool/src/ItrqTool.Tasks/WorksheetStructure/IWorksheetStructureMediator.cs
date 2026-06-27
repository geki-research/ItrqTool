namespace ItrqTool.Tasks.WorksheetStructure;

/// <summary>
/// The worksheet-structure oracle: asserts that <paramref name="filePath"/>'s worksheet really IS the
/// version identified by <paramref name="schema"/>, so a wrong-version sheet fails loudly instead of being
/// silently misread by a position-based parser. Returns one of three outcomes (Match / Mismatch / AssetError)
/// and NEVER throws for an asset problem — a missing/malformed/unsupported schema becomes AssetError.
/// </summary>
public interface IWorksheetStructureMediator
{
    WorksheetStructureResult Verify(string filePath, WorksheetSchemaRef schema);
}
