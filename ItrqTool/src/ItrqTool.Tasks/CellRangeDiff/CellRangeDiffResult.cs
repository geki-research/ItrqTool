using ItrqTool.Domain;

namespace ItrqTool.Tasks.CellRangeDiff;

public record CellRangeDiffResult(
    IReadOnlyList<ChangedCell> Changed,
    IReadOnlyList<UnchangedCell> Unchanged);

public record ChangedCell(
    string Address,
    ExcelCellStructure Cell1,
    ExcelCellStructure Cell2,
    bool TextChanged,
    bool DvChanged,
    bool CfChanged);

public record UnchangedCell(
    string Address,
    ExcelCellStructure Cell);
