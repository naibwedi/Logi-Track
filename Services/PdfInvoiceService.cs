using System.Globalization;
using logirack.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace logirack.Services;

public class PdfInvoiceService : IInvoiceService
{
    private static readonly XColor PrimaryColor = XColor.FromArgb(24, 43, 73);      // Navy #182B49
    private static readonly XColor AccentColor = XColor.FromArgb(37, 99, 235);      // Blue #2563EB
    private static readonly XColor LightGray = XColor.FromArgb(243, 244, 246);      // Gray #F3F4F6
    private static readonly XColor BorderGray = XColor.FromArgb(229, 231, 235);     // Gray #E5E7EB
    private static readonly XColor TextDark = XColor.FromArgb(17, 24, 39);          // Slate #111827
    private static readonly XColor TextMuted = XColor.FromArgb(107, 114, 128);      // Slate #6B7280
    private static readonly XColor SuccessGreen = XColor.FromArgb(22, 101, 52);     // Green #166534
    private static readonly XColor SuccessBg = XColor.FromArgb(220, 252, 231);       // Light green #DCFCE7

    public byte[] GenerateInvoicePdf(Trip trip, Customer customer)
    {
        if (trip == null) throw new ArgumentNullException(nameof(trip));
        if (customer == null) throw new ArgumentNullException(nameof(customer));

        var document = new PdfDocument();
        document.Info.Title = $"Invoice INV-{trip.Id:D6}";
        document.Info.Author = "Logi-Track";
        document.Info.Subject = $"Trip #{trip.Id} Shipment Invoice";
        document.Info.Creator = "Logi-Track Invoicing Service";

        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.A4; // 595 x 842 points

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            DrawInvoice(gfx, page, trip, customer);
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static void DrawInvoice(XGraphics gfx, PdfPage page, Trip trip, Customer customer)
    {
        double pageWidth = page.Width;
        double margin = 40;
        double contentWidth = pageWidth - (margin * 2);
        double y = margin;

        // Fonts
        var titleFont = new XFont("Arial", 20, XFontStyle.Bold);
        var subtitleFont = new XFont("Arial", 9, XFontStyle.Regular);
        var sectionHeaderFont = new XFont("Arial", 11, XFontStyle.Bold);
        var labelFont = new XFont("Arial", 9, XFontStyle.Bold);
        var valueFont = new XFont("Arial", 9, XFontStyle.Regular);
        var boldValueFont = new XFont("Arial", 9, XFontStyle.Bold);
        var smallFont = new XFont("Arial", 8, XFontStyle.Regular);
        var badgeFont = new XFont("Arial", 9, XFontStyle.Bold);
        var totalFont = new XFont("Arial", 12, XFontStyle.Bold);

        // Brushes and Pens
        var primaryBrush = new XSolidBrush(PrimaryColor);
        var textDarkBrush = new XSolidBrush(TextDark);
        var textMutedBrush = new XSolidBrush(TextMuted);
        var borderPen = new XPen(BorderGray, 1);
        var lightGrayBrush = new XSolidBrush(LightGray);

        // 1. HEADER SECTION (Brand & Invoice Meta)
        // Top banner background
        gfx.DrawRectangle(new XSolidBrush(PrimaryColor), margin, y, contentWidth, 60);

        // Brand Name & Subtitle
        gfx.DrawString("LOGI-TRACK", titleFont, XBrushes.White, margin + 15, y + 28);
        gfx.DrawString("FREIGHT & LOGISTICS MANAGEMENT", subtitleFont, new XSolidBrush(XColor.FromArgb(191, 219, 254)), margin + 15, y + 45);

        // Invoice ID and Dates (right side of banner)
        string invoiceId = $"INV-{trip.Id:D6}";
        string issueDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        gfx.DrawString(invoiceId, new XFont("Arial", 14, XFontStyle.Bold), XBrushes.White,
            new XRect(margin, y + 15, contentWidth - 15, 20), XStringFormats.TopRight);
        gfx.DrawString($"Issue Date: {issueDate}", subtitleFont, new XSolidBrush(XColor.FromArgb(229, 231, 235)),
            new XRect(margin, y + 38, contentWidth - 15, 15), XStringFormats.TopRight);

        y += 75;

        // 2. STATUS & TRIP METADATA BAR
        double metaBarHeight = 32;
        gfx.DrawRoundedRectangle(borderPen, lightGrayBrush, margin, y, contentWidth, metaBarHeight, 4, 4);

        string tripCreated = trip.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        gfx.DrawString("Trip Created:", labelFont, textMutedBrush, margin + 12, y + 20);
        gfx.DrawString(tripCreated, valueFont, textDarkBrush, margin + 85, y + 20);

        gfx.DrawString("Payment Status:", labelFont, textMutedBrush, margin + 220, y + 20);
        string paymentStatus = trip.Status == TripStatus.Completed ? "PAID / SETTLED" : trip.Status.ToString().ToUpperInvariant();
        var statusColor = trip.Status == TripStatus.Completed ? SuccessGreen : TextDark;
        gfx.DrawString(paymentStatus, badgeFont, new XSolidBrush(statusColor), margin + 315, y + 20);

        y += metaBarHeight + 15;

        // 3. TWO-COLUMN: CUSTOMER INFORMATION & ROUTE DETAILS
        double colWidth = (contentWidth - 15) / 2;

        // Left Column: Customer Information
        double colHeight = 110;
        gfx.DrawRoundedRectangle(borderPen, XBrushes.White, margin, y, colWidth, colHeight, 4, 4);
        gfx.DrawRectangle(new XSolidBrush(LightGray), margin, y, colWidth, 24);
        gfx.DrawString("CUSTOMER INFORMATION", sectionHeaderFont, primaryBrush, margin + 10, y + 16);

        string customerName = !string.IsNullOrWhiteSpace(customer.DisplayName)
            ? customer.DisplayName
            : $"{customer.FirstName} {customer.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(customerName)) customerName = customer.UserName ?? "Customer";

        double custY = y + 38;
        gfx.DrawString("Name:", labelFont, textMutedBrush, margin + 10, custY);
        gfx.DrawString(customerName, valueFont, textDarkBrush, margin + 65, custY);

        custY += 16;
        gfx.DrawString("Email:", labelFont, textMutedBrush, margin + 10, custY);
        gfx.DrawString(customer.Email ?? "N/A", valueFont, textDarkBrush, margin + 65, custY);

        custY += 16;
        gfx.DrawString("Phone:", labelFont, textMutedBrush, margin + 10, custY);
        gfx.DrawString(customer.PhoneNumber ?? "N/A", valueFont, textDarkBrush, margin + 65, custY);

        custY += 16;
        gfx.DrawString("Company:", labelFont, textMutedBrush, margin + 10, custY);
        string company = !string.IsNullOrWhiteSpace(customer.CompanyName) ? customer.CompanyName : "N/A";
        gfx.DrawString(company, valueFont, textDarkBrush, margin + 65, custY);

        // Right Column: Shipment & Route Specifications
        double col2X = margin + colWidth + 15;
        gfx.DrawRoundedRectangle(borderPen, XBrushes.White, col2X, y, colWidth, colHeight, 4, 4);
        gfx.DrawRectangle(new XSolidBrush(LightGray), col2X, y, colWidth, 24);
        gfx.DrawString("SHIPMENT & ROUTE", sectionHeaderFont, primaryBrush, col2X + 10, y + 16);

        double routeY = y + 38;
        gfx.DrawString("Origin:", labelFont, textMutedBrush, col2X + 10, routeY);
        string origin = $"{trip.FromAddress}, {trip.FromZipCode} {trip.FromCity}".Trim().Trim(',', ' ');
        gfx.DrawString(Truncate(origin, 32), valueFont, textDarkBrush, col2X + 75, routeY);

        routeY += 16;
        gfx.DrawString("Destination:", labelFont, textMutedBrush, col2X + 10, routeY);
        string destination = $"{trip.ToAddress}, {trip.ToZipCode} {trip.ToCity}".Trim().Trim(',', ' ');
        gfx.DrawString(Truncate(destination, 32), valueFont, textDarkBrush, col2X + 75, routeY);

        routeY += 16;
        gfx.DrawString("Goods Type:", labelFont, textMutedBrush, col2X + 10, routeY);
        gfx.DrawString(trip.GoodsType ?? "General Freight", valueFont, textDarkBrush, col2X + 75, routeY);

        routeY += 16;
        gfx.DrawString("Metrics:", labelFont, textMutedBrush, col2X + 10, routeY);
        string metrics = $"{trip.Distance:F1} km  |  {trip.Weight:F1} kg  |  {trip.Volume:F2} m³";
        gfx.DrawString(metrics, boldValueFont, textDarkBrush, col2X + 75, routeY);

        y += colHeight + 20;

        // 4. ITEMIZED FARE BREAKDOWN TABLE
        gfx.DrawString("ITEMIZED FARE BREAKDOWN", sectionHeaderFont, primaryBrush, margin, y);
        y += 10;

        // Table Header
        double tableY = y;
        double colDescWidth = contentWidth * 0.55;
        double colRateWidth = contentWidth * 0.25;
        double colSubtotalWidth = contentWidth * 0.20;

        gfx.DrawRectangle(new XSolidBrush(PrimaryColor), margin, tableY, contentWidth, 24);
        gfx.DrawString("Description", labelFont, XBrushes.White, margin + 10, tableY + 16);
        gfx.DrawString("Calculation / Rate", labelFont, XBrushes.White, margin + colDescWidth + 10, tableY + 16);
        gfx.DrawString("Amount (NOK)", labelFont, XBrushes.White,
            new XRect(margin + colDescWidth + colRateWidth, tableY, colSubtotalWidth - 10, 24), XStringFormats.CenterRight);

        tableY += 24;

        // Line Items
        double baseRate = 50.00;
        double distanceFee = trip.Distance * 15.00;
        double weightFee = trip.Weight * 20.00;
        double serviceFee = 100.00;
        double calculatedEstimated = baseRate + distanceFee + weightFee + serviceFee;

        double finalPrice = trip.AdminPrice > 0 ? trip.AdminPrice : trip.EstimatedPrice;
        if (finalPrice <= 0)
        {
            finalPrice = calculatedEstimated;
        }

        var lineItems = new List<(string Desc, string Rate, double Amount)>
        {
            ("Base Freight Handling", "Standard Base Dispatch Rate", baseRate),
            ("Distance Transport Fee", $"{trip.Distance:F1} km @ 15.00 NOK/km", distanceFee),
            ("Cargo Weight Surcharge", $"{trip.Weight:F1} kg @ 20.00 NOK/kg", weightFee),
            ("Service Handling & Dispatch Fee", "Standard Logistics Processing Fee", serviceFee)
        };

        // If AdminPrice differed from standard calculated estimate, add adjustment line
        if (trip.AdminPrice > 0 && Math.Abs(trip.AdminPrice - calculatedEstimated) > 0.01)
        {
            double diff = trip.AdminPrice - calculatedEstimated;
            string adjName = diff >= 0 ? "Admin Rate Adjustment" : "Contract / Promo Discount";
            lineItems.Add((adjName, "Rate Finalization Adjustment", diff));
        }

        bool alt = false;
        foreach (var item in lineItems)
        {
            var rowBrush = alt ? lightGrayBrush : XBrushes.White;
            gfx.DrawRectangle(rowBrush, margin, tableY, contentWidth, 22);
            gfx.DrawRectangle(borderPen, margin, tableY, contentWidth, 22);

            gfx.DrawString(item.Desc, valueFont, textDarkBrush, margin + 10, tableY + 15);
            gfx.DrawString(item.Rate, valueFont, textMutedBrush, margin + colDescWidth + 10, tableY + 15);
            gfx.DrawString($"{item.Amount:F2} NOK", valueFont, textDarkBrush,
                new XRect(margin + colDescWidth + colRateWidth, tableY, colSubtotalWidth - 10, 22), XStringFormats.CenterRight);

            tableY += 22;
            alt = !alt;
        }

        // Subtotal & Total Block
        tableY += 5;
        double totalsWidth = 260;
        double totalsX = margin + contentWidth - totalsWidth;

        // Total Final Price Row
        gfx.DrawRectangle(new XSolidBrush(PrimaryColor), totalsX, tableY, totalsWidth, 30);
        gfx.DrawString("TOTAL FINAL PRICE:", labelFont, XBrushes.White, totalsX + 12, tableY + 20);
        gfx.DrawString($"{finalPrice:F2} NOK", totalFont, XBrushes.White,
            new XRect(totalsX, tableY, totalsWidth - 12, 30), XStringFormats.CenterRight);

        tableY += 35;

        // Payment Settlement Status Box
        if (trip.Status == TripStatus.Completed)
        {
            gfx.DrawRoundedRectangle(new XPen(SuccessGreen, 1), new XSolidBrush(SuccessBg), totalsX, tableY, totalsWidth, 24, 3, 3);
            gfx.DrawString("PAYMENT STATUS: PAID IN FULL", badgeFont, new XSolidBrush(SuccessGreen),
                new XRect(totalsX, tableY, totalsWidth, 24), XStringFormats.Center);
        }
        else
        {
            gfx.DrawRoundedRectangle(borderPen, lightGrayBrush, totalsX, tableY, totalsWidth, 24, 3, 3);
            gfx.DrawString($"PAYMENT STATUS: {trip.Status.ToString().ToUpperInvariant()}", badgeFont, textDarkBrush,
                new XRect(totalsX, tableY, totalsWidth, 24), XStringFormats.Center);
        }

        y = tableY + 50;

        // 5. LEGAL / TAX NOTICE
        gfx.DrawString("Tax Notice: All prices listed in NOK and include statutory Value Added Tax (MVA 25%) where applicable.",
            smallFont, textMutedBrush, margin, y);
        y += 14;
        gfx.DrawString("Payment terms: Settled electronically upon cargo delivery confirmation.",
            smallFont, textMutedBrush, margin, y);

        // 6. FOOTER
        double footerY = page.Height - margin - 20;
        gfx.DrawLine(borderPen, margin, footerY, margin + contentWidth, footerY);
        gfx.DrawString("Logi-Track Systems AS • Haakon VIIs gate 1, 0161 Oslo, Norway • org.nr 999 888 777 MVA",
            smallFont, textMutedBrush, new XRect(margin, footerY + 6, contentWidth, 12), XStringFormats.Center);
        gfx.DrawString("Thank you for choosing Logi-Track. For inquiries, contact billing@logitrack.test",
            smallFont, textMutedBrush, new XRect(margin, footerY + 18, contentWidth, 12), XStringFormats.Center);
    }

    public byte[] GenerateSettlementStatementPdf(Trip trip, Driver driver, SettlementBreakdown breakdown, Vehicle? vehicle = null, Broker? broker = null)
    {
        if (trip == null) throw new ArgumentNullException(nameof(trip));
        if (driver == null) throw new ArgumentNullException(nameof(driver));

        var document = new PdfDocument();
        string invLabel = !string.IsNullOrWhiteSpace(trip.InvoiceNumber) ? trip.InvoiceNumber : $"INV-{trip.Id:D6}";
        document.Info.Title = $"Driver Settlement Statement {invLabel}";
        document.Info.Author = "Rufta Logistics LLC";
        document.Info.Subject = $"Trip #{trip.Id} Driver Settlement Statement (88% Split)";
        document.Info.Creator = "Logi-Track Settlement Engine";

        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            DrawSettlementStatement(gfx, page, trip, driver, breakdown, vehicle, broker);
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static void DrawSettlementStatement(
        XGraphics gfx,
        PdfPage page,
        Trip trip,
        Driver driver,
        SettlementBreakdown breakdown,
        Vehicle? vehicle,
        Broker? broker)
    {
        double pageWidth = page.Width;
        double margin = 40;
        double contentWidth = pageWidth - (margin * 2);
        double y = margin;

        var titleFont = new XFont("Arial", 18, XFontStyle.Bold);
        var subtitleFont = new XFont("Arial", 9, XFontStyle.Regular);
        var sectionHeaderFont = new XFont("Arial", 11, XFontStyle.Bold);
        var labelFont = new XFont("Arial", 9, XFontStyle.Bold);
        var valueFont = new XFont("Arial", 9, XFontStyle.Regular);
        var boldValueFont = new XFont("Arial", 9, XFontStyle.Bold);
        var smallFont = new XFont("Arial", 8, XFontStyle.Regular);
        var totalFont = new XFont("Arial", 12, XFontStyle.Bold);

        var primaryBrush = new XSolidBrush(PrimaryColor);
        var textDarkBrush = new XSolidBrush(TextDark);
        var textMutedBrush = new XSolidBrush(TextMuted);
        var borderPen = new XPen(BorderGray, 1);
        var lightGrayBrush = new XSolidBrush(LightGray);

        // Header banner
        gfx.DrawRectangle(primaryBrush, margin, y, contentWidth, 62);
        gfx.DrawString("RUFTA LOGISTICS LLC", titleFont, XBrushes.White, margin + 15, y + 26);
        gfx.DrawString("DRIVER SETTLEMENT VOUCHER (12% DISPATCH / 88% DRIVER SPLIT)", subtitleFont,
            new XSolidBrush(XColor.FromArgb(191, 219, 254)), margin + 15, y + 45);

        string invLabel = !string.IsNullOrWhiteSpace(trip.InvoiceNumber) ? trip.InvoiceNumber : $"INV-{trip.Id:D6}";
        string issueDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        gfx.DrawString(invLabel, new XFont("Arial", 14, XFontStyle.Bold), XBrushes.White,
            new XRect(margin, y + 15, contentWidth - 15, 20), XStringFormats.TopRight);
        gfx.DrawString($"Settlement Date: {issueDate}", subtitleFont, new XSolidBrush(XColor.FromArgb(229, 231, 235)),
            new XRect(margin, y + 38, contentWidth - 15, 15), XStringFormats.TopRight);

        y += 76;

        // Driver & Equipment metadata
        double colWidth = (contentWidth - 15) / 2;
        double colHeight = 96;
        gfx.DrawRoundedRectangle(borderPen, XBrushes.White, margin, y, colWidth, colHeight, 4, 4);
        gfx.DrawRectangle(lightGrayBrush, margin, y, colWidth, 22);
        gfx.DrawString("DRIVER & EQUIPMENT", sectionHeaderFont, primaryBrush, margin + 10, y + 15);

        string driverName = $"{driver.FirstName} {driver.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(driverName)) driverName = driver.Email ?? "Driver";

        double dy = y + 36;
        gfx.DrawString("Driver:", labelFont, textMutedBrush, margin + 10, dy);
        gfx.DrawString(driverName, boldValueFont, textDarkBrush, margin + 75, dy);

        dy += 16;
        gfx.DrawString("Tractor Unit:", labelFont, textMutedBrush, margin + 10, dy);
        string unitInfo = vehicle != null ? $"Unit #{vehicle.UnitNumber} ({vehicle.LicensePlate})" : "Unassigned";
        gfx.DrawString(unitInfo, valueFont, textDarkBrush, margin + 75, dy);

        dy += 16;
        gfx.DrawString("CDL / State:", labelFont, textMutedBrush, margin + 10, dy);
        string cdlInfo = !string.IsNullOrWhiteSpace(driver.CdlNumber) ? $"{driver.CdlNumber} ({driver.CdlState ?? "US"})" : "On File";
        gfx.DrawString(cdlInfo, valueFont, textDarkBrush, margin + 75, dy);

        // Load & Broker metadata
        double col2X = margin + colWidth + 15;
        gfx.DrawRoundedRectangle(borderPen, XBrushes.White, col2X, y, colWidth, colHeight, 4, 4);
        gfx.DrawRectangle(lightGrayBrush, col2X, y, colWidth, 22);
        gfx.DrawString("LOAD & BROKER DETAILS", sectionHeaderFont, primaryBrush, col2X + 10, y + 15);

        double ly = y + 36;
        gfx.DrawString("Broker:", labelFont, textMutedBrush, col2X + 10, ly);
        string brokerName = broker != null ? $"{broker.CompanyName} ({broker.DisplayMc})" : "Direct Shipper";
        gfx.DrawString(Truncate(brokerName, 30), valueFont, textDarkBrush, col2X + 80, ly);

        ly += 16;
        gfx.DrawString("Load #:", labelFont, textMutedBrush, col2X + 10, ly);
        gfx.DrawString(trip.BrokerLoadNumber ?? $"TRIP-{trip.Id}", boldValueFont, textDarkBrush, col2X + 80, ly);

        ly += 16;
        gfx.DrawString("Lane:", labelFont, textMutedBrush, col2X + 10, ly);
        gfx.DrawString(Truncate($"{trip.FromCity} -> {trip.ToCity}", 30), valueFont, textDarkBrush, col2X + 80, ly);

        y += colHeight + 20;

        // Settlement calculation table
        gfx.DrawString("SETTLEMENT ACCOUNTING BREAKDOWN", sectionHeaderFont, primaryBrush, margin, y);
        y += 10;

        double colDescWidth = contentWidth * 0.55;
        double colRateWidth = contentWidth * 0.25;
        double colSubtotalWidth = contentWidth * 0.20;

        gfx.DrawRectangle(primaryBrush, margin, y, contentWidth, 24);
        gfx.DrawString("Line Item", labelFont, XBrushes.White, margin + 10, y + 16);
        gfx.DrawString("Formula / Basis", labelFont, XBrushes.White, margin + colDescWidth + 10, y + 16);
        gfx.DrawString("Amount (USD)", labelFont, XBrushes.White,
            new XRect(margin + colDescWidth + colRateWidth, y, colSubtotalWidth - 10, 24), XStringFormats.CenterRight);

        y += 24;

        var rows = new List<(string Desc, string Basis, decimal Amount)>
        {
            (breakdown.IsTonu ? "TONU (Truck Order Not Used) Payout" : "Gross Load Rate (Rate Con + Accessorials)",
             breakdown.IsTonu ? "Canceled Load Flat Fee" : $"Rate Con + Accessorials (${breakdown.AccessorialsTotal:F2})",
             breakdown.GrossRate),
            ("Phoenix Capital Factoring Fee (Info)", "1.50% of Gross Rate", -breakdown.FactoringFee),
            ("Rufta Logistics Dispatch Fee", "12.00% of Gross Rate", -breakdown.DispatchFee),
            ("Driver Gross Share (Before Deductions)", "88.00% of Gross Rate", breakdown.DriverGrossShare)
        };

        if (breakdown.Deductions > 0)
        {
            string reason = !string.IsNullOrWhiteSpace(trip.DeductionReason) ? trip.DeductionReason : "Fuel / Cash Advance / Escrow";
            rows.Add(($"Driver Deductions ({Truncate(reason, 28)})", "Direct Deduction", -breakdown.Deductions));
        }

        bool alt = false;
        foreach (var row in rows)
        {
            var rowBrush = alt ? lightGrayBrush : XBrushes.White;
            gfx.DrawRectangle(rowBrush, margin, y, contentWidth, 22);
            gfx.DrawRectangle(borderPen, margin, y, contentWidth, 22);

            gfx.DrawString(row.Desc, valueFont, textDarkBrush, margin + 10, y + 15);
            gfx.DrawString(row.Basis, valueFont, textMutedBrush, margin + colDescWidth + 10, y + 15);
            string formattedAmt = row.Amount < 0 ? $"-${Math.Abs(row.Amount):F2}" : $"${row.Amount:F2}";
            gfx.DrawString(formattedAmt, boldValueFont, textDarkBrush,
                new XRect(margin + colDescWidth + colRateWidth, y, colSubtotalWidth - 10, 22), XStringFormats.CenterRight);

            y += 22;
            alt = !alt;
        }

        y += 10;
        double totalsWidth = 280;
        double totalsX = margin + contentWidth - totalsWidth;

        gfx.DrawRectangle(new XSolidBrush(SuccessGreen), totalsX, y, totalsWidth, 32);
        gfx.DrawString("DRIVER NET PAYOUT (88%):", labelFont, XBrushes.White, totalsX + 12, y + 20);
        gfx.DrawString($"${breakdown.DriverNetPay:F2} USD", totalFont, XBrushes.White,
            new XRect(totalsX, y, totalsWidth - 12, 32), XStringFormats.CenterRight);

        double footerY = page.Height - margin - 20;
        gfx.DrawLine(borderPen, margin, footerY, margin + contentWidth, footerY);
        gfx.DrawString("Rufta Logistics LLC • FMCSA Motor Carrier Settlement Statement • Confidential Driver Voucher",
            smallFont, textMutedBrush, new XRect(margin, footerY + 6, contentWidth, 12), XStringFormats.Center);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
        return value.Substring(0, maxLength - 3) + "...";
    }
}
