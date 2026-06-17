// Phase6FollowOnTests.cs — Contract tests for the follow-on sprint:
//   • Pareto CSV serialiser + format consistency
//   • Manifold ΔP split (entrance / friction / exit) on RegenSolverOutputs
//   • Independent ox / fuel valve ramps in StartTransientSim
//   • Ox / fuel ΔP separation in ChugAnalysis
//   • Doublet impingement angle as design variable
//
// Each item is constrained, audit-traceable, and covered by a small
// number of focused theories / facts. Back-compat defaults are
// asserted so legacy callers see no behavioural change.

using System.Globalization;
using System.IO;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class Phase6FollowOnTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Pareto CSV serialiser (centralised in ParetoFront.SaveToCsv)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ParetoCsv_Header_MatchesBatchFormat()
    {
        // Centralised header line must remain stable — batch writes (in
        // Program.WriteBatchOutputs) and the interactive UI button both
        // depend on this exact column order.
        using var tmp = TestTempFile.WithUniqueName("pareto_probe", "csv");
        ParetoFront.SaveToCsv(tmp.Path, System.Array.Empty<ParetoPoint>());
        string content = File.ReadAllText(tmp.Path);
        Assert.StartsWith("iteration,peak_wall_t_k,coolant_dp_pa,mass_g", content);
    }

    [Fact]
    public void ParetoCsv_Roundtrip_PreservesPointValues()
    {
        using var tmp = TestTempFile.WithUniqueName("pareto_probe", "csv");
        var points = new[]
        {
            new ParetoPoint(900.0, 1.5e6, 250.0, new double[] {1, 2, 3}, Iteration: 12),
            new ParetoPoint(1100.0, 0.9e6, 320.0, new double[] {4, 5}, Iteration: 27),
        };
        int written = ParetoFront.SaveToCsv(tmp.Path, points);
        Assert.Equal(2, written);

        string[] lines = File.ReadAllLines(tmp.Path);
        Assert.Equal(3, lines.Length);                    // header + 2 rows
        // Round-trip the first data row and confirm the headline numbers survive.
        string[] cols = lines[1].Split(',');
        Assert.Equal(12, int.Parse(cols[0], CultureInfo.InvariantCulture));
        Assert.Equal(900.00, double.Parse(cols[1], CultureInfo.InvariantCulture), precision: 2);
        Assert.Equal(1500000.0, double.Parse(cols[2], CultureInfo.InvariantCulture), precision: 0);
        Assert.Equal(250.00, double.Parse(cols[3], CultureInfo.InvariantCulture), precision: 2);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Manifold ΔP split (RegenSolverOutputs)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RegenSolve_TotalDP_EqualsSumOfManifoldComponents()
    {
        // The contract: CoolantPressureDrop_Pa = Entrance + Friction + Exit
        // exactly. When each component is surfaced individually, downstream
        // consumers (FeedSystem stackup, scoring, report) can break the
        // budget down without the solver re-deriving it.
        var (cond, design) = Baseline();
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var t = gen.Thermal;
        Assert.Equal(t.CoolantPressureDrop_Pa,
                     t.EntranceLoss_Pa + t.FrictionLoss_Pa + t.ExitLoss_Pa,
                     precision: 0);
        Assert.True(t.EntranceLoss_Pa >= 0);
        Assert.True(t.FrictionLoss_Pa >= 0);
        Assert.True(t.ExitLoss_Pa     >= 0);
    }

    [Fact]
    public void RegenSolve_FrictionDominates_TypicalDesign()
    {
        // Sanity: the manifold loss coefficients (K_ent ≈ 0.5, K_exit ≈ 1.0)
        // sum to ~1.5 velocity heads while the channel friction integral
        // typically eats 5-50× that for a coolant march of 80+ stations.
        // Friction should be at least 60 % of the total budget on the
        // baseline 500 N LOX/CH4 design.
        var (cond, design) = Baseline();
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var t = gen.Thermal;
        double frictionShare = t.FrictionLoss_Pa
                             / System.Math.Max(t.CoolantPressureDrop_Pa, 1.0);
        Assert.True(frictionShare >= 0.6,
            $"Friction should dominate on baseline; got {frictionShare:P0} (entrance {t.EntranceLoss_Pa:E1}, friction {t.FrictionLoss_Pa:E1}, exit {t.ExitLoss_Pa:E1}).");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Independent ox / fuel valve ramps
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void StartTransient_DefaultRamps_ReproducesSingleChannelBehaviour()
    {
        // When ox/fuel ramps + dome volumes default to 0, the simulator
        // splits 50/50 and uses the shared `ValveOpenTime_s`. Per-side
        // sample fields should mirror the aggregate at every step.
        var inp = new StartTransientInputs(
            ValveOpenTime_s: 0.10, IgniterDelay_s: 0.05,
            DomeVolume_m3: 5e-5, DomePropellantDensity_kgm3: 800,
            SteadyMassFlow_kgs: 0.5, ChamberVolume_m3: 5e-4,
            CStar_ms: 1850, ThroatArea_m2: 5e-5,
            ChamberPressure_Pa: 6.9e6, SimulationDuration_s: 0.5,
            TimeStep_s: 0.001, HardStartFactor: 0.5);
        var r = StartTransientSim.Run(inp);
        // Pick a sample mid-ramp where both ox and fuel are still ramping.
        var mid = r.Samples[40];
        Assert.Equal(mid.ValvePosition,    0.5 * (mid.OxValvePosition + mid.FuelValvePosition), precision: 6);
        // Aggregate dome fill is the average of per-side fills by construction.
        Assert.Equal(mid.DomeFillFraction, 0.5 * (mid.OxDomeFillFraction + mid.FuelDomeFillFraction), precision: 6);
    }

    [Fact]
    public void StartTransient_FuelLeadRamp_MitigatesHardStart()
    {
        // Legacy behaviour: both sides shared one ramp; long igniter delay
        // pooled big propellant masses → hard start. With the fuel-lead
        // pattern (open fuel valve early, ox catches up later), the fuel
        // dome fills before the ox dome — by the time both sides are
        // injecting the igniter has already fired, so less mass pools
        // pre-ignition.
        var slowEqual = new StartTransientInputs(
            ValveOpenTime_s: 0.10, IgniterDelay_s: 0.20,
            DomeVolume_m3: 5e-5, DomePropellantDensity_kgm3: 800,
            SteadyMassFlow_kgs: 0.5, ChamberVolume_m3: 5e-4,
            CStar_ms: 1850, ThroatArea_m2: 5e-5,
            ChamberPressure_Pa: 6.9e6, SimulationDuration_s: 0.5,
            TimeStep_s: 0.001, HardStartFactor: 0.5);
        var staged = slowEqual with
        {
            FuelValveOpenTime_s = 0.05,   // fuel leads
            OxValveOpenTime_s   = 0.20,   // ox lags
        };
        var equalRun  = StartTransientSim.Run(slowEqual);
        var stagedRun = StartTransientSim.Run(staged);
        Assert.True(stagedRun.UnburnedMassAtIgnition_kg
                    < equalRun.UnburnedMassAtIgnition_kg,
            $"Staged start should pool less unburned propellant pre-ignition. "
            + $"equal={equalRun.UnburnedMassAtIgnition_kg:E2}  "
            + $"staged={stagedRun.UnburnedMassAtIgnition_kg:E2}");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Ox / fuel ΔP separation in ChugAnalysis
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Chug_Default_AggregateOnly_BackCompatBehaviour()
    {
        // Legacy callers pass `new InjectorState(dPInj_Pa)` without
        // per-side drops. ChugAnalysis must produce the same headline
        // Rating + Reason as before, and per-side fields mirror.
        var inj = new InjectorState(DeltaPInj_Pa: 0.20 * 6.9e6);    // 20 % → Pass
        var r = ChugAnalysis.Evaluate(inj, 6.9e6);
        Assert.Equal(StabilityRating.Pass, r.Rating);
        Assert.Equal(r.OxRating,   r.Rating);
        Assert.Equal(r.FuelRating, r.Rating);
        Assert.Equal(r.OxDeltaPRatio,   r.DeltaPRatio, precision: 6);
        Assert.Equal(r.FuelDeltaPRatio, r.DeltaPRatio, precision: 6);
    }

    [Fact]
    public void Chug_PerSide_WorseSideDrivesHeadline()
    {
        // Ox at 22 % (Pass), fuel at 8 % (Fail) → headline is Fail and
        // Reason mentions the fuel side. Per-side fields keep their own
        // ratings.
        const double Pc = 6.9e6;
        var inj = new InjectorState(
            DeltaPInj_Pa:       0.20 * Pc,
            OxDeltaPInj_Pa:     0.22 * Pc,
            FuelDeltaPInj_Pa:   0.08 * Pc);
        var r = ChugAnalysis.Evaluate(inj, Pc);
        Assert.Equal(StabilityRating.Pass, r.OxRating);
        Assert.Equal(StabilityRating.Fail, r.FuelRating);
        Assert.Equal(StabilityRating.Fail, r.Rating);
        Assert.Contains("Fuel", r.Reason);
    }

    [Fact]
    public void Chug_PerSide_BothInBand_BothPass()
    {
        const double Pc = 6.9e6;
        var inj = new InjectorState(
            DeltaPInj_Pa:       0.20 * Pc,
            OxDeltaPInj_Pa:     0.18 * Pc,
            FuelDeltaPInj_Pa:   0.22 * Pc);
        var r = ChugAnalysis.Evaluate(inj, Pc);
        Assert.Equal(StabilityRating.Pass, r.OxRating);
        Assert.Equal(StabilityRating.Pass, r.FuelRating);
        Assert.Equal(StabilityRating.Pass, r.Rating);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Doublet impingement angle as design variable
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ImpingingDoublet_PatternAngle_ReachesElement()
    {
        // The pattern's ImpingementHalfAngle_deg should appear in the
        // sized element's notes. Verify by sizing twice with different
        // angles and checking that the notes change accordingly.
        var pat15 = InjectorPattern.DefaultImpinging() with { ImpingementHalfAngle_deg = 15.0 };
        var pat35 = InjectorPattern.DefaultImpinging() with { ImpingementHalfAngle_deg = 35.0 };
        var r15 = pat15.SizePattern(0.5, 0.15, 1.4e6, oxDensity_kgm3: 1140, fuelDensity_kgm3: 420);
        var r35 = pat35.SizePattern(0.5, 0.15, 1.4e6, oxDensity_kgm3: 1140, fuelDensity_kgm3: 420);
        Assert.Contains(r15.PerElementResult.Notes, n => n.Contains("Half-angle = 15°"));
        Assert.Contains(r35.PerElementResult.Notes, n => n.Contains("Half-angle = 35°"));
    }

    [Fact]
    public void ImpingingDoublet_AngleClamp_BoundsToPhysicalRange()
    {
        // Outside [10°, 45°] the element clamps. Verify by passing 5°
        // and 60°; sized output should report the clamp boundary.
        var pat5  = InjectorPattern.DefaultImpinging() with { ImpingementHalfAngle_deg = 5.0 };
        var pat60 = InjectorPattern.DefaultImpinging() with { ImpingementHalfAngle_deg = 60.0 };
        var r5    = pat5.SizePattern(0.5, 0.15, 1.4e6, 1140, 420);
        var r60   = pat60.SizePattern(0.5, 0.15, 1.4e6, 1140, 420);
        Assert.Contains(r5.PerElementResult.Notes,  n => n.Contains("Half-angle = 10°"));
        Assert.Contains(r60.PerElementResult.Notes, n => n.Contains("Half-angle = 45°"));
    }

    [Fact]
    public void ImpingingDoublet_DefaultAngle_StaysAt20Degrees()
    {
        // Back-compat: default pattern (no override) reports 20° in the
        // element's notes — same as the legacy element-class default.
        var pat = InjectorPattern.DefaultImpinging();
        var r   = pat.SizePattern(0.5, 0.15, 1.4e6, 1140, 420);
        Assert.Contains(r.PerElementResult.Notes, n => n.Contains("Half-angle = 20°"));
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static (OperatingConditions cond, RegenChamberDesign design) Baseline()
    {
        var cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 40,
        };
        return (cond, design);
    }
}
