// LaceFiniteExpansionRegressionTests.cs — regression guard for the LACE
// finite-expansion bug (red-team finding).
//
// LaceCycleSolver computed the exit velocity from the *vacuum* (infinite-area-
// ratio) limit, V_eq_vac = η_eff·√(2γ/(γ−1)·R·T_c), dropping the finite-
// expansion factor √(1 − (P_e/P_c)^((γ−1)/γ)) that its own docstring lists and
// that every rocket-style cycle carries. At the cluster area ratios this
// over-predicted exit velocity by ~20-30 % (and thrust/Isp more, after ram-drag
// subtraction). The solver now inverts the isentropic area-Mach relation for
// P_e/P_c at the design ε = A_e/A_t and applies the factor.
//
// This test backs the effective exit velocity out of the thrust decomposition
// F_net = ṁ_total·V_e − ṁ_air·V_∞ and confirms it sits well below the vacuum
// limit by the factor implied by the (now self-consistent) reported exit
// pressure ratio. It fails on the old code (V_e ≈ vacuum limit) and passes now.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class LaceFiniteExpansionRegressionTests
{
    private static AirbreathingEngineDesign LaceDesign() => new(
        Kind: AirbreathingEngineKind.LiquidAirCycle,
        InletThroatArea_m2:  0.50,
        CombustorArea_m2:    0.30,
        CombustorLength_m:   0.50,
        NozzleThroatArea_m2: 0.05,
        NozzleExitArea_m2:   1.50,   // ε = A_e/A_t = 30
        EquivalenceRatio:    0.0)
    {
        PrecoolerEffectiveness  = 0.95,
        LH2MassFlow_kgs         = 4.0,
        LaceChamberPressure_bar = 70.0,
        LaceAirToFuelRatio      = 8.0,
    };

    private static FlightConditions Cond() => new(25_000.0, 5.0, AirbreathingFuel.H2);

    [Fact]
    public void ExitVelocity_AppliesFiniteExpansionFactor_NotVacuumLimit()
    {
        var cond = Cond();
        var r = new LaceCycleSolver().Solve(LaceDesign(), cond);

        double fNet      = r.Stations.ThrustNet_N;
        double mDotAir   = r.Stations.Station(0).MassFlow_kg_s;
        double mDotTotal = r.Stations.Station(4).MassFlow_kg_s;
        double Tc        = r.Stations.Station(4).StagnationT_K;
        double Pc        = r.Stations.Station(4).StagnationP_Pa;
        double Pe        = r.Stations.Station(9).StagnationP_Pa;

        var atm    = StandardAtmosphere.At(cond.Altitude_m);
        double Vinf = cond.MachNumber * atm.SpeedOfSound_m_s;

        // F_net = ṁ_total·V_e − ṁ_air·V_∞  ⇒  V_e = (F_net + ṁ_air·V_∞)/ṁ_total.
        double Ve = (fNet + mDotAir * Vinf) / mDotTotal;

        const double g = LaceCycleSolver.GammaChamber;            // 1.20
        const double R = LaceCycleSolver.GasConstantChamber_JkgK; // 360
        double VeqVac = LaceCycleSolver.EffectiveIspEfficiency
                      * Math.Sqrt(2.0 * g / (g - 1.0) * R * Tc);

        // The finite-expansion factor implied by the reported exit pressure
        // ratio (station 9 is now consistent with the velocity calc).
        double expectedFactor = Math.Sqrt(1.0 - Math.Pow(Pe / Pc, (g - 1.0) / g));

        // (1) V_e must be well below the vacuum (infinite-ε) limit. The old
        //     code took V_e at the vacuum limit (factor ≈ 1).
        Assert.True(Ve < 0.90 * VeqVac,
            $"V_e={Ve:F1} m/s should be well below the vacuum limit "
          + $"{VeqVac:F1} m/s; actual factor {Ve / VeqVac:F3}");

        // (2) The realised factor must match the area-ratio expansion (within
        //     the small ambient back-pressure correction).
        Assert.Equal(expectedFactor, Ve / VeqVac, 2);
    }
}
