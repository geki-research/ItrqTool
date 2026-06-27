using ItrqTool.Domain;

namespace ItrqTool.Tasks.WorksheetStructure;

/// <summary>
/// Default <see cref="IWorksheetStructureMediator"/>. Loads the shipped schema by convention, selects the
/// verification strategy by <c>schemaFormatVersion</c>, reads the header cells via
/// <see cref="IExcelStructureReader.ReadCells"/>, and delegates the pure comparison to the strategy.
/// <para>
/// Channel discipline (lesson 149): only a SCHEMA problem (missing/unreadable/unparseable file, or an
/// unsupported schemaFormatVersion) becomes <see cref="WorksheetStructureOutcome.AssetError"/>. A broken
/// INPUT workbook is the calling task's existing concern — reader/IO exceptions are intentionally NOT
/// caught here, so they propagate exactly as they do today.
/// </para>
/// </summary>
public sealed class WorksheetStructureMediator : IWorksheetStructureMediator
{
    private readonly IExcelStructureReader _reader;
    private readonly WorksheetStructureSchemaLoader _loader;
    private readonly IReadOnlyList<ISchemaVerificationStrategy> _strategies;
    private readonly string _schemasBaseDir;

    public WorksheetStructureMediator(
        IExcelStructureReader reader,
        WorksheetStructureSchemaLoader loader,
        IEnumerable<ISchemaVerificationStrategy> strategies,
        string schemasBaseDir)
    {
        _reader = reader;
        _loader = loader;
        _strategies = strategies.ToList();
        _schemasBaseDir = schemasBaseDir;
    }

    public WorksheetStructureResult Verify(string filePath, WorksheetSchemaRef schema)
    {
        WorksheetStructureSchema loaded;
        try
        {
            loaded = _loader.Load(schema, _schemasBaseDir);
        }
        catch (WorksheetSchemaLoadException ex)
        {
            return WorksheetStructureResult.AssetError(ex.Message);
        }

        var strategy = _strategies.SingleOrDefault(s => s.SchemaFormatVersion == loaded.SchemaFormatVersion);
        if (strategy is null)
            return WorksheetStructureResult.AssetError(
                $"Unsupported schemaFormatVersion {loaded.SchemaFormatVersion} " +
                $"for {schema.Questionnaire}-{schema.Version}.");

        var addresses = loaded.Columns
            .Select(c => $"{c.Column}{c.Row ?? loaded.HeaderRow}".ToUpperInvariant())
            .ToList();

        // Reader/IO exceptions are NOT caught — a broken input file is the task's concern, not an asset error.
        var headerCells = _reader.ReadCells(filePath, loaded.SheetName, addresses);

        return strategy.Verify(loaded, headerCells, filePath);
    }
}
