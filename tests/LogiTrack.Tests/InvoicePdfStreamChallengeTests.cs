using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using logirack.Models;
using logirack.Services;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

namespace LogiTrack.Tests;

public sealed class InvoicePdfStreamChallengeTests
{
    private readonly ITestOutputHelper _output;

    public InvoicePdfStreamChallengeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static (Trip, Customer) CreateSampleTripAndCustomer(
        int id = 101,
        TripStatus status = TripStatus.Completed,
        double distance = 120.5,
        double weight = 45.0,
        double volume = 2.5,
        double estimatedPrice = 2857.5,
        double adminPrice = 2857.5,
        string customerName = "Ola Nordmann",
        string companyName = "Nordmann Logistics AS",
        string fromCity = "Oslo",
        string toCity = "Drammen")
    {
        var trip = new Trip
        {
            Id = id,
            CustomerId = "cust-test-1",
            FromCity = fromCity,
            ToCity = toCity,
            FromAddress = "Karl Johans gate 10",
            ToAddress = "Bragernes Torg 3",
            FromZipCode = "0154",
            ToZipCode = "3017",
            Distance = distance,
            Weight = weight,
            Volume = volume,
            GoodsType = "Commercial Goods",
            PickupTime = DateTime.UtcNow,
            EstimatedPrice = estimatedPrice,
            AdminPrice = adminPrice,
            Status = status,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow
        };

        var customer = new Customer
        {
            Id = "cust-test-1",
            FirstName = "Ola",
            LastName = "Nordmann",
            DisplayName = customerName,
            Email = "ola.nordmann@example.test",
            PhoneNumber = "+4740012345",
            CompanyName = companyName
        };

        return (trip, customer);
    }

    private static string ExtractTextFromPage(PdfPage page)
    {
        var fontMaps = new Dictionary<string, Dictionary<int, string>>();

        var fontsDict = page.Resources?.Elements.GetDictionary("/Font");
        if (fontsDict != null)
        {
            foreach (var key in fontsDict.Elements.Keys)
            {
                var font = fontsDict.Elements.GetDictionary(key);
                if (font == null) continue;

                var toUnicode = font.Elements["/ToUnicode"];
                if (toUnicode is PdfReference tRef && tRef.Value is PdfDictionary tDict && tDict.Stream != null)
                {
                    tDict.Stream.TryUnfilter();
                    var cmapText = Encoding.UTF8.GetString(tDict.Stream.Value);
                    fontMaps[key] = ParseCMap(cmapText);
                }
            }
        }

        var contentSb = new StringBuilder();
        for (int i = 0; i < page.Contents.Elements.Count; i++)
        {
            var item = page.Contents.Elements[i];
            PdfDictionary? dict = null;
            if (item is PdfReference pref && pref.Value is PdfDictionary d1) dict = d1;
            else if (item is PdfDictionary d2) dict = d2;

            if (dict?.Stream != null)
            {
                dict.Stream.TryUnfilter();
                contentSb.Append(Encoding.UTF8.GetString(dict.Stream.Value));
                contentSb.Append('\n');
            }
        }

        string rawContent = contentSb.ToString();
        var decoded = new StringBuilder();
        string currentFont = "";

        var tokens = Regex.Matches(rawContent, @"(/F\w+)\s+[\d\.]+\s+Tf|<([0-9A-Fa-f\r\n\s]+)>\s*Tj|\[([^\]]+)\]\s*TJ", RegexOptions.Singleline);
        foreach (Match m in tokens)
        {
            if (m.Groups[1].Success)
            {
                currentFont = m.Groups[1].Value;
            }
            else if (m.Groups[2].Success)
            {
                var cleanHex = Regex.Replace(m.Groups[2].Value, @"\s+", "");
                decoded.AppendLine(DecodeHexString(cleanHex, currentFont, fontMaps));
            }
            else if (m.Groups[3].Success)
            {
                var tjContent = m.Groups[3].Value;
                var tjHexMatches = Regex.Matches(tjContent, @"<([0-9A-Fa-f\r\n\s]+)>");
                var tjSb = new StringBuilder();
                foreach (Match hm in tjHexMatches)
                {
                    var cleanHex = Regex.Replace(hm.Groups[1].Value, @"\s+", "");
                    tjSb.Append(DecodeHexString(cleanHex, currentFont, fontMaps));
                }
                if (tjSb.Length > 0)
                {
                    decoded.AppendLine(tjSb.ToString());
                }
            }
        }

        return decoded.ToString();
    }

    private static string DecodeHexString(string hex, string fontName, Dictionary<string, Dictionary<int, string>> fontMaps)
    {
        fontMaps.TryGetValue(fontName, out var map);
        var sb = new StringBuilder();
        for (int i = 0; i + 4 <= hex.Length; i += 4)
        {
            int cid = int.Parse(hex.Substring(i, 4), NumberStyles.HexNumber);
            if (map != null && map.TryGetValue(cid, out var ch))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append((char)cid);
            }
        }
        return sb.ToString();
    }

    private static Dictionary<int, string> ParseCMap(string cmapText)
    {
        var map = new Dictionary<int, string>();

        var bfrangeMatches = Regex.Matches(cmapText, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>");
        foreach (Match m in bfrangeMatches)
        {
            int startCid = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
            int endCid = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
            int startUni = int.Parse(m.Groups[3].Value, NumberStyles.HexNumber);

            for (int cid = startCid; cid <= endCid; cid++)
            {
                map[cid] = ((char)(startUni + (cid - startCid))).ToString();
            }
        }

        var bfcharMatches = Regex.Matches(cmapText, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>");
        foreach (Match m in bfcharMatches)
        {
            int cid = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
            int uni = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
            if (!map.ContainsKey(cid))
            {
                map[cid] = ((char)uni).ToString();
            }
        }

        return map;
    }

    // --------------------------------------------------------------------
    // TEST 1: PDF Magic Bytes, Non-zero Length (> 1 KB), and EOF Marker
    // --------------------------------------------------------------------

    [Theory]
    [InlineData(1, 10.0, 5.0, 0.5, 400.0, 400.0)]
    [InlineData(42, 500.0, 100.0, 5.0, 9600.0, 9600.0)]
    [InlineData(999999, 1200.5, 2500.0, 30.0, 68157.5, 70000.0)]
    [InlineData(0, 0.0, 0.0, 0.0, 150.0, 150.0)]
    public void Stress_PdfStreamIntegrity_ValidMagicBytesAndSubstantialLength(
        int tripId, double distance, double weight, double volume, double estPrice, double adminPrice)
    {
        var (trip, customer) = CreateSampleTripAndCustomer(tripId, TripStatus.Completed, distance, weight, volume, estPrice, adminPrice);
        var service = new PdfInvoiceService();

        var pdfBytes = service.GenerateInvoicePdf(trip, customer);

        // 1. Assert non-null & substantial length (> 1024 bytes)
        Assert.NotNull(pdfBytes);
        Assert.NotEmpty(pdfBytes);
        Assert.True(pdfBytes.Length > 1024, $"PDF byte stream length {pdfBytes.Length} must be > 1 KB");

        // 2. Assert PDF magic bytes header starts with %PDF-
        var header = Encoding.ASCII.GetString(pdfBytes, 0, 5);
        Assert.Equal("%PDF-", header);

        // 3. Assert PDF trailer ends with %%EOF
        var trailer = Encoding.ASCII.GetString(pdfBytes, pdfBytes.Length - 40, 40);
        Assert.Contains("%%EOF", trailer);
    }

    // --------------------------------------------------------------------
    // TEST 2: PDF Document Structure, Object Parsing & Metadata Fields
    // --------------------------------------------------------------------

    [Theory]
    [InlineData(1001, "Invoice INV-001001", "Trip #1001 Shipment Invoice")]
    [InlineData(7, "Invoice INV-000007", "Trip #7 Shipment Invoice")]
    [InlineData(123456, "Invoice INV-123456", "Trip #123456 Shipment Invoice")]
    public void Stress_PdfDocumentStructureAndMetadata_StrictlyPopulated(
        int tripId, string expectedTitle, string expectedSubject)
    {
        var (trip, customer) = CreateSampleTripAndCustomer(tripId);
        var service = new PdfInvoiceService();
        var pdfBytes = service.GenerateInvoicePdf(trip, customer);

        using var ms = new MemoryStream(pdfBytes);

        // Verify parsing in all supported modes
        using var infoDoc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        Assert.Equal(expectedTitle, infoDoc.Info.Title);
        Assert.Equal("Logi-Track", infoDoc.Info.Author);
        Assert.Equal(expectedSubject, infoDoc.Info.Subject);
        Assert.Equal("Logi-Track Invoicing Service", infoDoc.Info.Creator);
        Assert.False(string.IsNullOrWhiteSpace(infoDoc.Info.Producer), "Producer metadata must be present");
        Assert.Equal(1, infoDoc.PageCount);

        // Page layout check
        var page = infoDoc.Pages[0];
        Assert.Equal(595, Math.Round(page.Width.Point));   // A4 Width: 595.28 pt
        Assert.Equal(842, Math.Round(page.Height.Point));  // A4 Height: 841.89 pt
    }

    // --------------------------------------------------------------------
    // TEST 3: Content Itemized Breakdown (Base, Distance, Weight, Handling, Total)
    // --------------------------------------------------------------------

    [Fact]
    public void Stress_ContentStream_StandardItemizedFareBreakdownAndPaymentStatus()
    {
        const double distance = 100.0;
        const double weight = 25.0;
        const double volume = 1.5;
        // Standard formula: 50 base + (100 * 15) distance + (25 * 20) weight + 100 handling = 50 + 1500 + 500 + 100 = 2150
        const double expectedTotal = 2150.00;

        var (trip, customer) = CreateSampleTripAndCustomer(
            id: 555,
            distance: distance,
            weight: weight,
            volume: volume,
            estimatedPrice: expectedTotal,
            adminPrice: expectedTotal);

        var service = new PdfInvoiceService();
        var pdfBytes = service.GenerateInvoicePdf(trip, customer);

        using var ms = new MemoryStream(pdfBytes);
        using var pdfDoc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

        string decodedText = ExtractTextFromPage(pdfDoc.Pages[0]);

        // 1. Header & ID
        Assert.Contains("LOGI-TRACK", decodedText);
        Assert.Contains("INV-000555", decodedText);

        // 2. Base Rate
        Assert.Contains("Base Freight Handling", decodedText);
        Assert.Contains("50.00 NOK", decodedText);

        // 3. Distance Transport Fee
        Assert.Contains("Distance Transport Fee", decodedText);
        Assert.Contains("100.0 km @ 15.00 NOK/km", decodedText);
        Assert.Contains("1500.00 NOK", decodedText);

        // 4. Weight Surcharge
        Assert.Contains("Cargo Weight Surcharge", decodedText);
        Assert.Contains("25.0 kg @ 20.00 NOK/kg", decodedText);
        Assert.Contains("500.00 NOK", decodedText);

        // 5. Service Handling Fee
        Assert.Contains("Service Handling & Dispatch Fee", decodedText);
        Assert.Contains("100.00 NOK", decodedText);

        // 6. Total Final Price
        Assert.Contains("TOTAL FINAL PRICE:", decodedText);
        Assert.Contains("2150.00 NOK", decodedText);

        // 7. Payment Status
        Assert.Contains("PAID / SETTLED", decodedText);
        Assert.Contains("PAYMENT STATUS: PAID IN FULL", decodedText);
    }

    // --------------------------------------------------------------------
    // TEST 4: Price Adjustments & Discounts (Positive adjustment & negative discount)
    // --------------------------------------------------------------------

    [Fact]
    public void Stress_ContentStream_AdminRateAdjustment_AppearsWhenPriceDiffers()
    {
        // Standard: 50 + (10 * 15) + (10 * 20) + 100 = 500 NOK.
        // Admin overrides to 750 NOK (+250 adjustment).
        var (trip, customer) = CreateSampleTripAndCustomer(
            id: 888,
            distance: 10.0,
            weight: 10.0,
            volume: 0.5,
            estimatedPrice: 500.0,
            adminPrice: 750.0);

        var service = new PdfInvoiceService();
        var pdfBytes = service.GenerateInvoicePdf(trip, customer);

        using var ms = new MemoryStream(pdfBytes);
        using var pdfDoc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

        string decodedText = ExtractTextFromPage(pdfDoc.Pages[0]);

        Assert.Contains("Admin Rate Adjustment", decodedText);
        Assert.Contains("250.00 NOK", decodedText);
        Assert.Contains("TOTAL FINAL PRICE:", decodedText);
        Assert.Contains("750.00 NOK", decodedText);
    }

    [Fact]
    public void Stress_ContentStream_ContractDiscount_AppearsWhenAdminPriceLower()
    {
        // Standard: 50 + (10 * 15) + (10 * 20) + 100 = 500 NOK.
        // Admin overrides to 400 NOK (-100 discount).
        var (trip, customer) = CreateSampleTripAndCustomer(
            id: 889,
            distance: 10.0,
            weight: 10.0,
            volume: 0.5,
            estimatedPrice: 500.0,
            adminPrice: 400.0);

        var service = new PdfInvoiceService();
        var pdfBytes = service.GenerateInvoicePdf(trip, customer);

        using var ms = new MemoryStream(pdfBytes);
        using var pdfDoc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

        string decodedText = ExtractTextFromPage(pdfDoc.Pages[0]);

        Assert.Contains("Contract / Promo Discount", decodedText);
        Assert.Contains("-100.00 NOK", decodedText);
        Assert.Contains("TOTAL FINAL PRICE:", decodedText);
        Assert.Contains("400.00 NOK", decodedText);
    }

    // --------------------------------------------------------------------
    // TEST 5: Special Characters, Norwegian Glyphs (æ, ø, å), and Long Texts
    // --------------------------------------------------------------------

    [Fact]
    public void Stress_ContentStream_HandlesNorwegianCharactersAndUnicodeGracefully()
    {
        var (trip, customer) = CreateSampleTripAndCustomer(
            id: 777,
            customerName: "Jørgen Åsbø",
            companyName: "Blåbærsyltetøy AS",
            fromCity: "Tromsø",
            toCity: "Bodø");

        var service = new PdfInvoiceService();
        var pdfBytes = service.GenerateInvoicePdf(trip, customer);

        using var ms = new MemoryStream(pdfBytes);
        using var pdfDoc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

        string decodedText = ExtractTextFromPage(pdfDoc.Pages[0]);

        Assert.Contains("Jørgen Åsbø", decodedText);
        Assert.Contains("Blåbærsyltetøy AS", decodedText);
        Assert.Contains("Tromsø", decodedText);
        Assert.Contains("Bodø", decodedText);
    }

    // --------------------------------------------------------------------
    // TEST 6: Concurrency & Thread-Safety Stress Test
    // --------------------------------------------------------------------

    [Fact]
    public void Stress_ConcurrentPdfGeneration_IsThreadSafeAndProducesValidOutputs()
    {
        var service = new PdfInvoiceService();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(1, 51, i =>
        {
            try
            {
                var (trip, customer) = CreateSampleTripAndCustomer(
                    id: 1000 + i,
                    distance: 10.0 * i,
                    weight: 2.5 * i);

                var bytes = service.GenerateInvoicePdf(trip, customer);

                Assert.NotNull(bytes);
                Assert.True(bytes.Length > 1024);
                Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

                using var ms = new MemoryStream(bytes);
                using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
                Assert.Equal($"Invoice INV-{1000 + i:D6}", doc.Info.Title);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }
}
