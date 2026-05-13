namespace ItrqTool.Domain;

public record TaskNode(
    string Id,
    string TaskType,
    IReadOnlyDictionary<string, TaskOutputRef> Inputs,
    IReadOnlyDictionary<string, string> OutputFileNames
);
