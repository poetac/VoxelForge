// RdeCombustorEnergyBalanceRegressionTests.cs — regression guard for the RDE
// combustor energy-balance bug (red-team finding).
//
// The RDE solver heated the combustor with f·η_b·LHV but omitted the (1+f)
// divisor that accounts for the added fuel mass:
//
//     OLD:  T_t4 = T_t2 + f·η_b·LHV/cp                 (mass NOT conserved)
//     NEW:  T_t4 = (T_t2 + f·η_b·LHV/cp) / (1 + f)     (mass conserved)
//
// Every sibling cycle solver (turbojet, ramjet, scramjet, …) carries the
// (1+f) factor; the RDE path did not, over-predicting combustor-exit total
// temperature by a factor of (1+f) — ≈3 % for H₂, ≈7 % for Jet-A. These
// tests pin the corrected energy balance and fail on the old code.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class RdeCombustorEnergyBalanceRegressionTests
{
    // Mirror the RDE-solver constants the test reconstructs the balance from.
    // f_stoich + LHV match the solver's internal H₂/air switch (private there).
    private const double H2FStoich = 0.0291;
    private const double H2Lhv     = 120e6;   // 120 MJ/kg LH2
    private const double Cp        = RotatingDetonationCycleSolver.HotSideCp_JkgK;
    private const double EtaB      = RotatingDetonationCycleSolver.CombustionEfficiency;

    private static AirbreathingEngineDesign RdeDesign(double phi) => new(
        Kind: AirbreathingEngineKind.RotatingDetonation,
        InletThroatArea_m2:  0.05,
        CombustorArea_m2:    0.30,
        CombustorLength_m:   0.50,
        NozzleThroatArea_m2: 0.020,
        NozzleExitArea_m2:   0.100,
        EquivalenceRatio:    phi)
    {
        RdePressureGainRatio      = 1.25,
        RdeWaveCount              = 4,
        RdeAnnularOuterDiameter_m = 0.150,
        RdeAnnularInnerDiameter_m = 0.110,
        RdeAnnularLength_m        = 0.150,
    };

    private static FlightConditions Cond() => new(10_000.0, 2.0, AirbreathingFuel.H2);

    [Fact]
    public void CombustorEnergyBalance_ConservesEnergyWithFuelMassAddition()
    {
        const double phi = 0.50;
        var r = new RotatingDetonationCycleSolver().Solve(RdeDesign(phi), Cond());

        double f   = phi * H2FStoich;
        double Tt2 = r.Stations.Station(2).StagnationT_K;
        double Tt4 = r.Stations.Station(4).StagnationT_K;

        // Energy in (per unit air mass) = enthalpy of the incoming air plus the
        // released fuel energy; energy out heats the combined (1+f) mass:
        //     (1+f)·cp·T_t4 = cp·T_t2 + f·η_b·LHV
        double lhs = (1.0 + f) * Cp * Tt4;
        double rhs = Cp * Tt2 + f * EtaB * H2Lhv;

        // The OLD code violated this by exactly f·rhs (≈1.5 % here); a tight
        // relative tolerance fails on it and passes on the (1+f) fix.
        Assert.True(Math.Abs(lhs - rhs) <= 1e-6 * rhs,
            $"combustor energy balance violated: lhs={lhs:E6}, rhs={rhs:E6}, "
          + $"rel-err={Math.Abs(lhs - rhs) / rhs:E3}");
    }

    [Fact]
    public void CombustorExitTemp_IsBelowNoMassAdditionValueByOnePlusF()
    {
        const double phi = 0.50;
        var r = new RotatingDetonationCycleSolver().Solve(RdeDesign(phi), Cond());

        double f   = phi * H2FStoich;
        double Tt2 = r.Stations.Station(2).StagnationT_K;
        double Tt4 = r.Stations.Station(4).StagnationT_K;

        // The old (buggy) value — what T_t4 would be without the (1+f) divisor.
        double tt4NoMassAddition = Tt2 + f * EtaB * H2Lhv / Cp;

        Assert.True(Tt4 < tt4NoMassAddition,
            $"T_t4 must sit below the no-mass-addition value; "
          + $"got {Tt4:F1} K vs {tt4NoMassAddition:F1} K");

        // The corrected T_t4 is exactly the old value divided by (1+f).
        Assert.Equal(1.0 + f, tt4NoMassAddition / Tt4, 6);
    }
}
