using ClosedXML.Excel;

namespace ItrqTool.Infrastructure.Excel;

internal static class ExcelStyleHelper
{
    internal static void WriteWithStylePreservation(
        IXLWorksheet worksheet, IXLCell cell, string columnLetter, object value)
    {
        // Effective-style capture (lesson 59): cells that inherit from the column style
        // (no explicit XF) lose alignment when a value is written, because ClosedXML
        // materialises a bare default cell style. Capture the effective alignment first,
        // then re-apply after the write.
        var ca = cell.Style.Alignment;
        bool cellExplicit = ca.Horizontal != XLAlignmentHorizontalValues.General
                         || ca.Vertical   != XLAlignmentVerticalValues.Bottom
                         || ca.WrapText   || ca.Indent != 0;

        XLAlignmentHorizontalValues hAlign;
        XLAlignmentVerticalValues   vAlign;
        bool wrap;
        int  indent;
        bool shrink;

        if (cellExplicit)
        {
            hAlign = ca.Horizontal;
            vAlign = ca.Vertical;
            wrap   = ca.WrapText;
            indent = ca.Indent;
            shrink = ca.ShrinkToFit;
        }
        else
        {
            var colStyle = worksheet.Column(columnLetter).Style.Alignment;
            hAlign = colStyle.Horizontal;
            vAlign = colStyle.Vertical;
            wrap   = colStyle.WrapText;
            indent = colStyle.Indent;
            shrink = colStyle.ShrinkToFit;
        }

        var numFmt = cell.Style.NumberFormat.Format;

        switch (value)
        {
            case int    n: cell.Value = n;            break;
            case string s: cell.Value = s;            break;
            default:       cell.Value = value.ToString(); break;
        }

        cell.Style.Alignment.Horizontal  = hAlign;
        cell.Style.Alignment.Vertical    = vAlign;
        cell.Style.Alignment.WrapText    = wrap;
        cell.Style.Alignment.Indent      = indent;
        cell.Style.Alignment.ShrinkToFit = shrink;
        if (!string.IsNullOrEmpty(numFmt))
            cell.Style.NumberFormat.Format = numFmt;
    }
}
