using System.Text.RegularExpressions;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;

namespace ItrqTool.Tasks.RiskLevelQuestionInjectED;

/// <summary>
/// Configuration for the ED RLQ inject task (v01 → v02 reference injection). A fork of
/// <c>RlqInjectConfig</c>: the two validation-config filenames (whose column letters are read at
/// run time from the referenced standard-shape <see cref="RlqV01Config"/> / <see cref="RlqV02Config"/>
/// assets), plus the four ED-only answer columns those shared shapes cannot carry — the v01
/// CURRENT Holding / EBOe answers (the injection sources) and the v02 PREVIOUS Holding / EBOe
/// answers (the injection targets). Amendment A3 adds the v02 CURRENT Holding / EBOe columns
/// (<see cref="TargetHoldingDvColumn"/> / <see cref="TargetEBOeDvColumn"/>): their data-validation
/// rule governs the corresponding write, mirroring the standard task, which judges the G write
/// against H's DV. They are DV-read only — never written. The v01 previous Holding / EBOe columns
/// are never read or written, so they have no keys here.
/// </summary>
public sealed class RlqEDInjectConfig
{
    public string CurrentConfigFilename { get; init; } = "";
    public string PreviousConfigFilename { get; init; } = "";

    public string SourceHoldingAnswerColumn { get; init; } = "";   // ED v01 current Holding answer
    public string SourceEBOeAnswerColumn { get; init; } = "";      // ED v01 current EBOe answer
    public string TargetHoldingAnswerColumn { get; init; } = "";   // ED v02 previous Holding answer
    public string TargetEBOeAnswerColumn { get; init; } = "";      // ED v02 previous EBOe answer
    public string TargetHoldingDvColumn { get; init; } = "";       // ED v02 current Holding answer (DV source)
    public string TargetEBOeDvColumn { get; init; } = "";          // ED v02 current EBOe answer (DV source)

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(CurrentConfigFilename))
            errors.Add("CurrentConfigFilename must not be empty.");

        if (string.IsNullOrWhiteSpace(PreviousConfigFilename))
            errors.Add("PreviousConfigFilename must not be empty.");

        ValidateColumnLetter(nameof(SourceHoldingAnswerColumn), SourceHoldingAnswerColumn, errors);
        ValidateColumnLetter(nameof(SourceEBOeAnswerColumn), SourceEBOeAnswerColumn, errors);
        ValidateColumnLetter(nameof(TargetHoldingAnswerColumn), TargetHoldingAnswerColumn, errors);
        ValidateColumnLetter(nameof(TargetEBOeAnswerColumn), TargetEBOeAnswerColumn, errors);
        ValidateColumnLetter(nameof(TargetHoldingDvColumn), TargetHoldingDvColumn, errors);
        ValidateColumnLetter(nameof(TargetEBOeDvColumn), TargetEBOeDvColumn, errors);

        // Same comparer as RlqV02Config's distinctness check: blank values excluded, then
        // ToUpperInvariant + ordinal equality.
        if (SameLetter(SourceHoldingAnswerColumn, SourceEBOeAnswerColumn))
            errors.Add($"{nameof(SourceHoldingAnswerColumn)} and {nameof(SourceEBOeAnswerColumn)} must be distinct.");

        // Every pair of the four v02 target letters, in declaration order.
        var targets = new (string Name, string Value)[]
        {
            (nameof(TargetHoldingAnswerColumn), TargetHoldingAnswerColumn),
            (nameof(TargetEBOeAnswerColumn), TargetEBOeAnswerColumn),
            (nameof(TargetHoldingDvColumn), TargetHoldingDvColumn),
            (nameof(TargetEBOeDvColumn), TargetEBOeDvColumn),
        };
        for (var i = 0; i < targets.Length; i++)
            for (var j = i + 1; j < targets.Length; j++)
                if (SameLetter(targets[i].Value, targets[j].Value))
                    errors.Add($"{targets[i].Name} and {targets[j].Name} must be distinct.");

        return errors;
    }

    /// <summary>
    /// Pure cross-file check: every ED letter that equals a column letter of the standard-shape
    /// config on the same side (sources vs v01, targets vs v02). One message per collision, in
    /// property-declaration order; empty when there is none.
    /// </summary>
    public IReadOnlyList<string> CollisionsWith(RlqV01Config previous, RlqV02Config current)
    {
        var previousColumns = new (string Name, string Value)[]
        {
            (nameof(RlqV01Config.QuestionNumberColumn), previous.QuestionNumberColumn),
            (nameof(RlqV01Config.TextColumn), previous.TextColumn),
            (nameof(RlqV01Config.GuidanceColumn), previous.GuidanceColumn),
            (nameof(RlqV01Config.RequestedTypeColumn), previous.RequestedTypeColumn),
            (nameof(RlqV01Config.PreviousAnswerColumn), previous.PreviousAnswerColumn),
            (nameof(RlqV01Config.AnswerColumn), previous.AnswerColumn),
            (nameof(RlqV01Config.RequestedExplanationColumn), previous.RequestedExplanationColumn),
            (nameof(RlqV01Config.PreviousExplanationColumn), previous.PreviousExplanationColumn),
            (nameof(RlqV01Config.CurrentExplanationColumn), previous.CurrentExplanationColumn),
            (nameof(RlqV01Config.MaterialChangeColumn), previous.MaterialChangeColumn),
            (nameof(RlqV01Config.ProvidedByColumn), previous.ProvidedByColumn),
            (nameof(RlqV01Config.XrefIdColumn), previous.XrefIdColumn),
        };

        var currentColumns = new (string Name, string Value)[]
        {
            (nameof(RlqV02Config.QuestionNumberColumn), current.QuestionNumberColumn),
            (nameof(RlqV02Config.TextColumn), current.TextColumn),
            (nameof(RlqV02Config.GuidanceColumn), current.GuidanceColumn),
            (nameof(RlqV02Config.RequestedTypeColumn), current.RequestedTypeColumn),
            (nameof(RlqV02Config.PreviousAnswerColumn), current.PreviousAnswerColumn),
            (nameof(RlqV02Config.AnswerColumn), current.AnswerColumn),
            (nameof(RlqV02Config.RequestedExplanationColumn), current.RequestedExplanationColumn),
            (nameof(RlqV02Config.PreviousExplanationColumn), current.PreviousExplanationColumn),
            (nameof(RlqV02Config.CurrentExplanationColumn), current.CurrentExplanationColumn),
            (nameof(RlqV02Config.MaterialChangeColumn), current.MaterialChangeColumn),
            (nameof(RlqV02Config.HowExplanationColumn), current.HowExplanationColumn),
            (nameof(RlqV02Config.ProvidedByColumn), current.ProvidedByColumn),
            (nameof(RlqV02Config.XrefIdColumn), current.XrefIdColumn),
        };

        var collisions = new List<string>();

        AddCollisions(nameof(SourceHoldingAnswerColumn), SourceHoldingAnswerColumn, nameof(RlqV01Config), previousColumns, collisions);
        AddCollisions(nameof(SourceEBOeAnswerColumn), SourceEBOeAnswerColumn, nameof(RlqV01Config), previousColumns, collisions);
        AddCollisions(nameof(TargetHoldingAnswerColumn), TargetHoldingAnswerColumn, nameof(RlqV02Config), currentColumns, collisions);
        AddCollisions(nameof(TargetEBOeAnswerColumn), TargetEBOeAnswerColumn, nameof(RlqV02Config), currentColumns, collisions);
        AddCollisions(nameof(TargetHoldingDvColumn), TargetHoldingDvColumn, nameof(RlqV02Config), currentColumns, collisions);
        AddCollisions(nameof(TargetEBOeDvColumn), TargetEBOeDvColumn, nameof(RlqV02Config), currentColumns, collisions);

        return collisions;
    }

    private static void AddCollisions(
        string edProperty,
        string letter,
        string otherConfig,
        (string Name, string Value)[] otherColumns,
        List<string> collisions)
    {
        foreach (var (name, value) in otherColumns)
            if (SameLetter(letter, value))
                collisions.Add(
                    $"Column letter collision: {nameof(RlqEDInjectConfig)}.{edProperty} '{letter}' equals {otherConfig}.{name}.");
    }

    private static bool SameLetter(string a, string b)
        => !string.IsNullOrWhiteSpace(a)
           && !string.IsNullOrWhiteSpace(b)
           && a.ToUpperInvariant() == b.ToUpperInvariant();

    private static readonly Regex ColumnLetterPattern = new(@"^[A-Za-z]+$", RegexOptions.Compiled);

    private static void ValidateColumnLetter(string propertyName, string value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{propertyName} must not be empty.");
        else if (!ColumnLetterPattern.IsMatch(value))
            errors.Add($"{propertyName} must be a valid column letter (e.g. 'A', 'AA').");
    }
}
