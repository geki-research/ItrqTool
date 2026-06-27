namespace ItrqTool.Tasks.WorksheetStructure;

/// <summary>
/// Identifies WHICH shipped schema a caller expects an input worksheet to match. Resolves by
/// convention to schemas/&lt;Questionnaire&gt;-&lt;Version&gt;.structure.json (e.g. ("rlq","v01")).
/// </summary>
public readonly record struct WorksheetSchemaRef(string Questionnaire, string Version);
