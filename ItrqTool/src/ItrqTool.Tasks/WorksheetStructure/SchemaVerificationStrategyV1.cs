using System.Text;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.WorksheetStructure;

/// <summary>
/// The one shipped verification strategy (schemaFormatVersion = 1). For each declared column it reads the
/// header cell at <c>{Column}{Row ?? HeaderRow}</c>, normalizes it (<see cref="HeaderNormalization"/>), and
/// matches iff it equals the normalized CanonicalHeader OR any normalized AcceptedVariant. All non-matching
/// columns are aggregated into ONE Fatal structure finding per workbook (the conceptual id
/// <c>structure.unexpected-worksheet-structure</c> leads the CheckResult text — the finding record has no id).
/// </summary>
public sealed class SchemaVerificationStrategyV1 : ISchemaVerificationStrategy
{
    public const string FindingId = "structure.unexpected-worksheet-structure";

    public int SchemaFormatVersion => 1;

    public WorksheetStructureResult Verify(
        WorksheetStructureSchema schema,
        IReadOnlyDictionary<string, ExcelCellStructure> headerCells,
        string filePath)
    {
        var offending = new List<(string Address, string Expected, string Actual)>();

        foreach (var col in schema.Columns)
        {
            var address = $"{col.Column}{col.Row ?? schema.HeaderRow}".ToUpperInvariant();
            var actual = headerCells.TryGetValue(address, out var cell) ? cell.TextValue : null;

            if (!Matches(actual, col))
                offending.Add((address, col.CanonicalHeader, actual ?? ""));
        }

        if (offending.Count == 0)
            return WorksheetStructureResult.Match();

        var addresses = string.Join(", ", offending.Select(o => o.Address));

        var sb = new StringBuilder();
        sb.Append(FindingId)
          .Append(": worksheet '").Append(schema.SheetName)
          .Append("' does not match the expected ")
          .Append(schema.Questionnaire).Append('-').Append(schema.QuestionnaireVersion)
          .Append(" structure. ").Append(offending.Count)
          .Append(offending.Count == 1 ? " column header differs:" : " column headers differ:");
        foreach (var (addr, expected, act) in offending)
            sb.Append(' ').Append(addr)
              .Append(" expected [").Append(expected)
              .Append("] but found [").Append(act).Append("];");

        var finding = new ValidationFinding(
            Check: ValidationCheck.Structure,
            Evaluation: FindingEvaluation.Fatal,
            CellAddresses: addresses,
            QuestionNumber: null,
            QuestionText: null,
            RequestedData: null,
            ProvidedBy: null,
            CheckResult: sb.ToString());

        return WorksheetStructureResult.Mismatch([finding]);
    }

    private static bool Matches(string? actual, WorksheetSchemaColumn col)
    {
        var normActual = HeaderNormalization.Normalize(actual);
        if (normActual == HeaderNormalization.Normalize(col.CanonicalHeader))
            return true;
        if (col.AcceptedVariants is null)
            return false;
        foreach (var variant in col.AcceptedVariants)
            if (normActual == HeaderNormalization.Normalize(variant))
                return true;
        return false;
    }
}
