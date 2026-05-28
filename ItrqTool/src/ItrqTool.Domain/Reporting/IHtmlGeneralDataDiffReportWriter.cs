namespace ItrqTool.Domain.Reporting;

public interface IHtmlGeneralDataDiffReportWriter
{
    /// <summary>
    /// Generates a self-contained HTML report for a General Data diff and writes
    /// it to filePath. Overwrites if the file exists. Creates the directory if it
    /// does not exist.
    /// </summary>
    void WriteReport(HtmlDiffGeneralDataReportData data, string filePath);
}
