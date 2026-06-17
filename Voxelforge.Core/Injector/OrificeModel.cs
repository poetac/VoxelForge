// OrificeModel.cs — Classical sharp-edged orifice sizing model.
//
// Reference: Huzel & Huang, AIAA Vol. 147, Ch. 8 §8.1:
//
//   Volumetric flow rate: Q = Cd · A · √(2·ΔP/ρ)     [m³/s]
//   Mass flow rate:       ṁ = Cd · A · √(2·ρ·ΔP)     [kg/s]
//   Orifice area:         A = ṁ / (Cd · √(2·ρ·ΔP))   [m²]
//   Jet velocity:         V = Cd · √(2·ΔP/ρ)          [m/s]
//
// Typical Cd for sharp-edged orifices: 0.61–0.72. Default 0.70 chosen as
// a common preliminary-design value for drilled injector orifices.
//
// Preliminary-design fidelity: single-phase, incompressible. Cavitation,
// compressibility, two-phase (LOX near sat.), and secondary flow
// contraction effects are not modelled.

namespace Voxelforge.Injector;

public static class OrificeModel
{
    /// <summary>
    /// Default discharge coefficient for a sharp-edged drilled orifice.
    /// Typical range 0.61–0.72; use 0.70 for preliminary design.
    /// </summary>
    public const double DefaultCd = 0.70;

    /// <summary>
    /// Propellant injection densities at typical engine inlet conditions.
    /// Used when the caller does not supply a measured value.
    /// Values are representative of saturated/subcooled liquid at
    /// engine inlet pressures (not critical-point conditions).
    /// </summary>
    public static class ReferenceDensity_kgm3
    {
        public const double LOX   = 1140.0;   // Sat. LOX ~90 K
        public const double LCH4  =  430.0;   // Subcooled LCH4 ~130 K
        public const double LH2   =   73.0;   // Sat. LH2 ~20 K
        public const double RP1   =  820.0;   // RP-1 at ~300 K
        public const double MMH   =  870.0;   // MMH at ~298 K (stub)
        public const double H2O2  = 1420.0;   // 98% H2O2 at ~298 K (stub)
    }

    // A5 (post-Phase-6 physics audit): per-fluid viscosity anchors at the
    // same reference states as ReferenceDensity_kgm3. Used by the feed-system
    // pressure stackup so Reynolds + Darcy-Weisbach friction predictions
    // are fluid-aware. Pre-A5, LineLoss.FrictionDP defaulted μ = 3e-4 Pa·s
    // (the LOX value), giving 25× too-high μ on LH2 lines and silently
    // distorting Re into the wrong friction-regime boundary.
    public static class ReferenceViscosity_PaS
    {
        public const double LOX   = 2.0e-4;   // Sat. LOX ~90 K (NIST)
        public const double LCH4  = 1.3e-4;   // Subcooled LCH4 ~130 K (NIST)
        public const double LH2   = 1.3e-5;   // Sat. LH2 ~20 K (NIST)
        public const double RP1   = 1.7e-3;   // RP-1 at ~300 K (Sutton 9e §7.2)
        public const double MMH   = 7.7e-4;   // MMH at ~298 K
        public const double H2O2  = 1.25e-3;  // 98% H2O2 at ~298 K
    }

    /// <summary>
    /// Representative injection viscosities for a given propellant pair.
    /// Returns (oxidiserViscosity_PaS, fuelViscosity_PaS) at the same
    /// reference states used by <see cref="InjectionDensities"/>.
    /// </summary>
    public static (double OxViscosity_PaS, double FuelViscosity_PaS)
        InjectionViscosities(Combustion.PropellantPair pair) => pair switch
    {
        Combustion.PropellantPair.LOX_CH4  => (ReferenceViscosity_PaS.LOX,  ReferenceViscosity_PaS.LCH4),
        Combustion.PropellantPair.LOX_H2   => (ReferenceViscosity_PaS.LOX,  ReferenceViscosity_PaS.LH2),
        Combustion.PropellantPair.LOX_RP1  => (ReferenceViscosity_PaS.LOX,  ReferenceViscosity_PaS.RP1),
        Combustion.PropellantPair.N2O4_MMH => (ReferenceViscosity_PaS.H2O2, ReferenceViscosity_PaS.MMH),
        Combustion.PropellantPair.H2O2_RP1 => (ReferenceViscosity_PaS.H2O2, ReferenceViscosity_PaS.RP1),
        _ => (ReferenceViscosity_PaS.LOX, ReferenceViscosity_PaS.LCH4),
    };

    // ─────────────────────────────────────────────────────────────────
    //  Core formula
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Actual jet velocity for a single-phase orifice:
    ///   V = Cd · √(2·ΔP/ρ)
    /// </summary>
    public static double JetVelocity_ms(double deltaPInj_Pa, double density_kgm3, double cd = DefaultCd)
        => cd * System.Math.Sqrt(2.0 * deltaPInj_Pa / System.Math.Max(density_kgm3, 1.0));

    /// <summary>
    /// Required orifice area for a given mass flow target:
    ///   A = ṁ / (Cd · √(2·ρ·ΔP))     [m²]
    /// Returns m² (SI); convert to mm² with ×1e6.
    /// </summary>
    public static double OrificeArea_m2(
        double massFlow_kgs, double deltaPInj_Pa, double density_kgm3, double cd = DefaultCd)
    {
        double denom = cd * System.Math.Sqrt(2.0 * density_kgm3 * System.Math.Max(deltaPInj_Pa, 1.0));
        return massFlow_kgs / System.Math.Max(denom, 1e-20);
    }

    /// <summary>
    /// Required orifice area in mm².
    /// </summary>
    public static double OrificeArea_mm2(
        double massFlow_kgs, double deltaPInj_Pa, double density_kgm3, double cd = DefaultCd)
        => OrificeArea_m2(massFlow_kgs, deltaPInj_Pa, density_kgm3, cd) * 1e6;

    /// <summary>
    /// Diameter of a single circular orifice with the required area [mm].
    /// </summary>
    public static double OrificeDiameter_mm(
        double massFlow_kgs, double deltaPInj_Pa, double density_kgm3, double cd = DefaultCd)
    {
        double area_mm2 = OrificeArea_mm2(massFlow_kgs, deltaPInj_Pa, density_kgm3, cd);
        return 2.0 * System.Math.Sqrt(area_mm2 / System.Math.PI);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Propellant density lookup by pair
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Representative injection densities for a given propellant pair.
    /// Returns (oxidiserDensity_kgm3, fuelDensity_kgm3).
    /// </summary>
    public static (double OxDensity_kgm3, double FuelDensity_kgm3)
        InjectionDensities(Combustion.PropellantPair pair) => pair switch
    {
        Combustion.PropellantPair.LOX_CH4  => (ReferenceDensity_kgm3.LOX,  ReferenceDensity_kgm3.LCH4),
        Combustion.PropellantPair.LOX_H2   => (ReferenceDensity_kgm3.LOX,  ReferenceDensity_kgm3.LH2),
        Combustion.PropellantPair.LOX_RP1  => (ReferenceDensity_kgm3.LOX,  ReferenceDensity_kgm3.RP1),
        Combustion.PropellantPair.N2O4_MMH => (ReferenceDensity_kgm3.H2O2, ReferenceDensity_kgm3.MMH),
        Combustion.PropellantPair.H2O2_RP1 => (ReferenceDensity_kgm3.H2O2, ReferenceDensity_kgm3.RP1),
        _ => (ReferenceDensity_kgm3.LOX, ReferenceDensity_kgm3.LCH4),
    };
}
