// T1_5PreScreenTests.cs — coverage for FeasibilityGate.PreScreen
// (T1.5 progressive-fidelity evaluation, 2026-04-27).

using Voxelforge.Combustion;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class T1_5PreScreenTests
{
    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N              = 2224.0,
        ChamberPressure_Pa    = 6.9e6,
        MixtureRatio          = 3.3,
        CoolantInletTemp_K    = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex     = 1,
        PropellantPair        = PropellantPair.LOX_CH4,
    };

    [Fact]
    public void PreScreen_ValidDesign_ReturnsNull()
    {
        // Default RegenChamberDesign sits inside every cheap-gate band.
        var result = FeasibilityGate.PreScreen(DefaultConditions(), new RegenChamberDesign());
        Assert.Null(result);
    }

    [Fact]
    public void PreScreen_ContractionRatioBelowFloor_FiresGate()
    {
        // SA never samples below 3.0, but a hand-built design or stale
        // saved file could.
        var design = new RegenChamberDesign { ContractionRatio = 1.5 };
        var v = FeasibilityGate.PreScreen(DefaultConditions(), design);
        Assert.NotNull(v);
        Assert.Equal("CONTRACTION_RATIO_OUT_OF_BAND", v!.ConstraintId);
        Assert.Contains("[pre-screen]", v.Description);
    }

    [Fact]
    public void PreScreen_ContractionRatioAboveCeiling_FiresGate()
    {
        var design = new RegenChamberDesign { ContractionRatio = 12.0 };
        var v = FeasibilityGate.PreScreen(DefaultConditions(), design);
        Assert.NotNull(v);
        Assert.Equal("CONTRACTION_RATIO_OUT_OF_BAND", v!.ConstraintId);
    }

    [Fact]
    public void PreScreen_LStarBelowFloor_FiresGate()
    {
        // LOX/CH4 nominal is ~1.10 m; floor = 95 % = ~1.045 m.
        // Set L* well below to fire.
        var design = new RegenChamberDesign { CharacteristicLength_m = 0.7 };
        var v = FeasibilityGate.PreScreen(DefaultConditions(), design);
        Assert.NotNull(v);
        Assert.Equal("L_STAR_BELOW_PROPELLANT_MIN", v!.ConstraintId);
    }

    [Fact]
    public void PreScreen_LStarAtNominal_DoesNotFire()
    {
        var design = new RegenChamberDesign { CharacteristicLength_m = 1.10 };
        var v = FeasibilityGate.PreScreen(DefaultConditions(), design);
        Assert.Null(v);
    }

    [Fact]
    public void PreScreen_TpmsStrutBelowFloor_FiresGate_OnlyOnTpmsTopology()
    {
        // Cell 1.5 mm, sf 0.50 → strut = 0.50 × 1.5 = 0.75 mm; LPBF floor 2.0 mm.
        // (Choose values where the OLD inverted formula (1−sf)·ce would give
        //  a different magnitude — 0.75 vs 0.75 here happens by coincidence,
        //  so use sf 0.65 below where (1−0.65)·1.5 = 0.525 vs 0.65·1.5 = 0.975.
        //  Either way the strut is below the 2.0 mm floor, so this test stays
        //  invariant to the formula direction; the new parity test below
        //  exercises the formula-sensitive cases.)
        var tpms = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.TpmsGyroid,
            TpmsCellEdge_mm = 1.5,
            TpmsSolidFraction = 0.65,
        };
        var v = FeasibilityGate.PreScreen(DefaultConditions(), tpms);
        Assert.NotNull(v);
        Assert.Equal("TPMS_CELL_FEATURE_TOO_SMALL", v!.ConstraintId);

        // Same numeric values on Axial topology must NOT fire (TPMS gate
        // is topology-conditional).
        var axial = tpms with { ChannelTopology = ChannelTopology.Axial };
        Assert.Null(FeasibilityGate.PreScreen(DefaultConditions(), axial));
    }

    [Fact]
    public void PreScreen_TpmsStrutAtFloor_DoesNotFire()
    {
        // Cell 4.0 × 0.50 = 2.0 mm strut — exactly at floor (correct
        // SOLID-fraction formula). Note: the boundary case sf=0.50 is
        // accidentally invariant to the (1−sf) bug because 0.50 = 1−0.50;
        // see PreScreen_TpmsStrutFormula_AgreesWithFullEval below for the
        // formula-direction-sensitive parity test.
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.TpmsGyroid,
            TpmsCellEdge_mm = 4.0,
            TpmsSolidFraction = 0.50,
        };
        var v = FeasibilityGate.PreScreen(DefaultConditions(), design);
        Assert.Null(v);
    }

    // Z1.1 parity-invariant regression (post-#107). Pre-screen MUST agree
    // with the full-eval helper TpmsCorrelations.StrutThickness_mm on the
    // sign of the rejection for every (cellEdge, solidFraction) inside the
    // valid envelope. The OLD (1 − sf) × ce formula would diverge from the
    // helper for every case where sf ≠ 0.50 (i.e. nearly the whole
    // envelope), so this Theory pins the formula direction.
    //
    // Range: solidFraction ∈ {0.35, 0.45, 0.50, 0.55, 0.65} (SA bounds);
    // cellEdge ∈ {1.5, 2.5, 4.0, 6.0}.
    [Theory]
    [InlineData(1.5, 0.35)]
    [InlineData(1.5, 0.45)]
    [InlineData(1.5, 0.50)]
    [InlineData(1.5, 0.55)]
    [InlineData(1.5, 0.65)]
    [InlineData(2.5, 0.35)]
    [InlineData(2.5, 0.45)]
    [InlineData(2.5, 0.50)]
    [InlineData(2.5, 0.55)]
    [InlineData(2.5, 0.65)]
    [InlineData(4.0, 0.35)]
    [InlineData(4.0, 0.45)]
    [InlineData(4.0, 0.50)]
    [InlineData(4.0, 0.55)]
    [InlineData(4.0, 0.65)]
    [InlineData(6.0, 0.35)]
    [InlineData(6.0, 0.45)]
    [InlineData(6.0, 0.50)]
    [InlineData(6.0, 0.55)]
    [InlineData(6.0, 0.65)]
    public void PreScreen_TpmsStrutFormula_AgreesWithFullEval(double cellEdge_mm, double solidFraction)
    {
        // Helper is the canonical formula used by both PreScreen and full
        // Evaluate (FeasibilityGate.cs:~1174). If they disagree, pre-screen
        // is rejecting designs the full pipeline accepts (or vice versa).
        double strut_mm = TpmsCorrelations.StrutThickness_mm(cellEdge_mm, solidFraction);
        bool helperRejects = strut_mm < TpmsCorrelations.MinStrutThickness_mm;

        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.TpmsGyroid,
            TpmsCellEdge_mm = cellEdge_mm,
            TpmsSolidFraction = solidFraction,
        };
        var v = FeasibilityGate.PreScreen(DefaultConditions(), design);

        if (helperRejects)
        {
            Assert.NotNull(v);
            Assert.Equal("TPMS_CELL_FEATURE_TOO_SMALL", v!.ConstraintId);
        }
        else
        {
            // Either passes outright, or fires a different gate (e.g.
            // L_STAR_BELOW_PROPELLANT_MIN) — but NOT the TPMS gate.
            Assert.True(v is null || v.ConstraintId != "TPMS_CELL_FEATURE_TOO_SMALL",
                $"Pre-screen fired TPMS gate at strut={strut_mm:F2} mm ≥ "
                + $"{TpmsCorrelations.MinStrutThickness_mm:F1} mm floor — "
                + "formula direction inverted?");
        }
    }

    [Fact]
    public void PreScreen_NullInputs_HandlesGracefully()
    {
        // Defensive: no exception on null arguments. Returns null
        // (treats as "passes" so the full pipeline still runs and
        // surfaces a more diagnostic failure later).
        Assert.Null(FeasibilityGate.PreScreen(null!, null!));
        Assert.Null(FeasibilityGate.PreScreen(null!, new RegenChamberDesign()));
        Assert.Null(FeasibilityGate.PreScreen(DefaultConditions(), null!));
    }

    [Fact]
    public void PreScreen_FastEnoughForSAHotPath()
    {
        // Smoke test: pre-screen must be µs-scale, not ms-scale, so the
        // hot-path wiring actually pays off. 10K iterations should complete
        // in well under 1 second on any reasonable hardware (target: ~10
        // µs per call → 100 ms for 10K calls; budgeting 1 s gives 10×
        // headroom).
        var cond = DefaultConditions();
        var design = new RegenChamberDesign();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++)
        {
            var _ = FeasibilityGate.PreScreen(cond, design);
        }
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"PreScreen took {sw.ElapsedMilliseconds} ms for 10k calls — too slow for SA hot path");
    }
}
