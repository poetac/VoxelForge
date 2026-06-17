// LcoeCalculator.cs — Sprint EC.W3 levelized-cost-of-energy helper.
//
// Standard LCOE formula (IEA definition):
//
//     LCOE = (CRF · Capex + AnnualOpex) / AnnualEnergy
//
//     CRF  = r · (1 + r)^n / ((1 + r)^n − 1)
//
// where CRF is the capital-recovery factor that amortises capex over
// the project's lifetime n years at discount rate r. Output units are
// $/kWh.
//
// This helper makes a stateless static call; pair it with a
// SystemCostBreakdown to compute LCOE for a power-generating system
// (PV, wind, fuel cell, microgrid).

using System;

namespace Voxelforge.Economics;

/// <summary>
/// Closed-form levelized-cost-of-energy calculator (Sprint EC.W3).
/// </summary>
internal static class LcoeCalculator
{
    /// <summary>
    /// Compute LCOE [$/kWh].
    /// </summary>
    /// <param name="capex_USD">Total capital expenditure [USD].</param>
    /// <param name="annualOpex_USD">Steady-state annual O&amp;M cost [USD/yr].
    /// Typically 1–3 % of capex; for PV ~ 1 %, wind ~ 2.5 %.</param>
    /// <param name="annualEnergyProduction_kWh">Annual energy delivered
    /// to the bus [kWh/yr]. For a power generator this is rated power ×
    /// capacity factor × 8760 h.</param>
    /// <param name="discountRate">r [-] — fractional annual discount
    /// rate. 0.07 is the canonical IEA default.</param>
    /// <param name="lifetimeYears">n — project life [years]. 20 yr is
    /// typical for PV / wind; 7–10 yr for batteries; 30+ for hydro.</param>
    /// <returns>LCOE [$/kWh].</returns>
    public static double Compute(
        double capex_USD,
        double annualOpex_USD,
        double annualEnergyProduction_kWh,
        double discountRate,
        int    lifetimeYears)
    {
        if (capex_USD < 0)
            throw new ArgumentOutOfRangeException(nameof(capex_USD),
                "Capex must be ≥ 0.");
        if (annualOpex_USD < 0)
            throw new ArgumentOutOfRangeException(nameof(annualOpex_USD),
                "annualOpex_USD must be ≥ 0.");
        if (annualEnergyProduction_kWh <= 0)
            throw new ArgumentOutOfRangeException(nameof(annualEnergyProduction_kWh),
                "annualEnergyProduction_kWh must be > 0.");
        if (discountRate < 0 || discountRate >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(discountRate),
                "discountRate must be in [0, 1).");
        if (lifetimeYears <= 0)
            throw new ArgumentOutOfRangeException(nameof(lifetimeYears),
                "lifetimeYears must be > 0.");

        double crf;
        if (discountRate == 0)
        {
            // Limit r → 0: CRF = 1 / n.
            crf = 1.0 / lifetimeYears;
        }
        else
        {
            double factor = Math.Pow(1.0 + discountRate, lifetimeYears);
            crf = discountRate * factor / (factor - 1.0);
        }
        return (crf * capex_USD + annualOpex_USD) / annualEnergyProduction_kWh;
    }

    /// <summary>
    /// Convenience overload: compute LCOE directly from a
    /// <see cref="SystemCostBreakdown"/> with caller-supplied opex /
    /// energy / discount.
    /// </summary>
    public static double Compute(
        SystemCostBreakdown breakdown,
        double annualOpex_USD,
        double annualEnergyProduction_kWh,
        double discountRate    = 0.07,
        int    lifetimeYears   = 20)
    {
        ArgumentNullException.ThrowIfNull(breakdown);
        return Compute(
            capex_USD:                  breakdown.TotalCapitalCost_USD,
            annualOpex_USD:             annualOpex_USD,
            annualEnergyProduction_kWh: annualEnergyProduction_kWh,
            discountRate:               discountRate,
            lifetimeYears:              lifetimeYears);
    }
}
