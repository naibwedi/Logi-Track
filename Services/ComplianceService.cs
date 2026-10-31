namespace logirack.Services;

using logirack.Models;

public enum ComplianceSeverity
{
    Warning,
    Expired
}

public sealed record ComplianceAlert(
    ComplianceSeverity Severity,
    string Category,
    string Subject,
    string Message,
    DateTime DueDate)
{
    public string EntityLabel => Subject;
    public DateTime ExpirationDate => DueDate;
}

/// <summary>
/// DOT / regulatory expiration rules for drivers (CDL, medical card per 49 CFR 391.41),
/// vehicles (registration, annual DOT inspection, IRS Form 2290) and carrier filings (IFTA quarterly).
/// Pure logic: callers pass "today" so behaviour is deterministic and testable.
/// </summary>
public static class ComplianceService
{
    public const int WarningWindowDays = 30;

    public static bool IsMedicalCardExpired(Driver driver, DateTime today) =>
        driver.MedicalCardExpirationDate.HasValue && driver.MedicalCardExpirationDate.Value.Date < today.Date;

    public static bool IsCdlExpired(Driver driver, DateTime today) =>
        driver.CdlExpirationDate.HasValue && driver.CdlExpirationDate.Value.Date < today.Date;

    /// <summary>Next IFTA quarterly return due date on or after <paramref name="today"/> (Jan 31, Apr 30, Jul 31, Oct 31).</summary>
    public static DateTime NextIftaDueDate(DateTime today)
    {
        var d = today.Date;
        var candidates = new[]
        {
            new DateTime(d.Year, 1, 31), new DateTime(d.Year, 4, 30),
            new DateTime(d.Year, 7, 31), new DateTime(d.Year, 10, 31),
            new DateTime(d.Year + 1, 1, 31)
        };
        return candidates.First(c => c >= d);
    }

    public static IReadOnlyList<ComplianceAlert> GetAlerts(IEnumerable<Driver> drivers, IEnumerable<Vehicle> vehicles, DateTime today)
    {
        var alerts = new List<ComplianceAlert>();
        var date = today.Date;

        foreach (var driver in drivers)
        {
            var name = $"{driver.FirstName} {driver.LastName}".Trim();
            if (driver.MedicalCardExpirationDate.HasValue)
                Check(alerts, "DOT Medical Card", name, driver.MedicalCardExpirationDate.Value, date);
            else
                alerts.Add(new ComplianceAlert(ComplianceSeverity.Warning, "DOT Medical Card", name,
                    "No medical card expiration date on file.", date));

            if (driver.CdlExpirationDate.HasValue)
                Check(alerts, "CDL", name, driver.CdlExpirationDate.Value, date);
        }

        foreach (var vehicle in vehicles.Where(v => v.Status != VehicleStatus.Decommissioned))
        {
            var unit = $"Unit {vehicle.UnitNumber}";
            Check(alerts, "Vehicle Registration", unit, vehicle.RegistrationExpirationDate, date);
            if (vehicle.AnnualDotInspectionExpiration.HasValue)
                Check(alerts, "Annual DOT Inspection", unit, vehicle.AnnualDotInspectionExpiration.Value, date);
            if (vehicle.Form2290ExpirationDate.HasValue)
                Check(alerts, "IRS Form 2290", unit, vehicle.Form2290ExpirationDate.Value, date);
        }

        var ifta = NextIftaDueDate(date);
        if ((ifta - date).TotalDays <= WarningWindowDays)
        {
            alerts.Add(new ComplianceAlert(ComplianceSeverity.Warning, "IFTA Quarterly Return", "Carrier",
                $"IFTA fuel tax return due {ifta:MMM d, yyyy} ({(ifta - date).TotalDays:0} days).", ifta));
        }

        return alerts
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.DueDate)
            .ToList();
    }

    private static void Check(List<ComplianceAlert> alerts, string category, string subject, DateTime expires, DateTime today)
    {
        var days = (expires.Date - today).TotalDays;
        if (days < 0)
            alerts.Add(new ComplianceAlert(ComplianceSeverity.Expired, category, subject,
                $"{category} expired on {expires:MMM d, yyyy}.", expires.Date));
        else if (days <= WarningWindowDays)
            alerts.Add(new ComplianceAlert(ComplianceSeverity.Warning, category, subject,
                $"{category} expires on {expires:MMM d, yyyy} ({days:0} days).", expires.Date));
    }
}
