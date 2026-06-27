namespace ItrqTool.Tasks.WorksheetStructure;

/// <summary>
/// A shipped, per-(questionnaire, version) structure schema: the trusted oracle that asserts an
/// input worksheet really IS the version a task expects. Authored from the real template bytes
/// and resolved by convention (schemas/&lt;q&gt;-&lt;version&gt;.structure.json) — never derived from a
/// runtime input. Self-versioned by <see cref="SchemaFormatVersion"/> (the strategy seam).
/// </summary>
public sealed record WorksheetStructureSchema(
    int SchemaFormatVersion,
    string Questionnaire,
    string QuestionnaireVersion,
    string SheetName,
    int HeaderRow,
    IReadOnlyList<WorksheetSchemaColumn> Columns);

/// <summary>
/// One column's header expectation. <paramref name="Column"/> is the letter (e.g. "H");
/// <paramref name="Row"/> optionally overrides the schema's <see cref="WorksheetStructureSchema.HeaderRow"/>
/// (for a two-row header). <paramref name="CanonicalHeader"/> is the EXACT real-template header string
/// (blank is allowed). <paramref name="AcceptedVariants"/> is a deterministic allow-list (NOT fuzzy /
/// NOT string-distance) of additional acceptable headers; null/absent means "canonical only".
/// </summary>
public sealed record WorksheetSchemaColumn(
    string Column,
    int? Row,
    string CanonicalHeader,
    IReadOnlyList<string>? AcceptedVariants);
