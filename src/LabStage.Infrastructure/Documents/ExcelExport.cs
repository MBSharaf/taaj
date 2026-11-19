using ClosedXML.Excel;

namespace LabStage.Infrastructure.Documents;

/// <summary>One right-to-left Excel sheet with a styled header row (Arabic headers, data as given).</summary>
public static class ExcelExport
{
    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IEnumerable<object?[]> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName.Length > 31 ? sheetName[..31] : sheetName);
        ws.RightToLeft = true;
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0D2A78");
        }
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                    case int i: cell.Value = i; break;
                    case long l: cell.Value = l; break;
                    case bool b: cell.Value = b ? "نعم" : "لا"; break;
                    case DateTimeOffset dt: cell.Value = dt.ToOffset(TimeSpan.FromHours(2)).DateTime; cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm"; break;
                    default: cell.Value = row[c]!.ToString(); break;
                }
            }
            r++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
