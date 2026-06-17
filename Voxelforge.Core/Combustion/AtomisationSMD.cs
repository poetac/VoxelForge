// AtomisationSMD.cs — Rizk-Lefebvre SMD (Sauter Mean Diameter)
// correlation for coaxial / airblast / pressure-shear injector
// elements. Used to overlay spray quality on the Pareto scatter so
// designs can be ranked by atomisation in addition to mass.
//
// Correlation (airblast / shear):
//
//   SMD = 0.48 · D · (σ / (ρ_G · U_rel² · D))^0.4
//       + 0.15 · D · (μ_L² / (σ · ρ_L · D))^0.5
//
// where
//   D      = characteristic orifice diameter (m)
//   σ      = fuel surface tension (N/m)
//   μ_L    = fuel dynamic viscosity (kg/(m·s))
//   ρ_L    = fuel liquid density (kg/m³)
//   ρ_G    = oxidiser (or chamber gas) density (kg/m³)
//   U_rel  = relative velocity between fuel + ox streams (m/s)
//
// References:
//   Lefebvre, "Atomization and Sprays" (2017), §6.2.
//   Rizk & Lefebvre, "Influence of Airblast Atomizer Design Features on
//     Mean Drop Size," AIAA Journal 21, no. 8 (1983): 1139-1142.
//   Hulka, "Classical Pressure-Swirl Injector Design," AIAA 2010-6720,
//     Fig. 5 for reference SMD ranges on LOX/hydrocarbon engines
//     (50–150 µm typical for well-designed coaxial shear).
//
// MVP scope:
//   • One function, `Compute`, taking injection-side properties + a
//     <see cref="Injector.Elements.OrificeResult"/> + pair-specific
//     fuel properties from <see cref="FuelFluidProperties"/>.
//   • Returns SMD in micrometres (µm) — the conventional report unit.
//   • Pair coverage: LOX/CH4, LOX/H2, LOX/RP-1 (hardcoded σ, μ_L
//     values at injection saturation temperatures). Other pairs return
//     NaN with a note so the caller can soft-degrade.
//   • No breakup-length model, no spray angle, no Sauter-distribution
//     shape — those are Tier C follow-ons when someone needs them.

namespace Voxelforge.Combustion;

/// <summary>
/// Fuel / oxidiser properties at injection conditions, used by
/// <see cref="AtomisationSMD.Compute"/>. Values are representative
/// saturation-pressure averages — precise to ±10 % across the usable
/// injection-pressure / temperature band for each pair.
/// </summary>
public readonly record struct FuelFluidProperties(
    double FuelSurfaceTension_Nm,    // σ, N/m
    double FuelViscosity_kgms,       // μ_L, kg/(m·s)
    double FuelDensity_kgm3,         // ρ_L, kg/m³
    double OxidiserDensity_kgm3)     // ρ_G (effectively ρ_ox at injection), kg/m³
{
    /// <summary>
    /// Fluid-property lookup by propellant pair. Covers the three
    /// implemented pairs; others return `default` (all zeros) so the
    /// caller can gate on <see cref="FuelSurfaceTension_Nm"/> &gt; 0.
    /// </summary>
    public static FuelFluidProperties For(PropellantPair pair) => pair switch
    {
        // LOX/CH4: LCH4 at 120 K (saturation at ~1.5 MPa)
        //   σ = 0.018 N/m (Sych 1991), μ_L = 1.2e-4, ρ_L = 420, ρ_ox ≈ 1140
        PropellantPair.LOX_CH4 => new(0.018, 1.2e-4, 420.0, 1140.0),

        // LOX/H2: LH2 at 25 K
        //   σ = 0.0020 N/m, μ_L = 1.3e-5, ρ_L = 71, ρ_ox ≈ 1140
        PropellantPair.LOX_H2  => new(0.0020, 1.3e-5, 71.0, 1140.0),

        // LOX/RP-1: RP-1 at 290 K
        //   σ = 0.027 N/m, μ_L = 1.8e-3, ρ_L = 810, ρ_ox ≈ 1140
        PropellantPair.LOX_RP1 => new(0.027, 1.8e-3, 810.0, 1140.0),

        _ => default,
    };
}

/// <summary>
/// Rizk-Lefebvre SMD correlation. Pure math; no PicoGK dependency.
/// </summary>
public static class AtomisationSMD
{
    /// <summary>
    /// Sauter Mean Diameter in micrometres from the classical
    /// Rizk-Lefebvre airblast correlation. Returns
    /// <see cref="double.NaN"/> when the fuel properties are
    /// unpopulated (unsupported propellant pair) or the relative
    /// velocity is non-positive.
    /// </summary>
    /// <param name="fluidProps">Fluid bundle via <see cref="FuelFluidProperties.For"/>.</param>
    /// <param name="oxVelocity_ms">Ox orifice exit velocity (m/s).</param>
    /// <param name="fuelVelocity_ms">Fuel orifice exit velocity (m/s).</param>
    /// <param name="characteristicDiameter_m">
    /// Characteristic orifice diameter in metres. Use the fuel
    /// equivalent-circle diameter for coax + impinging; the pintle
    /// post diameter for pintle; the discharge diameter for swirl.
    /// </param>
    public static double Compute(
        FuelFluidProperties fluidProps,
        double              oxVelocity_ms,
        double              fuelVelocity_ms,
        double              characteristicDiameter_m)
    {
        if (fluidProps.FuelSurfaceTension_Nm <= 0) return double.NaN;
        if (characteristicDiameter_m       <= 0)   return double.NaN;

        double D     = characteristicDiameter_m;
        double sigma = fluidProps.FuelSurfaceTension_Nm;
        double muL   = fluidProps.FuelViscosity_kgms;
        double rhoL  = fluidProps.FuelDensity_kgm3;
        double rhoG  = fluidProps.OxidiserDensity_kgm3;
        double uRel  = Math.Abs(fuelVelocity_ms - oxVelocity_ms);
        if (uRel < 1e-3) return double.NaN;

        double weberInv = sigma / Math.Max(rhoG * uRel * uRel * D, 1e-12);
        double ohnesorgeSq = (muL * muL) / Math.Max(sigma * rhoL * D, 1e-12);

        double smd_m = 0.48 * D * Math.Pow(weberInv, 0.4)
                     + 0.15 * D * Math.Pow(ohnesorgeSq, 0.5);
        return smd_m * 1e6;   // metres → µm
    }

    /// <summary>
    /// Convenience wrapper: pull per-element velocities + fuel diameter
    /// from an <see cref="Injector.Elements.OrificeResult"/> and apply
    /// <see cref="Compute"/>. Returns NaN when the element result
    /// reports zero-area orifices.
    /// </summary>
    public static double ComputeFromElementResult(
        PropellantPair                        pair,
        Injector.Elements.OrificeResult       orifice)
    {
        var props = FuelFluidProperties.For(pair);
        if (orifice.FuelEquivDiameter_mm <= 0) return double.NaN;
        return Compute(
            fluidProps:              props,
            oxVelocity_ms:           orifice.OxVelocity_ms,
            fuelVelocity_ms:         orifice.FuelVelocity_ms,
            characteristicDiameter_m: orifice.FuelEquivDiameter_mm * 1e-3);
    }

    /// <summary>
    /// Qualitative label for a given SMD — lets the scatter panel and
    /// report surface something more actionable than a raw µm number.
    ///   &lt; 30 µm   Excellent (high c* efficiency expected)
    ///   30-80 µm   Good      (typical well-designed coax / doublet)
    ///   80-150 µm  Marginal  (increased chug susceptibility)
    ///   &gt; 150 µm Poor      (recompute element count / ΔP_inj)
    /// </summary>
    public static string QualitativeLabel(double smd_um)
    {
        if (double.IsNaN(smd_um) || smd_um <= 0) return "n/a";
        if (smd_um < 30)   return "Excellent";
        if (smd_um < 80)   return "Good";
        if (smd_um < 150)  return "Marginal";
        return "Poor";
    }
}
