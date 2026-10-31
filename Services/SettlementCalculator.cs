namespace logirack.Services;

using logirack.Models;

/// <summary>
/// Itemized load settlement following Rufta Logistics' accounting:
///   Gross = Rate Confirmation + Accessorials (detention, lumper, other)
///   Factoring fee = 1.5% of Gross (deducted by the factoring company from the carrier's advance)
///   Dispatch fee  = 12% of Gross (company / dispatcher share)
///   Driver net    = Gross - Dispatch fee - Deductions
/// For a TONU load the gross is the TONU payout and no accessorials apply.
/// </summary>
public sealed record SettlementBreakdown(
    decimal RateConfirmation,
    decimal Detention,
    decimal Lumper,
    decimal OtherAccessorials,
    decimal Accessorials,
    decimal GrossRate,
    decimal FactoringFee,
    decimal DispatchFee,
    decimal Deductions,
    decimal DriverNetPay,
    decimal CarrierNetAfterFactoring,
    bool IsTonu)
{
    public decimal AccessorialsTotal => Accessorials;
    public decimal DriverGrossShare => GrossRate - DispatchFee;
}

public static class SettlementCalculator
{
    public const decimal FactoringRate = 0.015m;
    public const decimal DispatchRate = 0.12m;
    public const decimal TonuMinimum = 150m;
    public const decimal TonuMaximum = 200m;

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static SettlementBreakdown Calculate(
        decimal rateConfirmation,
        decimal detention = 0m,
        decimal lumper = 0m,
        decimal otherAccessorials = 0m,
        decimal deductions = 0m)
    {
        if (rateConfirmation < 0) throw new ArgumentOutOfRangeException(nameof(rateConfirmation), "Rate confirmation cannot be negative.");
        if (detention < 0 || lumper < 0 || otherAccessorials < 0) throw new ArgumentOutOfRangeException(nameof(detention), "Accessorials cannot be negative.");
        if (deductions < 0) throw new ArgumentOutOfRangeException(nameof(deductions), "Enter deductions as a positive amount.");

        var accessorials = Round(detention + lumper + otherAccessorials);
        var gross = Round(rateConfirmation + accessorials);
        return Build(Round(rateConfirmation), Round(detention), Round(lumper), Round(otherAccessorials), accessorials, gross, Round(deductions), isTonu: false);
    }

    public static SettlementBreakdown CalculateTonu(decimal tonuAmount, decimal deductions = 0m)
    {
        if (tonuAmount < TonuMinimum || tonuAmount > TonuMaximum)
            throw new ArgumentOutOfRangeException(nameof(tonuAmount), $"TONU payout must be between {TonuMinimum:C0} and {TonuMaximum:C0}.");
        if (deductions < 0) throw new ArgumentOutOfRangeException(nameof(deductions), "Enter deductions as a positive amount.");

        var gross = Round(tonuAmount);
        return Build(0m, 0m, 0m, 0m, 0m, gross, Round(deductions), isTonu: true);
    }

    /// <summary>Settlement for a stored trip.</summary>
    public static SettlementBreakdown ForTrip(Trip trip)
    {
        if (trip.Status == TripStatus.Tonu && trip.TonuAmount is > 0)
            return CalculateTonu(trip.TonuAmount.Value, trip.Deductions);
        var rate = trip.RateConfirmationAmount is > 0
            ? trip.RateConfirmationAmount.Value
            : (decimal)Math.Max(0, trip.AdminPrice > 0 ? trip.AdminPrice : trip.EstimatedPrice);
        return Calculate(rate, trip.DetentionFee, trip.LumperFee, trip.OtherAccessorials, trip.Deductions);
    }

    private static SettlementBreakdown Build(decimal rate, decimal detention, decimal lumper, decimal other,
        decimal accessorials, decimal gross, decimal deductions, bool isTonu)
    {
        var factoring = Round(gross * FactoringRate);
        var dispatch = Round(gross * DispatchRate);
        var driverNet = gross - dispatch - deductions;
        var carrierNet = gross - factoring;
        return new SettlementBreakdown(rate, detention, lumper, other, accessorials, gross, factoring, dispatch,
            deductions, driverNet, carrierNet, isTonu);
    }
}
