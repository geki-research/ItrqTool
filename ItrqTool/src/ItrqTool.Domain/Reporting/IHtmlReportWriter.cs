namespace ItrqTool.Domain.Reporting;

public interface IHtmlReportWriter
{
    /// <summary>
    /// Generates a self-contained HTML report and writes it to
    /// filePath. Overwrites if the file already exists.
    /// Creates the directory if it does not exist.
    /// </summary>
    void WriteReport(HtmlDiffReportData data, string filePath);
}
