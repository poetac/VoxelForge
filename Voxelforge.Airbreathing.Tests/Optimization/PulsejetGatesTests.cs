// PulsejetGatesTests.cs — Wave 1 PR-4 (sub-step 1a.5).
// Per-gate unit tests for PULSEJET_BLOWOUT_LEAN + PULSEJET_ACOUSTIC_OVERPRESSURE.

using System.Linq;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Optimization;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Airbreathing.Tests.Optimization;

public sealed class PulsejetGatesTests
{
    private static AirbreathingEngineDesign V1Like(double phi = 0.95) =>
        new AirbreathingEngineDesign(
            Kind:                AirbreathingEngineKind.Pulsejet,
            InletThroatArea_m2:  0.030,
            CombustorArea_m2:    0.075,
            CombustorLength_m:   0.80,
            NozzleThroatArea_m2: 0.025,
            NozzleExitArea_m2:   0.040,
            EquivalenceRatio:    phi,
            CompressorPressureRatio: 1.0)
        with
        {
            PulsejetTubeLength_m    = 3.40,
            PulsejetIntakeArea_m2   = 0.030,
            PulsejetTailpipeArea_m2 = 0.040,
        };

    private static FlightConditions SeaLevelStaticJp8() =>
        new(0.0, 0.001, AirbreathingFuel.Jp8);

    private static FlightConditions SeaLevelStaticH2() =>
        new(0.0, 0.001, AirbreathingFuel.H2);

    [Fact]
    public void BothPulsejetGates_RegisteredInRegistry()
    {
        // Force lazy registration by accessing All.
        var ids = AirbreathingGateRegistry.Instance.All.Select(g => g.Id).ToArray();
        Assert.Contains("PULSEJET_BLOWOUT_LEAN", ids);
        Assert.Contains("PULSEJET_ACOUSTIC_OVERPRESSURE", ids);
    }

    [Fact]
    public void PulsejetBlowoutLean_NominalPhi_DoesNotFire()
    {
        // φ = 0.95 → f = 0.95 · 0.0676 = 0.0642 (above 0.030 LFL).
        var (gates, _) = AirbreathingFeasibility.Evaluate(
            V1Like(phi: 0.95), SeaLevelStaticJp8(),
            new PulsejetCycleSolver().Solve(V1Like(phi: 0.95), SeaLevelStaticJp8()).Stations);
        Assert.DoesNotContain(gates.Violations,
            v => v.ConstraintId == "PULSEJET_BLOWOUT_LEAN");
    }

    [Fact]
    public void PulsejetBlowoutLean_LeanPhi_Fires()
    {
        // φ = 0.30 → f = 0.30 · 0.0676 = 0.0203 (below 0.030 LFL).
        // φ ≥ 0.20 keeps the existing COMBUSTOR_BLOWOUT_LEAN silent so we
        // can isolate the pulsejet-specific gate.
        var design = V1Like(phi: 0.30);
        var stations = new PulsejetCycleSolver().Solve(design, SeaLevelStaticJp8()).Stations;
        var (gates, _) = AirbreathingFeasibility.Evaluate(design, SeaLevelStaticJp8(), stations);
        Assert.Contains(gates.Violations,
            v => v.ConstraintId == "PULSEJET_BLOWOUT_LEAN");
        Assert.False(gates.IsFeasible);
    }

    [Fact]
    public void PulsejetBlowoutLean_H2Fuel_DoesNotFireRegardlessOfPhi()
    {
        // H₂ has LFL ~0.003 mass fraction, far below the hydrocarbon
        // 0.030. The gate self-guards on Fuel != H2 to avoid spurious
        // firings.
        var design = V1Like(phi: 0.30);
        var stations = new PulsejetCycleSolver().Solve(design, SeaLevelStaticH2()).Stations;
        var (gates, _) = AirbreathingFeasibility.Evaluate(design, SeaLevelStaticH2(), stations);
        Assert.DoesNotContain(gates.Violations,
            v => v.ConstraintId == "PULSEJET_BLOWOUT_LEAN");
    }

    [Fact]
    public void PulsejetBlowoutLean_NotPulsejet_DoesNotFire()
    {
        // Self-guard on Kind=Pulsejet — ramjet design at lean phi must
        // not trip the pulsejet gate.
        var design = new AirbreathingEngineDesign(
            AirbreathingEngineKind.Ramjet,
            0.030, 0.075, 0.80, 0.025, 0.040, 0.30, 1.0);
        var stations = new RamjetCycleSolver().Solve(design, SeaLevelStaticJp8()).Stations;
        var (gates, _) = AirbreathingFeasibility.Evaluate(design, SeaLevelStaticJp8(), stations);
        Assert.DoesNotContain(gates.Violations,
            v => v.ConstraintId == "PULSEJET_BLOWOUT_LEAN");
    }

    [Fact]
    public void PulsejetAcousticOverpressure_HighTempRatio_FiresAdvisory()
    {
        // V-1 nominal at φ = 0.95 produces T_t4 well above 2000 K (very rich
        // pulsejet); peak/steady ratio exceeds 1.30 advisory ceiling.
        var design = V1Like(phi: 0.95);
        var stations = new PulsejetCycleSolver().Solve(design, SeaLevelStaticJp8()).Stations;
        var (gates, advisories) = AirbreathingFeasibility.Evaluate(
            design, SeaLevelStaticJp8(), stations);

        // Fires as ADVISORY, not hard — IsFeasible stays true.
        Assert.Contains(advisories, v => v.ConstraintId == "PULSEJET_ACOUSTIC_OVERPRESSURE");
        Assert.DoesNotContain(gates.Violations,
            v => v.ConstraintId == "PULSEJET_ACOUSTIC_OVERPRESSURE");
    }

    [Fact]
    public void PulsejetAcousticOverpressure_NotPulsejet_DoesNotFire()
    {
        // Ramjet at the same lean phi: gate self-guards on Kind=Pulsejet.
        var design = new AirbreathingEngineDesign(
            AirbreathingEngineKind.Ramjet,
            0.030, 0.075, 0.80, 0.025, 0.040, 0.95, 1.0);
        var stations = new RamjetCycleSolver().Solve(design, SeaLevelStaticJp8()).Stations;
        var (_, advisories) = AirbreathingFeasibility.Evaluate(
            design, SeaLevelStaticJp8(), stations);
        Assert.DoesNotContain(advisories,
            v => v.ConstraintId == "PULSEJET_ACOUSTIC_OVERPRESSURE");
    }
}
