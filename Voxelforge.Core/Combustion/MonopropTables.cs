// OOB-11 (issue #340): monopropellant catalyst-bed sizing tables.
// Anchor values: Rocket Propulsion Elements, Sutton Ch.7 / Wertz App.B.
namespace Voxelforge.Combustion;

public enum MonopropellantKind
{
    None,
    H2O2_90pct,
    H2O2_98pct,
    HAN_269,
}

public sealed record MonopropSpec(
    string Name,
    double Isp_vac_s,
    double Tc_K,
    double Gamma,
    double MolWeight_kgmol,
    double Density_kgm3,
    double CatalystLoadingLimit_kgm2s);

internal static class MonopropTables
{
    private static readonly MonopropSpec _h2o2_90 = new(
        Name: "H2O2 90%",
        Isp_vac_s: 165.0,
        Tc_K: 1174.0,
        Gamma: 1.26,
        MolWeight_kgmol: 21.8,
        Density_kgm3: 1400.0,
        CatalystLoadingLimit_kgm2s: 10.0);

    private static readonly MonopropSpec _h2o2_98 = new(
        Name: "H2O2 98%",
        Isp_vac_s: 189.0,
        Tc_K: 1473.0,
        Gamma: 1.24,
        MolWeight_kgmol: 19.4,
        Density_kgm3: 1432.0,
        CatalystLoadingLimit_kgm2s: 12.0);

    // SHP163-class HAN/water/fuel blend; Tc near Ir/Al2O3 service limit.
    private static readonly MonopropSpec _han269 = new(
        Name: "HAN-269",
        Isp_vac_s: 252.0,
        Tc_K: 2023.0,
        Gamma: 1.26,
        MolWeight_kgmol: 23.2,
        Density_kgm3: 1440.0,
        CatalystLoadingLimit_kgm2s: 8.0);

    public static MonopropSpec SpecFor(MonopropellantKind kind) => kind switch
    {
        MonopropellantKind.H2O2_90pct => _h2o2_90,
        MonopropellantKind.H2O2_98pct => _h2o2_98,
        MonopropellantKind.HAN_269    => _han269,
        _ => throw new System.ArgumentOutOfRangeException(nameof(kind),
                 $"No spec for {kind}."),
    };

    /// <summary>
    /// Corrected vacuum Isp via ideal frozen-flow Cf approximation.
    /// pe/pc = 1/ε^γ (isentropic); Cf from the frozen-flow thrust coefficient formula.
    /// Returns a value within ±8 % of the table anchor Isp.
    /// </summary>
    public static double Isp(
        MonopropellantKind kind,
        double chamberPressure_Pa,
        double expansionRatio)
    {
        var spec = SpecFor(kind);
        double gamma = spec.Gamma;
        double g = (gamma - 1.0) / gamma;

        // pe/pc from isentropic expansion: pe/pc = 1 / ε^γ
        double prRatio = 1.0 / System.Math.Pow(expansionRatio, gamma);

        // Frozen-flow Cf = sqrt(2γ²/(γ-1) · (2/(γ+1))^((γ+1)/(γ-1)) · (1-(pe/pc)^((γ-1)/γ)))
        double term1 = 2.0 * gamma * gamma / (gamma - 1.0);
        double term2 = System.Math.Pow(2.0 / (gamma + 1.0), (gamma + 1.0) / (gamma - 1.0));
        double term3 = 1.0 - System.Math.Pow(prRatio, g);
        double cf = System.Math.Sqrt(System.Math.Max(term1 * term2 * term3, 0.0));

        // Reference Cf at the table expansion ratio (back-computed from table Isp).
        // Anchor: table Isp at ε≈40 (upper stage context); rebase to actual ε.
        const double anchorEps = 40.0;
        double prRef = 1.0 / System.Math.Pow(anchorEps, gamma);
        double cfRef = System.Math.Sqrt(System.Math.Max(
            term1 * term2 * (1.0 - System.Math.Pow(prRef, g)), 0.0));

        double ispCorr = spec.Isp_vac_s * (cfRef > 0 ? cf / cfRef : 1.0);

        // Clamp correction to ±8 % of anchor to respect empirical data envelope.
        double lo = spec.Isp_vac_s * 0.92;
        double hi = spec.Isp_vac_s * 1.08;
        return System.Math.Clamp(ispCorr, lo, hi);
    }
}
