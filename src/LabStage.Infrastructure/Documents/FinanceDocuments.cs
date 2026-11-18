using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LabStage.Infrastructure.Documents;

/// <summary>One line of an invoice.</summary>
public sealed record InvoiceLine(string Description, int Quantity, decimal UnitPriceEgp, decimal TotalEgp);

/// <summary>A payment made on the order (shown on the invoice).</summary>
public sealed record InvoicePayment(string Label, decimal AmountEgp, string Status, DateTimeOffset? PaidAt);

/// <summary>
/// Data for an order invoice. Party = "Dentist" or "Lab": each version shows only that party's figures
/// (the lab version carries the platform commission and the lab's net; the dentist version does not).
/// </summary>
public sealed record InvoiceData(
    string Party, string Number, DateTimeOffset IssuedAt, string OrderNumber, string PatientName,
    string DentistName, string ClinicName, string LabName, IReadOnlyList<InvoiceLine> Lines, decimal TotalEgp,
    IReadOnlyList<InvoicePayment> Payments, decimal PaidEgp, decimal RemainingEgp,
    decimal? CommissionEgp, int? CommissionPercent, decimal? LabNetEgp, bool IsMockup);

public sealed record ReceiptData(
    string Party, string Number, DateTimeOffset PaidAt, string OrderNumber, string PatientName, string DentistName,
    string LabName, string MilestoneLabel, decimal AmountEgp, string Provider, string TransactionRef, bool TestMode,
    decimal RefundedEgp, decimal? CommissionEgp, int? CommissionPercent, decimal? LabNetEgp);

/// <summary>Arabic (right-to-left) PDF invoices and payment receipts.</summary>
public static class FinanceDocuments
{
    private const string Navy = "#0D2A78";
    private const string Ink = "#0B1A45";
    private const string Muted = "#5A6785";
    private const string Line = "#E6E9F1";
    private const string Panel = "#F4F6FA";
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    /// <summary>Arabic-capable font families to use (every one must exist). Override with the Documents:Fonts setting.</summary>
    public static string[] Fonts { get; set; } = OperatingSystem.IsWindows() ? ["Tahoma", "Arial"] : ["Noto Naskh Arabic"];

    static FinanceDocuments()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Arabic needs a font with Arabic glyphs: the server's fonts (Windows: Tahoma/Arial; Linux: install fonts-noto-core),
        // or .ttf files dropped in App_Data/fonts next to the app.
        QuestPDF.Settings.UseEnvironmentFonts = true;
        var local = Path.Combine(AppContext.BaseDirectory, "App_Data", "fonts");
        if (Directory.Exists(local)) QuestPDF.Settings.FontDiscoveryPaths.Add(local);
    }

    private static string Egp(decimal v) => v.ToString("#,0.00", En) + " ج.م";
    private static string Date(DateTimeOffset d) => d.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd HH:mm", En);

    public static byte[] Invoice(InvoiceData d) => Document.Create(c => c.Page(page =>
    {
        Setup(page);
        page.Header().Element(h => Header(h, d.Party == "Lab" ? "فاتورة الطلب — نسخة المعمل" : "فاتورة الطلب", d.Number, d.IssuedAt));
        page.Content().PaddingVertical(14).Column(col =>
        {
            col.Spacing(12);
            col.Item().Element(x => Parties(x, d.OrderNumber, d.PatientName, d.DentistName, d.ClinicName, d.LabName));
            if (d.IsMockup) col.Item().Text("Mockup — تصميم فقط").FontColor(Navy).Bold();

            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(5); cd.RelativeColumn(1.4f); cd.RelativeColumn(2.2f); cd.RelativeColumn(2.4f); });
                t.Header(h =>
                {
                    foreach (var head in new[] { "البند", "الكمية", "سعر الوحدة", "الإجمالي" })
                        h.Cell().Background(Navy).Padding(6).Text(head).FontColor(Colors.White).Bold();
                });
                foreach (var l in d.Lines)
                {
                    t.Cell().BorderBottom(1).BorderColor(Line).Padding(6).Text(l.Description);
                    t.Cell().BorderBottom(1).BorderColor(Line).Padding(6).Text(l.Quantity.ToString(En));
                    t.Cell().BorderBottom(1).BorderColor(Line).Padding(6).Text(Egp(l.UnitPriceEgp));
                    t.Cell().BorderBottom(1).BorderColor(Line).Padding(6).Text(Egp(l.TotalEgp));
                }
            });

            col.Item().AlignLeft().Width(260).Column(sum =>
            {
                Row(sum, "إجمالي الطلب", Egp(d.TotalEgp), bold: true);
                if (d.CommissionEgp is { } com) Row(sum, $"عمولة المنصة ({d.CommissionPercent}%)", "- " + Egp(com));
                if (d.LabNetEgp is { } net) Row(sum, "صافي المعمل", Egp(net), bold: true);
                Row(sum, "المدفوع", Egp(d.PaidEgp));
                Row(sum, "المتبقي", Egp(d.RemainingEgp), bold: true);
            });

            if (d.Payments.Count > 0)
            {
                col.Item().Text("جدول الدفعات").Bold().FontColor(Ink);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(4); cd.RelativeColumn(2.4f); cd.RelativeColumn(2.4f); cd.RelativeColumn(2.6f); });
                    foreach (var head in new[] { "الدفعة", "المبلغ", "الحالة", "تاريخ السداد" })
                        t.Cell().Background(Panel).Padding(5).Text(head).Bold();
                    foreach (var p in d.Payments)
                    {
                        t.Cell().BorderBottom(1).BorderColor(Line).Padding(5).Text(p.Label);
                        t.Cell().BorderBottom(1).BorderColor(Line).Padding(5).Text(Egp(p.AmountEgp));
                        t.Cell().BorderBottom(1).BorderColor(Line).Padding(5).Text(p.Status);
                        t.Cell().BorderBottom(1).BorderColor(Line).Padding(5).Text(p.PaidAt is { } at ? Date(at) : "—");
                    }
                });
            }
            col.Item().PaddingTop(8).Text("السداد على دفعتين: 20% بعد اعتماد التصميم (أو عند تأكيد الطلب إن لم توجد مرحلة تصميم) و80% عند اكتمال آخر مرحلة.").FontSize(8).FontColor(Muted);
        });
        Footer(page);
    })).GeneratePdf();

    public static byte[] Receipt(ReceiptData d) => Document.Create(c => c.Page(page =>
    {
        Setup(page);
        page.Header().Element(h => Header(h, d.Party == "Lab" ? "إشعار تحصيل دفعة — نسخة المعمل" : "إيصال سداد", d.Number, d.PaidAt));
        page.Content().PaddingVertical(14).Column(col =>
        {
            col.Spacing(12);
            if (d.TestMode)
                col.Item().Background("#FDF0D8").Padding(8).Text("عملية تجريبية (وضع الاختبار) — ليست دفعة حقيقية").FontColor("#9A5B00").Bold();
            col.Item().Element(x => Parties(x, d.OrderNumber, d.PatientName, d.DentistName, string.Empty, d.LabName));
            col.Item().Background(Panel).Padding(14).Column(box =>
            {
                box.Spacing(6);
                Row(box, "الدفعة", d.MilestoneLabel);
                Row(box, "المبلغ المدفوع", Egp(d.AmountEgp), bold: true);
                if (d.CommissionEgp is { } com) Row(box, $"عمولة المنصة ({d.CommissionPercent}%)", "- " + Egp(com));
                if (d.LabNetEgp is { } net) Row(box, "صافي المعمل من الدفعة", Egp(net), bold: true);
                if (d.RefundedEgp > 0) Row(box, "تم استرداده", Egp(d.RefundedEgp));
                Row(box, "وسيلة الدفع", d.Provider);
                Row(box, "مرجع العملية", d.TransactionRef);
                Row(box, "تاريخ السداد", Date(d.PaidAt));
            });
            col.Item().Text("تم تأكيد هذه الدفعة من بوابة الدفع مباشرةً.").FontSize(9).FontColor(Muted);
        });
        Footer(page);
    })).GeneratePdf();

    // ---------------- building blocks ----------------

    private static void Setup(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.ContentFromRightToLeft();
        // Arabic-capable fonts available on the server (Windows: Tahoma/Arial; Linux: install fonts-noto / Noto Naskh Arabic).
        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Ink).FontFamily(Fonts));
    }

    private static void Header(IContainer c, string title, string number, DateTimeOffset at) => c.Column(col =>
    {
        col.Item().Row(r =>
        {
            r.RelativeItem().Column(x =>
            {
                x.Item().Text("تاج").FontSize(26).Bold().FontColor(Navy);
                x.Item().Text("منصة معامل الأسنان في مصر").FontSize(9).FontColor(Muted);
            });
            r.RelativeItem().AlignLeft().Column(x =>
            {
                x.Item().AlignLeft().Text(title).FontSize(16).Bold().FontColor(Ink);
                x.Item().AlignLeft().Text($"رقم: {number}").FontColor(Muted);
                x.Item().AlignLeft().Text($"التاريخ: {Date(at)}").FontColor(Muted);
            });
        });
        col.Item().PaddingTop(8).LineHorizontal(2).LineColor(Navy);
    });

    private static void Parties(IContainer c, string orderNumber, string patient, string dentist, string clinic, string lab) => c.Row(r =>
    {
        r.Spacing(10);
        r.RelativeItem().Background(Panel).Padding(10).Column(x =>
        {
            x.Item().Text("الطلب").Bold();
            x.Item().Text($"رقم الطلب: {orderNumber}");
            x.Item().Text($"اسم المريض: {patient}");
        });
        r.RelativeItem().Background(Panel).Padding(10).Column(x =>
        {
            x.Item().Text("الأطراف").Bold();
            x.Item().Text($"الطبيب: {dentist}");
            if (!string.IsNullOrWhiteSpace(clinic)) x.Item().Text($"العيادة: {clinic}");
            x.Item().Text($"المعمل: {lab}");
        });
    });

    private static void Row(ColumnDescriptor col, string label, string value, bool bold = false) => col.Item().Row(r =>
    {
        r.RelativeItem().Text(label).FontColor(Muted);
        var t = r.AutoItem().Text(value);
        if (bold) t.Bold();
    });

    private static void Footer(PageDescriptor page) => page.Footer().Column(f =>
    {
        f.Item().LineHorizontal(1).LineColor(Line);
        f.Item().PaddingTop(4).Row(r =>
        {
            r.RelativeItem().Text("تاج — هذا المستند صادر إلكترونيًا ولا يحتاج إلى توقيع.").FontSize(8).FontColor(Muted);
            r.AutoItem().Text(x => { x.CurrentPageNumber().FontSize(8); x.Span(" / ").FontSize(8); x.TotalPages().FontSize(8); });
        });
    });
}
