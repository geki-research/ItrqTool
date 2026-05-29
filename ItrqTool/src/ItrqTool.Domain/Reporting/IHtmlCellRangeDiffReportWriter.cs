namespace ItrqTool.Domain.Reporting;

public interface IHtmlCellRangeDiffReportWriter
{
    void WriteReport(HtmlDiffCellRangeReportData data, string filePath);
}
