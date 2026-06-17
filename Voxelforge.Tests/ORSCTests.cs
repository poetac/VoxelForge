// ORSCTests.cs — Sprint 24 regression + invariant suite for
// oxygen-rich staged combustion.
//
// Coverage:
//   • CycleSolver: ORSC registered + correct flags (no fuel-rich
//     preburner, has ox-rich preburner, has turbine, discharge feeds
//     main chamber, NOT FFSC-dual sizing)
//   • OxRichPreburnerMassFlowFraction exposed on the interface;
//     legacy-table rows consistent
//   • ORSC_PREBURNER_OXCORROSION gate: fires at T > (service − 50 K),
//     silent on fuel-rich preburner paths, silent on non-ORSC cycles
//     even when ox preburner thermal is populated
//   • TurbineSizing.Size works with oxPreburner only (no fuel-rich) on
//     ORSC — picks ox-rich preburner as drive gas, returns non-null
//     TurbineSizingResult

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class ORSCTests
{
    // ── Solver invariants ────────────────────────────────────────────

    [Fact]
    public void CycleSolver_ORSC_Registered()
    {
        var s = CycleSolvers.Get(EngineCycle.ORSC);
        Assert.NotNull(s);
        Assert.Equal(EngineCycle.ORSC, s.Cycle);
    }

    [Fact]
    public void CycleSolver_ORSC_HasExpectedFlags()
    {
        var s = CycleSolvers.Get(EngineCycle.ORSC);

        Assert.False(s.HasFuelRichPreburner);
        Assert.True(s.HasOxRichPreburner);
        Assert.Equal(1.50, s.PreburnerPcMultiplier,            precision: 6);
        Assert.Equal(0.0,  s.FuelRichPreburnerMassFlowFraction, precision: 6);
        Assert.Equal(1.00, s.OxRichPreburnerMassFlowFraction,   precision: 6);
        Assert.False(s.UsesFfscDualPreburnerSizing);   // single ox-rich
        Assert.True(s.HasTurbopump);
        Assert.False(s.HasElectricPowerConverter);
        Assert.True(s.HasTurbine);
        Assert.True(s.TurbineDischargeFeedsMainChamber);
    }

    [Fact]
    public void CycleSolver_ORSC_DiffersFromStagedCombustion_OnSideRouting()
    {
        // StagedCombustion: fuel-rich preburner only.
        // ORSC: ox-rich preburner only.
        // Both: staged (discharge into main chamber), same PcMultiplier,
        // same full-flow fraction, neither FFSC-dual.
        var sc    = CycleSolvers.Get(EngineCycle.StagedCombustion);
        var orsc  = CycleSolvers.Get(EngineCycle.ORSC);

        Assert.True(sc.HasFuelRichPreburner);
        Assert.False(sc.HasOxRichPreburner);

        Assert.False(orsc.HasFuelRichPreburner);
        Assert.True(orsc.HasOxRichPreburner);

        Assert.Equal(sc.PreburnerPcMultiplier,            orsc.PreburnerPcMultiplier, precision: 6);
        Assert.Equal(sc.FuelRichPreburnerMassFlowFraction, orsc.OxRichPreburnerMassFlowFraction, precision: 6);
        Assert.Equal(sc.UsesFfscDualPreburnerSizing,      orsc.UsesFfscDualPreburnerSizing);
        Assert.Equal(sc.TurbineDischargeFeedsMainChamber, orsc.TurbineDischargeFeedsMainChamber);
    }

    [Fact]
    public void CycleSolver_OxRichMassFlowFraction_MatchesExpectedPerCycle()
    {
        // Legacy-table invariant: only FFSC + ORSC route any flow
        // through an ox-rich preburner. Pins that so a future cycle
        // with ox-rich mass flow must update the expected table here.
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            var s = CycleSolvers.Get(c);
            double expected = c switch
            {
                EngineCycle.FullFlow => 1.00,
                EngineCycle.ORSC     => 1.00,
                _                    => 0.00,
            };
            Assert.Equal(expected, s.OxRichPreburnerMassFlowFraction,
                precision: 6);
        }
    }

    [Fact]
    public void CycleSolver_OxRichMassFlowPositive_IffHasOxRichPreburner()
    {
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            var s = CycleSolvers.Get(c);
            Assert.Equal(s.HasOxRichPreburner, s.OxRichPreburnerMassFlowFraction > 0.0);
        }
    }

    // ── TurbineSizing with ORSC drive-gas path ────────────────────────

    [Fact]
    public void TurbineSizing_ORSC_UsesOxPreburnerAsDriveGas()
    {
        // Fixture: an ORSC cycle has fuelPreburner = null and
        // oxPreburner populated. Pre-Sprint-24 the code short-circuited
        // to null on "fuelPreburner is null"; Sprint 24 accepts either
        // preburner as drive gas.
        var oxPreburner = new PreburnerResult(
            Cycle:                    EngineCycle.ORSC,
            MixtureRatio:             50.0,          // ox-rich
            ChamberPressure_Pa:       18e6,
            WarmGasTemperature_K:     1050.0,        // below turbine-inlet limit
            WarmGasCStar_ms:          1400.0,
            WarmGasGamma:             1.25,
            WarmGasMolecularWeight:   30.0,
            MassFlow_kgs:             3.0,
            CharacteristicLength_m:   0.40,
            ChamberVolume_mm3:        1.5e6,
            Notes:                    "ORSC test fixture",
            Warnings:                 System.Array.Empty<string>(),
            Thermal:                  null);

        var fuelPump = new PumpSizing(
            PropellantLabel:       "fuel",
            MassFlow_kgs:          0.8,
            InletPressure_Pa:      5e5,
            DischargePressure_Pa:  18e6,
            Density_kgm3:          810.0,
            HeadRise_m:            2200.0,
            HydraulicPower_W:      80_000.0,
            ShaftPower_W:          120_000.0,
            Efficiency:            0.66,
            Rpm:                   28_000.0,
            NPSHA_m:               120.0,
            NPSHR_m:               25.0,
            NPSHAcceptable:        true);
        var oxPump = fuelPump with
        {
            PropellantLabel = "ox",
            MassFlow_kgs    = 2.2,
            Density_kgm3    = 1140.0,
            ShaftPower_W    = 180_000.0,
        };

        var result = TurbineSizing.Size(
            cycle:                  EngineCycle.ORSC,
            mainChamberPressure_Pa: 12e6,
            fuelPump:               fuelPump,
            oxPump:                 oxPump,
            fuelPreburner:          null,         // ORSC — no fuel-rich preburner
            oxPreburner:            oxPreburner);

        Assert.NotNull(result);
        Assert.NotNull(result!.FuelTurbine);   // still sized — drive = ox preburner
        Assert.NotNull(result.OxTurbine);
        // Both turbines drive at the ox-rich preburner's inlet T.
        Assert.Equal(1050.0, result.FuelTurbine!.InletTemperature_K, precision: 3);
        Assert.Equal(1050.0, result.OxTurbine!.InletTemperature_K,   precision: 3);
    }

    [Fact]
    public void TurbineSizing_ReturnsNull_WhenBothPreburnersAreNull()
    {
        // Sanity: if both preburners are null (should never happen on a
        // cycle-solver-driven call path, but defensive), returns null.
        var fuelPump = new PumpSizing(
            "fuel", 1.0, 5e5, 12e6, 800, 1500,
            50_000, 75_000, 0.65, 25_000, 100, 20, true);

        var result = TurbineSizing.Size(
            cycle:                  EngineCycle.ORSC,
            mainChamberPressure_Pa: 12e6,
            fuelPump:               fuelPump,
            oxPump:                 null,
            fuelPreburner:          null,
            oxPreburner:            null);

        Assert.Null(result);
    }

    // ── ORSC_PREBURNER_OXCORROSION gate ──────────────────────────────

    private static RegenGenerationResult OrscResultWithPreburnerThermal(
        double peakWallT_K,
        FeedSystem.EngineCycle cycle = FeedSystem.EngineCycle.ORSC)
    {
        // Build a skeleton generation result with an ox-rich preburner
        // carrying a thermal solution at the requested peak T. Other
        // fields come from a standard GenerateWith call on default
        // conditions, then we overwrite OxidizerPreburner via `with`.
        var cond = new OperatingConditions { EngineCycle = cycle };
        var design = new RegenChamberDesign();
        var gen = RegenChamberOptimization.GenerateWith(cond, design);

        var thermal = new PreburnerThermalResult(
            PeakWallT_K:                  peakWallT_K,
            TAwCore_K:                    peakWallT_K + 80.0,
            CoolantOutletT_K:             cond.CoolantInletTemp_K + 80.0,
            CoolantPressureDrop_Pa:       0,
            TotalHeatLoad_W:              150_000.0,
            HGasSide_Wm2K:                12_000.0,
            HCoolantSide_Wm2K:            30_000.0,
            ChamberInnerSurfaceArea_m2:   0.02,
            ChannelHydraulicDiameter_mm:  1.5,
            Warnings:                     System.Array.Empty<string>());

        var oxPreburner = new PreburnerResult(
            Cycle:                    cycle,
            MixtureRatio:             50.0,
            ChamberPressure_Pa:       18e6,
            WarmGasTemperature_K:     peakWallT_K + 50.0,
            WarmGasCStar_ms:          1400.0,
            WarmGasGamma:             1.25,
            WarmGasMolecularWeight:   30.0,
            MassFlow_kgs:             3.0,
            CharacteristicLength_m:   0.40,
            ChamberVolume_mm3:        1.5e6,
            Notes:                    "ORSC test",
            Warnings:                 System.Array.Empty<string>(),
            Thermal:                  thermal);

        return gen with { OxidizerPreburner = oxPreburner };
    }

    [Fact]
    public void ORSCGate_Fires_InWarnZone_BelowServiceLimit()
    {
        // CuCrZr service limit = 923 K; warn zone = 873-923 K.
        var mat = WallMaterials.All[1];   // CuCrZr (default)
        double warnZoneT = mat.MaxServiceTemp_K - 25.0;   // 25 K below limit
        Assert.InRange(warnZoneT,
            mat.MaxServiceTemp_K - 50.0,
            mat.MaxServiceTemp_K);

        var gen = OrscResultWithPreburnerThermal(warnZoneT);
        var gate = FeasibilityGate.Evaluate(gen);

        var corrosion = System.Array.Find(gate.Violations,
            v => v.ConstraintId == "ORSC_PREBURNER_OXCORROSION");
        Assert.NotNull(corrosion);
        Assert.Equal(warnZoneT, corrosion!.ActualValue, precision: 3);
        Assert.Equal(mat.MaxServiceTemp_K - 50.0, corrosion.Limit, precision: 3);
        // PREBURNER_WALL_TEMP should NOT fire — we're below the hard limit.
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "PREBURNER_WALL_TEMP");
    }

    [Fact]
    public void ORSCGate_Silent_BelowWarnZone()
    {
        // Wall T comfortably below (service − 50 K) → no corrosion gate.
        var mat = WallMaterials.All[1];   // CuCrZr
        double coolT = mat.MaxServiceTemp_K - 100.0;

        var gen = OrscResultWithPreburnerThermal(coolT);
        var gate = FeasibilityGate.Evaluate(gen);

        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "ORSC_PREBURNER_OXCORROSION");
    }

    [Fact]
    public void ORSCGate_SilentOnFFSC_SameWarnZoneTemperature()
    {
        // FFSC cycle with an ox-rich preburner at the same warn-zone T
        // must NOT fire the ORSC corrosion gate. Sprint 24 scopes the
        // tighter margin to ORSC only; FFSC keeps the slacker hard-only
        // margin until a real FFSC design pushes near the limit.
        var mat = WallMaterials.All[1];
        double warnZoneT = mat.MaxServiceTemp_K - 25.0;

        var gen = OrscResultWithPreburnerThermal(warnZoneT,
            cycle: FeedSystem.EngineCycle.FullFlow);
        var gate = FeasibilityGate.Evaluate(gen);

        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "ORSC_PREBURNER_OXCORROSION");
    }

    [Fact]
    public void ORSCGate_AlsoFires_WhenWallAlsoExceedsHardServiceLimit()
    {
        // Hard-over: T > service → both PREBURNER_WALL_TEMP and the
        // ORSC corrosion gate should fire. They represent different
        // concerns; the warn-zone gate doesn't swallow the hard one.
        var mat = WallMaterials.All[1];
        double hotT = mat.MaxServiceTemp_K + 25.0;

        var gen = OrscResultWithPreburnerThermal(hotT);
        var gate = FeasibilityGate.Evaluate(gen);

        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "PREBURNER_WALL_TEMP");
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "ORSC_PREBURNER_OXCORROSION");
    }
}
