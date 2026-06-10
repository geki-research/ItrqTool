namespace ItrqTool.Domain.Validation;

public interface IFeedbackChecklistWriter
{
    void Populate(
        IReadOnlyList<FeedbackChecklistRow>  rows,
        string                               templatePath,
        string                               outputPath,
        FeedbackChecklistWriterOptions       options);
}
