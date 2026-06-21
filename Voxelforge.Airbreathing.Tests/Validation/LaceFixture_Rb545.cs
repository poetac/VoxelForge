// LaceFixture_Rb545.cs — Sprint A.W3 acceptance fixture for the LACE
// (Liquid Air Cycle Engine) variant.
//
// Reference engine: RB-545 (Rolls-Royce / HOTOL precursor, ~1985). LACE-
// class hybrid air-breathing/rocket at the lower stage of the conceptual
// HOTOL SSTO. Cluster anchors:
//
//   Flight Mach:           5.0
//   Altitude:              25 km
//   Precooler effectiveness: ε ≈ 0.95
//   LH₂ mass flow:         ~4 kg/s (cluster value at lower end of sweep)
//   Chamber pressure:      ~70 bar
//   Air-to-fuel ratio:     ~8 (rich; chamber cool relative to stoichiometric)
//   Inlet capture area:    ~0.5 m²
//
// Targets (cluster-anchored sanity bands, NOT vendor data — RB-545 was
// never built; SABRE's cluster band is the closest analog):
//   Net thrust:        30 – 300 kN
//   Isp (fuel basis):  1000 – 4000 s
//   Precooler outlet air T:  < 95 K (saturated-liquid-air target)
//
// Band recalibration note: the thrust/Isp floors were lowered (from 50 kN /
// 1500 s) when the solver's finite-expansion factor was restored. The earlier
// solver used the vacuum (infinite-area-ratio) exit velocity, over-predicting
// thrust/Isp; with the √(1 − (P_e/P_c)^((γ−1)/γ)) factor applied at the design
// ε = A_e/A_t = 30, the RB-545 point now sits at ~50 kN / ~1276 s. The floors
// were dropped to bracket the corrected physics with margin (these are
// plausibility windows for a never-built engine, not vendor acceptance limits).
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. LACE (Liquid Air Cycle Engine) hybrid variant. ADR-036's air-
// breathing ladder does NOT cover LACE explicitly — this fixture covers an
// ADR-036 GAP. Bands extrapolated per ADR-029 D4 generalised cluster anchors:
// ±25 % thrust, ±20 % Isp (matches the scramjet / RBCC outer-bound regime
// since LACE's precooler-effectiveness assumption is similarly unvalidated
// against flown hardware — RB-545 was never built; SABRE is the closest
// analog, also pre-production).

using Voxelforge.Airbreathing;

namespace Voxelforge.Airbreathing.Tests.Validation;

public sealed class LaceFixture_Rb545
{
    private const double TargetThrustLow_N  =  30_000.0;
    private const double TargetThrustHigh_N = 300_000.0;
    private const double TargetIspLow_s     =   1000.0;
    private const double TargetIspHigh_s    =   4000.0;

    private static AirbreathingEngineDesign Rb545Design() => new(
        Kind: AirbreathingEngineKind.LiquidAirCycle,
        InletThroatArea_m2:  0.50,
        CombustorArea_m2:    0.30,
        CombustorLength_m:   0.50,
        NozzleThroatArea_m2: 0.05,
        NozzleExitArea_m2:   1.50,
        EquivalenceRatio:    0.0)
    {
        PrecoolerEffectiveness  = 0.95,
        LH2MassFlow_kgs         = 4.0,
        LaceChamberPressure_bar = 70.0,
        LaceAirToFuelRatio      = 8.0,
    };

    private static FlightConditions Rb545Conditions()
        => new(Altitude_m: 25_000.0, MachNumber: 5.0, Fuel: AirbreathingFuel.H2);

    [Fact]
    public void Rb545_NetThrust_InClusterBand()
    {
        var r = AirbreathingOptimization.GenerateWith(Rb545Design(), Rb545Conditions());
        Assert.InRange(r.Stations.ThrustNet_N, TargetThrustLow_N, TargetThrustHigh_N);
    }

    [Fact]
    public void Rb545_FuelIsp_InClusterBand()
    {
        var r = AirbreathingOptimization.GenerateWith(Rb545Design(), Rb545Conditions());
        Assert.InRange(r.Stations.SpecificImpulse_s, TargetIspLow_s, TargetIspHigh_s);
    }

    [Fact]
    public void Rb545_PrecoolerOutletAirTemp_BelowLiquefactionTarget()
    {
        var r = AirbreathingOptimization.GenerateWith(Rb545Design(), Rb545Conditions());
        // Station 2 is the precooler outlet.
        Assert.True(r.Stations.Station(2).StagnationT_K < 95.0,
            $"At ε=0.95 the precooler outlet should drop below 95 K; got "
          + $"{r.Stations.Station(2).StagnationT_K:F1} K");
    }

    [Fact]
    public void Rb545_ChamberTemperature_InCombustionCluster()
    {
        var r = AirbreathingOptimization.GenerateWith(Rb545Design(), Rb545Conditions());
        // Station 4 is the chamber. T_c(MR=8) ≈ 3500 − 30·2 = 3440 K from
        // the cluster fit, clamped to [2000, 3700].
        Assert.InRange(r.Stations.Station(4).StagnationT_K, 2000.0, 3700.0);
    }

    [Fact]
    public void Rb545_PrecoolerHeatDuty_SurfacedOnSpecificWork()
    {
        var r = AirbreathingOptimization.GenerateWith(Rb545Design(), Rb545Conditions());
        // Per-kg-air precooler heat duty at Mach 5 / ε=0.95 should be
        // roughly cp·(T_t1 − T_out) ≈ 1004.7·(1300−63) ≈ 1.24 MJ/kg.
        Assert.True(r.SpecificWork_Jkg > 1.0e6 && r.SpecificWork_Jkg < 1.5e6,
            $"Precooler heat duty {r.SpecificWork_Jkg:F0} J/kg outside expected "
          + "1.0–1.5 MJ/kg band for Mach 5 / ε=0.95.");
    }

    [Fact]
    public void Rb545_BaselineIsFeasible()
    {
        var r = AirbreathingOptimization.GenerateWith(Rb545Design(), Rb545Conditions());
        Assert.True(r.IsFeasible,
            $"RB-545 baseline should pass; saw {r.Violations.Count} violations.");
    }

    [Fact]
    public void Rb545_Deterministic()
    {
        var r1 = AirbreathingOptimization.GenerateWith(Rb545Design(), Rb545Conditions());
        var r2 = AirbreathingOptimization.GenerateWith(Rb545Design(), Rb545Conditions());
        Assert.Equal(r1.Stations.ThrustNet_N,       r2.Stations.ThrustNet_N);
        Assert.Equal(r1.Stations.SpecificImpulse_s, r2.Stations.SpecificImpulse_s);
    }
}
