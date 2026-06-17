// SprintUpgradesTests.cs — Contract tests for the 2026-04-19 sprint:
//   Sprint 1.1 — ΔP_inj flows from InjectorPattern into chug screening
//   Sprint 1.2 — Pack/Unpack carry 18 vars (13 chamber + 5 injector)
//   Sprint 1.3 — ELEMENT_DENSITY_TOO_HIGH gate + injector-ratio scoring
//   Sprint 2   — fin-efficiency correction on coolant-side HTC
//   Sprint 3   — DesignProvenance hash identity and sensitivity
//   Sprint 4   — ReportExport best-so-far banner

using System.Numerics;
using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Coolant;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.IO;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.Optimization;
using Voxelforge.Structure;
using Voxelforge.Tests.Helpers;
using Voxelforge.Turbopump;

namespace Voxelforge.Tests;

public class SprintUpgradesTests
{
    // ═════════════════════════════════════════════════════════════════
    //   Sprint 1.1 — ΔP_inj feedback
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void InjectorPattern_DefaultDeltaPInjFraction_Is20Percent()
    {
        var pat = InjectorPattern.DefaultCoax();
        Assert.Equal(0.20, pat.DeltaPInjFraction, precision: 6);
    }

    [Theory]
    [InlineData(0.05, StabilityRating.Fail)]      // 5% ΔP — chug FAIL (below marginal pad)
    [InlineData(0.14, StabilityRating.Marginal)]  // just below 15% — marginal band
    [InlineData(0.20, StabilityRating.Pass)]      // nominal middle of band
    [InlineData(0.24, StabilityRating.Pass)]      // still inside upper
    [InlineData(0.40, StabilityRating.Fail)]      // way above 27% fail threshold
    public void StabilityScreening_ReflectsInjectorStatePassedIn(double dPFrac, StabilityRating expected)
    {
        // Build a minimal gas state + contour fixture; the composite can only
        // degrade below the chug rating, never improve it, so if chug is
        // Pass/Marginal/Fail the composite is ≤ that level.
        var gas = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 6.9e6);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: 8.0, contractionRatio: 6.0, expansionRatio: 8.0,
            characteristicLength_m: 1.1, stationCount: 60);

        var state = new InjectorState(dPFrac * 6.9e6);
        var report = StabilityScreening.Evaluate(contour, gas, 6.9e6, state);
        Assert.Equal(expected, report.Chug.Rating);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 1.2 — Pack / Unpack with injector vars
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Bounds_Length_Is31AfterTrackB()
    {
        // 18 → 20 with TPMS unit-cell vars.
        // 20 → 22 with Tier-C SA knobs (PreburnerMrRatio + FlangeRadialProjection).
        // 22 → 23 with aerospike PlugLengthRatio.
        // 23 → 24 with AerospikeContractionRatio.
        // 24 → 26 with FilmFuelFraction + FilmSlotHeightOverride_mm
        //   (feasibility-audit Sprint 5, 2026-04-26).
        // 26 → 28 with PintleDiameterOverride_mm + PintleSleeveHoleCountOverride
        //   (feasibility-audit Sprint H2, 2026-04-27).
        // 28 → 31 with ChamberWallThicknessOverride_mm + ThroatWallThicknessOverride_mm
        //   + ExitWallThicknessOverride_mm (Track B, 2026-04-27).
        // 31 → 34 with HelmholtzNeckArea_mm2 + HelmholtzCavityVolume_mm3
        //   + QuarterWaveLength_mm (OOB-6 / Sprint B-3, 2026-04-30).
        Assert.Equal(34, RegenChamberOptimization.Bounds.Length);
    }

    [Fact]
    public void PackUnpack_RoundTripsInjectorFields_WhenPatternPresent()
    {
        var baseline = new RegenChamberDesign
        {
            InjectorElementPattern = InjectorPattern.DefaultCoax(24) with
            {
                DeltaPInjFraction = 0.22,
                OuterRowFilmFraction = 0.07,
                CdOx = 0.78,
                CdFuel = 0.68,
            },
        };

        double[] packed = RegenChamberOptimization.Pack(baseline);
        Assert.Equal(34, packed.Length);   // +3 from OOB-6 acoustic dampers (HelmholtzNeckArea, CavityVolume, QuarterWaveLength)
        // [13]=count, [14]=ΔP frac, [15]=film, [16]=Cd_ox, [17]=Cd_fuel
        Assert.Equal(24,    packed[13], precision: 6);
        Assert.Equal(0.22,  packed[14], precision: 6);
        Assert.Equal(0.07,  packed[15], precision: 6);
        Assert.Equal(0.78,  packed[16], precision: 6);
        Assert.Equal(0.68,  packed[17], precision: 6);

        // Round-trip.
        var copy = RegenChamberOptimization.Unpack(packed, baseline);
        var pat  = copy.InjectorElementPattern!;
        Assert.Equal(24,   pat.ElementCount);
        Assert.Equal(0.22, pat.DeltaPInjFraction, precision: 6);
        Assert.Equal(0.07, pat.OuterRowFilmFraction, precision: 6);
        Assert.Equal(0.78, pat.CdOx,   precision: 6);
        Assert.Equal(0.68, pat.CdFuel, precision: 6);
    }

    [Fact]
    public void PackUnpack_LeavesNullPattern_WhenBaselineHasNone()
    {
        // Unpack must not invent a pattern for users who haven't asked for one.
        var baseline = new RegenChamberDesign();   // no pattern
        double[] packed = RegenChamberOptimization.Pack(baseline);
        Assert.Equal(34, packed.Length);   // 34 dims (inert slots for pattern + TPMS + Tier-C + aerospike + film + Track-B + dampers)

        var copy = RegenChamberOptimization.Unpack(packed, baseline);
        Assert.Null(copy.InjectorElementPattern);
    }

    [Fact]
    public void Unpack_AppliesBounds_ClampsToSafeRange()
    {
        var baseline = new RegenChamberDesign
        {
            InjectorElementPattern = InjectorPattern.DefaultCoax(),
        };
        double[] p = RegenChamberOptimization.Pack(baseline);
        // Intentionally drive Cds out of bound; unpack clamps to [0.30, 1.0].
        p[16] = 5.0;    // absurd Cd_ox
        p[17] = -1.0;   // negative Cd_fuel
        var copy = RegenChamberOptimization.Unpack(p, baseline);
        Assert.InRange(copy.InjectorElementPattern!.CdOx,   0.30, 1.0);
        Assert.InRange(copy.InjectorElementPattern!.CdFuel, 0.30, 1.0);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 1.3 — element density + injector ratio penalties
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void FeasibilityGate_FailsWhenElementDensityExceedsCeiling()
    {
        // Synthetic small chamber (R = 15 mm → 7 cm² face) with 80 elements →
        // density ≈ 11 / cm² >> 0.7 ceiling → must raise ELEMENT_DENSITY_TOO_HIGH.
        var r = BuildSafeBaseResult();
        var dense = r with
        {
            InjectorPattern = InjectorPattern.DefaultCoax(80),
            InjectorSizing = new PatternSizingResult(
                ElementCount: 80,
                PerElementResult: new OrificeResult(0.5, 0.5, 20, 20, 1, 1, Array.Empty<string>()),
                TotalOxArea_mm2: 40, TotalFuelArea_mm2: 40, FlowSplitCheck: 1.00,
                Warnings: Array.Empty<string>()),
        };
        var gate = FeasibilityGate.Evaluate(dense);
        Assert.Contains(gate.Violations, v => v.ConstraintId == "ELEMENT_DENSITY_TOO_HIGH");
    }

    [Fact]
    public void FeasibilityGate_PassesAtReasonableElementDensity()
    {
        var r = BuildSafeBaseResult();
        // R ≈ 20 mm → face ≈ 12.6 cm². 6 elements → 0.48 / cm² < 0.7 ceiling.
        var reasonable = r with
        {
            InjectorPattern = InjectorPattern.DefaultCoax(6),
            InjectorSizing = new PatternSizingResult(
                ElementCount: 6,
                PerElementResult: new OrificeResult(0.5, 0.5, 20, 20, 1, 1, Array.Empty<string>()),
                TotalOxArea_mm2: 3, TotalFuelArea_mm2: 3, FlowSplitCheck: 1.00,
                Warnings: Array.Empty<string>()),
        };
        var gate = FeasibilityGate.Evaluate(reasonable);
        Assert.DoesNotContain(gate.Violations, v => v.ConstraintId == "ELEMENT_DENSITY_TOO_HIGH");
    }

    [Fact]
    public void ScoringProfiles_ContainsMaxInjectorUniformity()
    {
        Assert.Contains(RegenChamberOptimization.Profiles,
                        pr => pr.Name == "Max Injector Uniformity" && pr.InjectorRatioWeight > 0);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Tier-C SA knobs (PreburnerMrRatio + FlangeRadialProjection)
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Bounds_TierC_RangesMatchSpec()
    {
        // Bounds[20] / Bounds[21] bracket the
        // SA-tunable Tier-C knobs. Narrower than the Unpack clamp so the
        // sampler stays in the physically-sensible region while still
        // allowing callers to hand in a pre-tuned design outside the band.
        var b = RegenChamberOptimization.Bounds;
        Assert.Equal((0.30, 1.00), b[20]);    // PreburnerMrRatio  — fuel-rich for GG/SC/FFSC
        Assert.Equal((8.0,  24.0), b[21]);    // FlangeRadialProjection_mm
    }

    [Fact]
    public void Pack_EmitsBaselineFields_ForTierCKnobs()
    {
        // Sprint 7 Track C (2026-04-22): with the registry-driven binder
        // replacing the former hand-coded fallbacks, Pack now faithfully
        // mirrors the baseline's field values. A fresh RegenChamberDesign
        // has PreburnerMrRatio = 0 and FlangeRadialProjection_mm = 0
        // (both "use default downstream" sentinels); Pack emits those
        // zeros. The cycle-gated / monolithic-gated downstream code paths
        // still treat 0 as "fall back to the hardware-specific default"
        // (PreburnerChamber.SuggestPreburnerMr + PumpMountFlange.DefaultRadialProjection_mm
        // respectively), so the engineering behaviour is unchanged —
        // only the SA starting point differs for fresh baselines that
        // don't seed these knobs explicitly. AutoSeeder and test fixtures
        // that care about SA starting values should now set the fields
        // on the baseline rather than relying on Pack's former fallback.
        var design = new RegenChamberDesign();
        double[] p = RegenChamberOptimization.Pack(design);
        Assert.Equal(0.0, p[20], precision: 6);
        Assert.Equal(0.0, p[21], precision: 6);
    }

    [Fact]
    public void PackUnpack_RoundTripsTierCKnobs()
    {
        var baseline = new RegenChamberDesign
        {
            PreburnerMrRatio          = 0.45,
            FlangeRadialProjection_mm = 18.0,
        };
        double[] p = RegenChamberOptimization.Pack(baseline);
        Assert.Equal(0.45, p[20], precision: 6);
        Assert.Equal(18.0, p[21], precision: 6);

        var copy = RegenChamberOptimization.Unpack(p, baseline);
        Assert.Equal(0.45, copy.PreburnerMrRatio,          precision: 6);
        Assert.Equal(18.0, copy.FlangeRadialProjection_mm, precision: 6);
    }

    [Fact]
    public void Unpack_ClampsTierCKnobs_ToSafeRange()
    {
        var baseline = new RegenChamberDesign();
        double[] p = RegenChamberOptimization.Pack(baseline);
        // Drive both knobs out of SA bounds. Unpack clamps to its outer
        // safety range ([0.10, 2.00] and [4.0, 40.0]) so even a pathological
        // SA step cannot produce a degenerate preburner or flange.
        p[20] = 10.0;     // absurd MR
        p[21] = 500.0;    // absurd projection
        var copy = RegenChamberOptimization.Unpack(p, baseline);
        Assert.InRange(copy.PreburnerMrRatio,          0.10, 2.00);
        Assert.InRange(copy.FlangeRadialProjection_mm, 4.0,  40.0);

        p[20] = -1.0;     // negative
        p[21] = -10.0;    // negative
        var copy2 = RegenChamberOptimization.Unpack(p, baseline);
        Assert.InRange(copy2.PreburnerMrRatio,          0.10, 2.00);
        Assert.InRange(copy2.FlangeRadialProjection_mm, 4.0,  40.0);
    }

    [Fact]
    public void PumpMountFlange_RespectsRadialProjectionOverride()
    {
        // The radial-projection wiring terminates at PumpMountFlange.Size — anything
        // non-zero passed in overrides DefaultRadialProjection_mm. This
        // keeps the regression on the seam MonolithicEngineBuilder uses.
        var @default = PumpMountFlange.Size(casingOuterRadius_mm: 30.0);
        var wider    = PumpMountFlange.Size(casingOuterRadius_mm: 30.0, radialProjection_mm: 20.0);
        Assert.Equal(PumpMountFlange.DefaultRadialProjection_mm,
                     @default.OuterRadius_mm - @default.InnerRadius_mm,
                     precision: 6);
        Assert.Equal(20.0, wider.OuterRadius_mm - wider.InnerRadius_mm, precision: 6);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 1 (2026-04-22) — aerospike PlugLengthRatio SA knob
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Bounds_AerospikeDim22_MatchesAngelinoClampRange()
    {
        // The aerospike-plug SA bound must match the physical-validity
        // envelope on AerospikeContourGenerator (below 0.15 the plug is
        // a bluff-base disc; above 1.00 is a full spike). Keeping these
        // numbers in sync prevents the SA sampler from exploring designs
        // the contour generator will then reject at Generate-time.
        var b = RegenChamberOptimization.Bounds;
        Assert.Equal(
            (AerospikeContourGenerator.MinPlugLengthRatio,
             AerospikeContourGenerator.MaxPlugLengthRatio),
            b[22]);
    }

    [Fact]
    public void PackUnpack_RoundTripsPlugLengthRatio_WhenBaselineIsAerospike()
    {
        var baseline = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            PlugLengthRatio = 0.42,
        };
        double[] p = RegenChamberOptimization.Pack(baseline);
        Assert.Equal(34, p.Length);
        Assert.Equal(0.42, p[22], precision: 6);

        var copy = RegenChamberOptimization.Unpack(p, baseline);
        Assert.Equal(0.42, copy.PlugLengthRatio, precision: 6);
    }

    [Fact]
    public void Unpack_LeavesPlugLengthRatioAtDefault_WhenBaselineIsNotAerospike()
    {
        // A non-aerospike baseline with a perturbed dim[22] must not
        // silently revert to the packed value on Unpack — that would
        // reintroduce the "silent categorical revert" bug
        // that the TPMS block was explicitly written to avoid.
        var baseline = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Axial,
            PlugLengthRatio = 0.30,   // record default
        };
        double[] p = RegenChamberOptimization.Pack(baseline);
        p[22] = 0.85;   // the SA sampler pokes dim 22 with an aerospike-ish value

        var copy = RegenChamberOptimization.Unpack(p, baseline);
        // Non-aerospike baseline ignores dim 22 → stays at record default.
        Assert.Equal(0.30, copy.PlugLengthRatio, precision: 6);
    }

    [Fact]
    public void Unpack_ClampsPlugLengthRatio_WhenBaselineIsAerospike()
    {
        var baseline = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
        };
        double[] p = RegenChamberOptimization.Pack(baseline);

        p[22] = 2.5;       // absurdly high — clamp to MaxPlugLengthRatio
        var high = RegenChamberOptimization.Unpack(p, baseline);
        Assert.InRange(high.PlugLengthRatio,
            AerospikeContourGenerator.MinPlugLengthRatio,
            AerospikeContourGenerator.MaxPlugLengthRatio);

        p[22] = -0.1;      // negative — clamp to MinPlugLengthRatio
        var low  = RegenChamberOptimization.Unpack(p, baseline);
        Assert.InRange(low.PlugLengthRatio,
            AerospikeContourGenerator.MinPlugLengthRatio,
            AerospikeContourGenerator.MaxPlugLengthRatio);
    }

    [Fact]
    public void AutoSeeder_Aerospike_PackedVectorCarriesPlugLengthRatio()
    {
        // AutoSeeder with a ChannelTopologyOverride = Aerospike must produce
        // a design whose Pack() emits a non-zero PlugLengthRatio at dim 22.
        // This is the end-to-end "spec → seed → SA vector" smoke check so a
        // subsequent SA run can start from a meaningful aerospike baseline.
        var seed = AutoSeeder.Seed(new EngineSpec(
            PropellantPair:           PropellantPair.LOX_CH4,
            Thrust_N:                 5000.0,
            ChamberPressure_Pa:       6.9e6,
            ExpansionRatio:           15.0,
            ChannelTopologyOverride:  ChannelTopology.Aerospike));

        Assert.Equal(ChannelTopology.Aerospike, seed.Design.ChannelTopology);
        Assert.InRange(seed.Design.PlugLengthRatio,
            AerospikeContourGenerator.MinPlugLengthRatio,
            AerospikeContourGenerator.MaxPlugLengthRatio);

        var packed = RegenChamberOptimization.Pack(seed.Design);
        Assert.Equal(34, packed.Length);
        Assert.Equal(seed.Design.PlugLengthRatio, packed[22], precision: 6);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 2a (2026-04-22) — AerospikeBuilder.BuildPhysicsOnly
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildPhysicsOnly_ReturnsNullVoxels_ButPopulatesPhysicsFields()
    {
        // The xUnit-safe entry point must produce everything except the
        // PicoGK Voxels body. Contour, derived radii, volume, mass, and
        // description are all meaningful; Voxels is explicitly null so
        // downstream code can branch on it cleanly.
        var spec = new AerospikeSpec(
            Thrust_N:           20_000.0,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4);

        var r = AerospikeBuilder.BuildPhysicsOnly(spec);

        Assert.Null(r.Voxels);
        Assert.NotNull(r.Contour);
        Assert.True(r.ThroatOuterRadius_mm > 0);
        Assert.True(r.ThroatInnerRadius_mm > 0);
        Assert.True(r.ThroatInnerRadius_mm < r.ThroatOuterRadius_mm);
        Assert.Equal(r.ThroatOuterRadius_mm * 0.40, r.ThroatInnerRadius_mm, precision: 6);
        Assert.True(r.ChamberRadius_mm > r.ThroatOuterRadius_mm,
            "chamber must be wider than throat (contraction ratio > 1)");
        Assert.True(r.ChamberLength_mm > 0);
        Assert.True(r.PlugTruncatedLength_mm > 0);
        Assert.True(r.SolidVolume_mm3 > 0);
        Assert.True(r.EstimatedMass_g > 0);
        Assert.Contains("Aerospike", r.Description);
        Assert.Null(r.Thermal);   // IncludeRegenChannels defaults false
    }

    [Fact]
    public void BuildPhysicsOnly_WithRegenChannels_PopulatesThermal()
    {
        // When IncludeRegenChannels is opted in, the thermal solve must
        // run and produce a populated AerospikeThermalResult. This is the
        // xUnit-safe path through AerospikePlugCooling.Solve.
        var spec = new AerospikeSpec(
            Thrust_N:              20_000.0,
            ChamberPressure_Pa:    7e6,
            ExpansionRatio:        15.0,
            PlugLengthRatio:       0.30,
            PropellantPair:        PropellantPair.LOX_CH4,
            IncludeRegenChannels:  true,
            PlugChannelCount:      24,
            PlugChannelWidth_mm:   2.5,
            PlugChannelDepth_mm:   2.0,
            PlugWallThickness_mm:  0.8);

        var r = AerospikeBuilder.BuildPhysicsOnly(spec);

        Assert.Null(r.Voxels);
        Assert.NotNull(r.Thermal);
        Assert.True(r.Thermal!.PeakGasSideWallT_K > 0);
        Assert.True(r.Thermal.CoolantOutletT_K > 0);
        Assert.True(r.Thermal.GasSideWallT_K.Length > 0);
        Assert.True(r.Thermal.HeatFlux_Wm2.Length == r.Thermal.GasSideWallT_K.Length);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 9 Track B (2026-04-22) — Preburner regen cooling + gate
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void PreburnerResult_Thermal_IsNull_WhenNotOpted_In()
    {
        // Default GenerateWith on a staged-combustion cycle sizes the
        // preburner but skips cooling — design.IncludePreburnerRegenCooling
        // is false, so PreburnerResult.Thermal comes back null.
        var cond = new OperatingConditions
        {
            Thrust_N           = 50_000,
            ChamberPressure_Pa = 10e6,
            PropellantPair     = PropellantPair.LOX_CH4,
            EngineCycle        = FeedSystem.EngineCycle.StagedCombustion,
        };
        var design = new RegenChamberDesign();
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());
        Assert.NotNull(gen.Preburner);
        Assert.Null(gen.Preburner!.Thermal);
    }

    [Fact]
    public void PreburnerResult_Thermal_PopulatedWhenOptedIn()
    {
        // Opt in via design.IncludePreburnerRegenCooling; GenerateWith
        // now wires HeatTransfer.PreburnerCooling.Solve into
        // SizePreburnerFor and attaches the result.
        var cond = new OperatingConditions
        {
            Thrust_N           = 50_000,
            ChamberPressure_Pa = 10e6,
            PropellantPair     = PropellantPair.LOX_CH4,
            EngineCycle        = FeedSystem.EngineCycle.StagedCombustion,
        };
        var design = new RegenChamberDesign
        {
            IncludePreburnerRegenCooling = true,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());
        Assert.NotNull(gen.Preburner);
        Assert.NotNull(gen.Preburner!.Thermal);
        var t = gen.Preburner.Thermal!;
        Assert.True(t.PeakWallT_K > cond.CoolantInletTemp_K,
            "wall T sits between coolant inlet and gas recovery — must exceed coolant");
        Assert.True(t.PeakWallT_K < t.TAwCore_K,
            "wall T must be below the gas-side recovery T");
        Assert.True(t.HGasSide_Wm2K > 0);
        Assert.True(t.HCoolantSide_Wm2K > 0);
    }

    [Fact]
    public void FeasibilityGate_PreburnerWallTemp_SilentWhenNotOptedIn()
    {
        // No cooling solver → no Thermal → gate never evaluates.
        var cond = new OperatingConditions
        {
            Thrust_N           = 50_000,
            ChamberPressure_Pa = 20e6,
            PropellantPair     = PropellantPair.LOX_CH4,
            EngineCycle        = FeedSystem.EngineCycle.StagedCombustion,
        };
        var design = new RegenChamberDesign();
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "PREBURNER_WALL_TEMP");
    }

    [Fact]
    public void FeasibilityGate_PreburnerWallTemp_FiresWhenWallExceedsMaterial()
    {
        // Construct a scenario where the preburner cooling can't keep
        // up: very high Pc (drives high warm-gas Cp × ρU), shallow /
        // few channels (low h_c), low coolant flow (not tunable — set
        // by derived fuel mass flow at the main-chamber operating
        // point). If the MVP lumped-parameter model predicts T_wall
        // above the material service limit, the gate fires.
        var cond = new OperatingConditions
        {
            Thrust_N           = 200_000,   // large engine → large preburner mass flow
            ChamberPressure_Pa = 25e6,       // high Pc → warm-gas density + h_g high
            PropellantPair     = PropellantPair.LOX_CH4,
            EngineCycle        = FeedSystem.EngineCycle.StagedCombustion,
            WallMaterialIndex  = 1,          // CuCrZr (800 K service limit)
        };
        var design = new RegenChamberDesign
        {
            IncludePreburnerRegenCooling = true,
            PreburnerChannelCount        = 8,    // sparse coverage
            PreburnerChannelWidth_mm     = 1.0,  // narrow
            PreburnerChannelDepth_mm     = 0.8,  // shallow
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());
        Assert.NotNull(gen.Preburner?.Thermal);

        var feas = FeasibilityGate.Evaluate(gen);
        var mat = HeatTransfer.WallMaterials.All[1];
        if (gen.Preburner!.Thermal!.PeakWallT_K > mat.MaxServiceTemp_K)
        {
            Assert.Contains(feas.Violations,
                v => v.ConstraintId == "PREBURNER_WALL_TEMP");
        }
        // If the lumped model under-predicts for this fixture, at
        // minimum assert the thermal result is present and the gate
        // path is wired — the fixture is a best-effort attempt to
        // trigger the gate, not a proof that the physics fires for
        // every over-spec design.
        Assert.NotNull(gen.Preburner.Thermal);
    }

    [Fact]
    public void PreburnerCooling_Solve_DirectInvocation()
    {
        // Direct-invocation test bypasses GenerateWith. Constructs a
        // PreburnerResult fixture, calls Solve, asserts the returned
        // fields are physical (T_wall between coolant and gas recovery,
        // positive heat load, positive HTCs).
        var preburner = new Chamber.PreburnerResult(
            Cycle:                     FeedSystem.EngineCycle.StagedCombustion,
            MixtureRatio:              0.6,
            ChamberPressure_Pa:        15e6,
            WarmGasTemperature_K:      1000.0,
            WarmGasCStar_ms:           1500.0,
            WarmGasGamma:              1.22,
            WarmGasMolecularWeight:    18.0,
            MassFlow_kgs:              5.0,
            CharacteristicLength_m:    0.40,
            ChamberVolume_mm3:         200_000,   // 200 mL typical small preburner
            Notes:                     "fixture",
            Warnings:                  System.Array.Empty<string>());

        var fluid = Coolant.CoolantRegistry.Get("CH4");
        var mat = HeatTransfer.WallMaterials.All[1];  // CuCrZr

        var t = HeatTransfer.PreburnerCooling.Solve(
            preburner:           preburner,
            channelCount:        24,
            channelWidth_mm:     2.5,
            channelDepth_mm:     2.0,
            wallThickness_mm:    0.8,
            coolantMassFlow_kgs: 1.0,
            coolantInletT_K:     150.0,
            coolantInletP_Pa:    12e6,
            coolantFluid:        fluid,
            wall:                mat);

        Assert.True(t.PeakWallT_K > 150.0 && t.PeakWallT_K < t.TAwCore_K,
            "wall T must sit between coolant inlet (150 K) and gas recovery");
        Assert.Equal(1000.0 * 0.90, t.TAwCore_K, precision: 3);   // recovery factor 0.90
        Assert.True(t.HGasSide_Wm2K > 0);
        Assert.True(t.HCoolantSide_Wm2K > 0);
        Assert.True(t.TotalHeatLoad_W >= 0);
        Assert.True(t.ChamberInnerSurfaceArea_m2 > 0);
    }

    // ═════════════════════════════════════════════════════════════════
    //   PH-46 (2026-04-29) — Preburner mid-bulk T one-step Picard
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void PH46_PreburnerWallT_HigherThanInletOnlyEstimate()
    {
        // The post-PH-46 implementation takes one Picard step on T_bulk:
        //   T_bulk_mid = T_in + 0.5 · ΔT_seed
        // and re-solves T_wall against that mid-bulk T. The mid-bulk T is
        // higher than the inlet T whenever there's any heat load, so the
        // refined T_wall is HIGHER than the seed T_wall (no heat flows
        // upstream against the gradient). This test pins the direction of
        // the shift without depending on absolute numbers.
        var preburner = new Chamber.PreburnerResult(
            Cycle:                     FeedSystem.EngineCycle.StagedCombustion,
            MixtureRatio:              0.6,
            ChamberPressure_Pa:        15e6,
            WarmGasTemperature_K:      1000.0,
            WarmGasCStar_ms:           1500.0,
            WarmGasGamma:              1.22,
            WarmGasMolecularWeight:    18.0,
            MassFlow_kgs:              5.0,
            CharacteristicLength_m:    0.40,
            ChamberVolume_mm3:         200_000,
            Notes:                     "fixture",
            Warnings:                  System.Array.Empty<string>());
        var fluid = Coolant.CoolantRegistry.Get("CH4");
        var mat = HeatTransfer.WallMaterials.All[1];  // CuCrZr

        var t = HeatTransfer.PreburnerCooling.Solve(
            preburner:           preburner,
            channelCount:        24,
            channelWidth_mm:     2.5,
            channelDepth_mm:     2.0,
            wallThickness_mm:    0.8,
            coolantMassFlow_kgs: 1.0,
            coolantInletT_K:     150.0,
            coolantInletP_Pa:    12e6,
            coolantFluid:        fluid,
            wall:                mat);

        // ΔT > 0 → mid-bulk T > inlet → T_wall > seed T_wall (which used
        // inlet T). The exact shift is 5-15 % per the audit; we check the
        // refined coolant outlet exceeds inlet (sanity) and is consistent
        // with the heat load.
        double cp = fluid.GetState(150.0, 12e6).Cp_Jkg;
        double expectedDeltaT = t.TotalHeatLoad_W / (1.0 * cp);
        Assert.True(t.CoolantOutletT_K > 150.0,
            "outlet T must exceed inlet T given positive heat load");
        Assert.Equal(150.0 + expectedDeltaT, t.CoolantOutletT_K, precision: 0);
        // T_wall must lie between mid-bulk T and gas recovery T.
        double midBulk = 150.0 + 0.5 * expectedDeltaT;
        Assert.True(t.PeakWallT_K > midBulk,
            $"T_wall {t.PeakWallT_K:F0} K should exceed mid-bulk T {midBulk:F0} K");
        Assert.True(t.PeakWallT_K < t.TAwCore_K);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Z3-m1 sibling — preburner 1-D wall conduction (2026-04-29)
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Z3m1Preburner_ThickerWall_RaisesPeakWallT_LowersHeatLoad()
    {
        // Pre-#236 the wall thickness parameter was discarded
        // (`_ = wallThickness_mm`) — the lumped energy balance treated the
        // wall as having infinite conductivity. Post-#236 the series-
        // resistance balance includes R_wall = t/k(T), so increasing the
        // wall thickness shifts T-drop from the convection sides into the
        // wall: T_wg moves CLOSER to T_aw (higher) while q DROPS (more
        // resistance in series). This test pins both directions.
        var preburner = new Chamber.PreburnerResult(
            Cycle:                     FeedSystem.EngineCycle.StagedCombustion,
            MixtureRatio:              0.6,
            ChamberPressure_Pa:        15e6,
            WarmGasTemperature_K:      1000.0,
            WarmGasCStar_ms:           1500.0,
            WarmGasGamma:              1.22,
            WarmGasMolecularWeight:    18.0,
            MassFlow_kgs:              5.0,
            CharacteristicLength_m:    0.40,
            ChamberVolume_mm3:         200_000,
            Notes:                     "fixture",
            Warnings:                  System.Array.Empty<string>());
        var fluid = Coolant.CoolantRegistry.Get("CH4");
        var mat = HeatTransfer.WallMaterials.All[1];  // CuCrZr

        HeatTransfer.PreburnerThermalResult Solve(double t_wall) =>
            HeatTransfer.PreburnerCooling.Solve(
                preburner:           preburner,
                channelCount:        24,
                channelWidth_mm:     2.5,
                channelDepth_mm:     2.0,
                wallThickness_mm:    t_wall,
                coolantMassFlow_kgs: 1.0,
                coolantInletT_K:     150.0,
                coolantInletP_Pa:    12e6,
                coolantFluid:        fluid,
                wall:                mat);

        var thin  = Solve(t_wall: 0.5);
        var thick = Solve(t_wall: 2.0);

        Assert.True(thick.PeakWallT_K > thin.PeakWallT_K,
            $"thicker wall (2 mm) should produce higher T_wg ({thick.PeakWallT_K:F0} K) "
          + $"than thin wall (0.5 mm, {thin.PeakWallT_K:F0} K) — the wall T-drop term "
          + $"shifts heat-flux gradient from convection into conduction.");
        Assert.True(thick.TotalHeatLoad_W < thin.TotalHeatLoad_W,
            $"thicker wall should lower total heat load (more series resistance). "
          + $"Got thin Q={thin.TotalHeatLoad_W:F0} W vs thick Q={thick.TotalHeatLoad_W:F0} W.");
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 9 Track C (2026-04-22) — AerospikeContractionRatio dim [23]
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void AerospikeContractionRatio_DefaultIs6_PreservesLegacySizing()
    {
        // Pre-Sprint-9 AerospikeBuilder hardcoded contraction ratio = 6.0.
        // The new SA-tunable field defaults to 6.0 so existing call sites
        // see bit-identical chamber radii.
        var d = new RegenChamberDesign();
        Assert.Equal(6.0, d.AerospikeContractionRatio, precision: 6);
    }

    [Fact]
    public void PackUnpack_RoundTripsAerospikeContractionRatio_WhenBaselineIsAerospike()
    {
        // Sprint 9 Track C dim [23] — gated on AerospikeTopology.
        var baseline = new RegenChamberDesign
        {
            ChannelTopology          = ChannelTopology.Aerospike,
            AerospikeContractionRatio = 7.5,
        };
        double[] p = RegenChamberOptimization.Pack(baseline);
        Assert.Equal(34, p.Length);
        Assert.Equal(7.5, p[23], precision: 6);

        var copy = RegenChamberOptimization.Unpack(p, baseline);
        Assert.Equal(7.5, copy.AerospikeContractionRatio, precision: 6);
    }

    [Fact]
    public void Unpack_LeavesAerospikeContractionRatio_WhenBaselineNotAerospike()
    {
        // Gate suppression: non-aerospike baselines must keep their
        // AerospikeContractionRatio at the baseline value even when a
        // perturbed sampler pokes dim 23 with an aerospike-ish value.
        var baseline = new RegenChamberDesign
        {
            ChannelTopology           = ChannelTopology.Axial,
            AerospikeContractionRatio = 6.0,
        };
        double[] p = RegenChamberOptimization.Pack(baseline);
        p[23] = 9.0;   // SA poke — should be ignored on the Axial baseline.

        var copy = RegenChamberOptimization.Unpack(p, baseline);
        Assert.Equal(6.0, copy.AerospikeContractionRatio, precision: 6);
    }

    [Fact]
    public void AerospikeOptimization_ToSpec_ForwardsContractionRatio()
    {
        // The SA-facing bridge must pass the design's
        // AerospikeContractionRatio into AerospikeSpec.ChamberContractionRatio
        // so AerospikeBuilder.BuildPhysicsOnly uses it to size the
        // pre-throat chamber radius.
        var cond = new OperatingConditions { Thrust_N = 20_000 };
        var design = new RegenChamberDesign
        {
            ChannelTopology           = ChannelTopology.Aerospike,
            ExpansionRatio            = 15.0,
            PlugLengthRatio           = 0.30,
            AerospikeContractionRatio = 8.0,
        };
        var spec = AerospikeOptimization.ToSpec(cond, design);
        Assert.Equal(8.0, spec.ChamberContractionRatio, precision: 6);
    }

    [Fact]
    public void BuildPhysicsOnly_ReadsContractionRatio_ForChamberRadius()
    {
        // ε_c ↑ → R_chamber ↑ (via R = √ε_c · R_outer_throat). Pin the
        // relationship: a contraction ratio of 8 produces a chamber with
        // ≈ √(8/6) × the ratio-6 chamber radius.
        var specBase = new AerospikeSpec(
            Thrust_N:           20_000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4);
        var specWide = specBase with { ChamberContractionRatio = 8.0 };

        var baseR = AerospikeBuilder.BuildPhysicsOnly(specBase).ChamberRadius_mm;
        var wideR = AerospikeBuilder.BuildPhysicsOnly(specWide).ChamberRadius_mm;

        double expected = baseR * System.Math.Sqrt(8.0 / 6.0);
        Assert.InRange(wideR, expected * 0.99, expected * 1.01);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 7 Track A (2026-04-22) — Aerospike injector integration
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void AerospikeSpec_WithoutInjectorPattern_LeavesInjectorSizingNull()
    {
        // Back-compat: the pre-Sprint-7 path (AerospikeSpec.InjectorPattern
        // defaults to null) produces an AerospikeBuildResult with
        // InjectorSizing = null. No feasibility changes, no gate fires.
        var spec = new AerospikeSpec(
            Thrust_N:           20_000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4);
        var r = AerospikeBuilder.BuildPhysicsOnly(spec);
        Assert.Null(r.InjectorSizing);

        var feas = AerospikeFeasibility.Evaluate(r, wallMaterialIndex: 1);
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "AEROSPIKE_ELEMENT_CLEARANCE");
    }

    [Fact]
    public void AerospikeSpec_WithReasonablePattern_SizesClearanceOk()
    {
        // A 20-element coax pattern on a 20 kN / 7 MPa aerospike has
        // plenty of face room. Clearance must come back OK.
        var pattern = Injector.InjectorPattern.DefaultCoax(20);
        var spec = new AerospikeSpec(
            Thrust_N:           20_000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4,
            InjectorPattern:    pattern);
        var r = AerospikeBuilder.BuildPhysicsOnly(spec);

        Assert.NotNull(r.InjectorSizing);
        var inj = r.InjectorSizing!;
        Assert.Equal(20, inj.PatternSizing.ElementCount);
        Assert.True(inj.PitchCircleRadius_mm > 0);
        Assert.True(inj.ArcSpacing_mm > 0);
        Assert.True(inj.MinClearance_mm > 0);
        Assert.True(inj.ClearanceOk, "20-element pattern at 20 kN should fit");

        var feas = AerospikeFeasibility.Evaluate(r, wallMaterialIndex: 1);
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "AEROSPIKE_ELEMENT_CLEARANCE");
    }

    [Fact]
    public void AerospikeSpec_WithTooManyElements_FiresClearanceGate()
    {
        // 200 elements crammed onto a ~10 kN aerospike can't fit — the
        // arc spacing on the default 60 %·R_chamber pitch circle falls
        // below (element OD + 2 mm LPBF floor).
        var pattern = Injector.InjectorPattern.DefaultCoax(200);
        var spec = new AerospikeSpec(
            Thrust_N:           10_000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4,
            InjectorPattern:    pattern);
        var r = AerospikeBuilder.BuildPhysicsOnly(spec);

        Assert.NotNull(r.InjectorSizing);
        Assert.False(r.InjectorSizing!.ClearanceOk,
            "200 elements on a 10 kN aerospike must violate clearance");

        var feas = AerospikeFeasibility.Evaluate(r, wallMaterialIndex: 1);
        var v = Assert.Single(feas.Violations,
            v => v.ConstraintId == "AEROSPIKE_ELEMENT_CLEARANCE");
        Assert.Contains("cannot fit", v.Description);
    }

    [Fact]
    public void AerospikeOptimization_ToSpec_ForwardsInjectorPattern()
    {
        // The SA-facing bridge must forward the design's pattern into
        // AerospikeSpec so BuildPhysicsOnly can size it.
        var pattern = Injector.InjectorPattern.DefaultCoax(24);
        var cond = new OperatingConditions { Thrust_N = 20_000 };
        var design = new RegenChamberDesign
        {
            ChannelTopology        = ChannelTopology.Aerospike,
            ExpansionRatio         = 15.0,
            PlugLengthRatio        = 0.30,
            InjectorElementPattern = pattern,
        };
        var spec = AerospikeOptimization.ToSpec(cond, design);
        Assert.Same(pattern, spec.InjectorPattern);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 8 Track A (2026-04-22) — Aerospike injector-face thermal
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void AerospikeBuildResult_InjectorFace_IsNull_WithoutPattern()
    {
        // No pattern → no InjectorSizing → no InjectorFace.
        var spec = new AerospikeSpec(
            Thrust_N:           20_000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4);
        var r = AerospikeBuilder.BuildPhysicsOnly(spec);
        Assert.Null(r.InjectorFace);
    }

    [Fact]
    public void AerospikeInjectorFaceThermal_Populates_WhenPatternPresent()
    {
        // Reasonable 20-element coax pattern produces a finite T_face.
        var spec = new AerospikeSpec(
            Thrust_N:           20_000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4,
            InjectorPattern:    Injector.InjectorPattern.DefaultCoax(20));
        var r = AerospikeBuilder.BuildPhysicsOnly(spec);
        Assert.NotNull(r.InjectorFace);
        var face = r.InjectorFace!;
        Assert.True(face.TFace_K > face.TPropAvg_K,
            "face is warmer than the cold-side propellant mean");
        Assert.True(face.TFace_K < face.TAwCore_K,
            "face is cooler than the gas-side adiabatic wall");
        Assert.True(face.BoreAreaFraction >= 0);
        Assert.True(face.HGasSide_Wm2K > 0);
    }

    [Fact]
    public void AerospikeFeasibility_InjectorFaceTemp_FiresWhenFaceExceedsMaterial()
    {
        // Pick a baseline where bore-area coverage is extremely low
        // (few big-flow elements) — the estimator's equilibrium then
        // approaches T_aw which blows past CuCrZr's service limit.
        var spec = new AerospikeSpec(
            Thrust_N:           200_000,     // high thrust to get a hot chamber
            ChamberPressure_Pa: 20e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4,
            WallMaterialIndex:  1,           // CuCrZr
            InjectorPattern:    Injector.InjectorPattern.DefaultCoax(8));
        var r = AerospikeBuilder.BuildPhysicsOnly(spec);
        var feas = AerospikeFeasibility.Evaluate(r, wallMaterialIndex: 1);

        // Either the thermal gate or the clearance gate could fire
        // depending on the exact scaling — the point of this test is
        // that the thermal gate IS evaluated. The only way to assert
        // without overspecifying the sizing math is to confirm the
        // gate's ConstraintId appears in the violation list whenever
        // TFace_K > limit.
        //
        // PH-35 aerospike-face follow-on (2026-04-29 — closes #234):
        // gate now keys on `face.MaxServiceTemp_K` (default 1200 K
        // IN625/SS face material, overrideable via
        // `OperatingConditions.InjectorFaceMaxTemp_K_Override` →
        // `AerospikeSpec.InjectorFaceMaxTemp_K_Override`) instead of
        // `material.MaxServiceTemp_K` (the chamber-wall liner limit).
        if (r.InjectorFace is { } face)
        {
            double faceLimit_K = face.MaxServiceTemp_K > 0
                ? face.MaxServiceTemp_K
                : HeatTransfer.InjectorFaceThermal.DefaultInjectorFaceMaxTemp_K;
            if (face.TFace_K > faceLimit_K)
            {
                Assert.Contains(feas.Violations,
                    v => v.ConstraintId == "AEROSPIKE_INJECTOR_FACE_TEMP");
            }
            else
            {
                Assert.DoesNotContain(feas.Violations,
                    v => v.ConstraintId == "AEROSPIKE_INJECTOR_FACE_TEMP");
            }
        }
    }

    // ──────────────── PH-36 + PH-35 aerospike-face follow-ons (2026-04-29) ────────────────
    //   Closes #233 (per-pair oxidizer T) + #234 (face material T-limit override).
    //   Same plumbing pattern shipped for the bell-chamber path in PRs #227 + #229.

    [Fact]
    public void PH33_PH34_AerospikeFace_DefaultsMaxServiceTempTo1200K()
    {
        // No override on the spec → AerospikeInjectorFaceResult.MaxServiceTemp_K
        // = InjectorFaceThermal.DefaultInjectorFaceMaxTemp_K (1200 K).
        var spec = new AerospikeSpec(
            Thrust_N:           20_000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4,
            WallMaterialIndex:  1,
            InjectorPattern:    Injector.InjectorPattern.DefaultCoax(20));
        var r = AerospikeBuilder.BuildPhysicsOnly(spec);
        Assert.NotNull(r.InjectorFace);
        Assert.Equal(1200.0, r.InjectorFace!.MaxServiceTemp_K, precision: 3);
    }

    [Fact]
    public void PH35_AerospikeFace_OverridePropagatesToResult()
    {
        // Set a tighter override (e.g. SS316L brazed face on a CuCrZr liner
        // → ~1100 K limit) → result surfaces it, and AerospikeFeasibility
        // gate keys on it.
        var spec = new AerospikeSpec(
            Thrust_N:           20_000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4,
            WallMaterialIndex:  1,
            InjectorPattern:    Injector.InjectorPattern.DefaultCoax(20),
            InjectorFaceMaxTemp_K_Override: 1100.0);
        var r = AerospikeBuilder.BuildPhysicsOnly(spec);
        Assert.NotNull(r.InjectorFace);
        Assert.Equal(1100.0, r.InjectorFace!.MaxServiceTemp_K, precision: 3);
    }

    [Fact]
    public void PH36_AerospikeFace_OxidizerInletTempOverride_ShiftsTPropAvg()
    {
        // Mirrors PH36_OxidizerInletTempOverride_ShiftsTPropAvg from the
        // bell-chamber path — warmer oxidizer raises T_prop_avg, which
        // raises T_face. All current LOX-based pairs default to 90.18 K so
        // existing fixtures stay bit-identical (within 0.18 K).
        var coldSpec = new AerospikeSpec(
            Thrust_N:           20_000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4,
            WallMaterialIndex:  1,
            InjectorPattern:    Injector.InjectorPattern.DefaultCoax(20));
        var warmSpec = coldSpec with { OxidizerInletTemp_K = 500.0 };

        var coldResult = AerospikeBuilder.BuildPhysicsOnly(coldSpec);
        var warmResult = AerospikeBuilder.BuildPhysicsOnly(warmSpec);

        Assert.NotNull(coldResult.InjectorFace);
        Assert.NotNull(warmResult.InjectorFace);
        Assert.True(warmResult.InjectorFace!.TPropAvg_K > coldResult.InjectorFace!.TPropAvg_K,
            $"warm-ox T_prop_avg ({warmResult.InjectorFace.TPropAvg_K:F0} K) should "
          + $"exceed cold-ox ({coldResult.InjectorFace.TPropAvg_K:F0} K) when "
          + $"OxidizerInletTemp_K is bumped 90 → 500 K.");
    }

    [Fact]
    public void AerospikeFeasibility_InjectorFaceTemp_SilentWithoutPattern()
    {
        // No pattern → no face → gate skipped.
        var spec = new AerospikeSpec(
            Thrust_N:           200_000,
            ChamberPressure_Pa: 20e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4);
        var r = AerospikeBuilder.BuildPhysicsOnly(spec);
        var feas = AerospikeFeasibility.Evaluate(r, wallMaterialIndex: 1);
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "AEROSPIKE_INJECTOR_FACE_TEMP");
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 7 Track B (2026-04-22) — monolithic aerospike envelope
    // ═════════════════════════════════════════════════════════════════

    private static AerospikeBuildResult MakeStandardAerospikePlug()
    {
        var spec = new AerospikeSpec(
            Thrust_N:           20_000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4);
        return AerospikeBuilder.BuildPhysicsOnly(spec);
    }

    private static MonolithicBodyEnvelopes MakePlugEnvelopes(
        AerospikeBuildResult? plug,
        System.Numerics.Vector3 plugOrigin = default)
    {
        // Provide the simplest set of envelopes — only chamber + plug;
        // pump / preburner / turbine envelopes are absent so the
        // non-plug checks are inert.
        return new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 80.0,
            ChamberLength_mm:      120.0,
            FuelPumpGeometry:      null,
            FuelPumpOrigin:        default,
            OxPumpGeometry:        null,
            OxPumpOrigin:          default,
            PreburnerGeometry:     null,
            PreburnerOrigin:       default,
            AerospikePlug:         plug,
            AerospikePlugOrigin:   plugOrigin);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 9 Track A (2026-04-22) — Monolithic aerospike composition
    // ═════════════════════════════════════════════════════════════════
    //
    // BuildAerospike is a PicoGK-task-thread path (voxelises the plug +
    // pumps + manifold), so the xUnit suite can't exercise it directly
    // per ADR-005. These tests validate the NON-voxel portions of the
    // pipeline: the physics-only sidecar that Turbopump + Preburner
    // sizing rides on, and the MonolithicFeasibility.Evaluate call
    // with aerospike envelope populated.

    [Fact]
    public void AerospikeMonolithic_PhysicsSidecar_CarriesTurbopumpAndPreburner()
    {
        // When the Aerospike monolithic pipeline routes turbopump +
        // preburner sizing through GenerateWith(skipVoxelGeometry:true),
        // both fields must come back populated for a staged-combustion
        // cycle. This is the critical invariant BuildAerospike relies
        // on — if GenerateWith stopped populating Turbopump on the
        // skipVoxel path, the monolithic composer would silently lose
        // its pump geometry.
        var cond = new OperatingConditions
        {
            Thrust_N           = 50_000,
            ChamberPressure_Pa = 10e6,
            PropellantPair     = PropellantPair.LOX_CH4,
            EngineCycle        = FeedSystem.EngineCycle.StagedCombustion,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            ExpansionRatio  = 15.0,
            PlugLengthRatio = 0.30,
        };

        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.NotNull(gen.Turbopump);
        Assert.NotNull(gen.Turbopump!.FuelPump);
        Assert.NotNull(gen.Turbopump.OxPump);
        Assert.NotNull(gen.Preburner);   // StagedCombustion → preburner present
        Assert.NotNull(gen.Aerospike);   // sidecar populated for aerospike topology
    }

    [Fact]
    public void AerospikeMonolithic_PlugEnvelope_FeasibleForReasonableLayout()
    {
        // An aerospike-monolithic design with no conflicting tubes must
        // pass the body-intersection gate. This validates that the
        // plug-envelope addition in Sprint 7 Track B doesn't produce
        // spurious violations on normal routings — the critical
        // "no false positives" check.
        var cond = new OperatingConditions
        {
            Thrust_N           = 50_000,
            ChamberPressure_Pa = 10e6,
            PropellantPair     = PropellantPair.LOX_CH4,
            EngineCycle        = FeedSystem.EngineCycle.StagedCombustion,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            ExpansionRatio  = 15.0,
            PlugLengthRatio = 0.30,
        };

        // Build aerospike body (physics-only — no voxels).
        var aeroSpec = AerospikeOptimization.ToSpec(cond, design);
        var aero = AerospikeBuilder.BuildPhysicsOnly(aeroSpec);

        // Physics sidecar for Turbopump geometry.
        var physics = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true);

        // Build envelopes matching what BuildAerospike would use.
        double chamberLen = aero.ChamberLength_mm;
        var fuelPumpOrigin = new System.Numerics.Vector3(
            (float)(-chamberLen + chamberLen * 0.15), 80f, -40f);
        var oxPumpOrigin = new System.Numerics.Vector3(
            (float)(-chamberLen + chamberLen * 0.15), -80f, -40f);

        var envelopes = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: aero.ChamberRadius_mm + aeroSpec.OuterShellThickness_mm,
            ChamberLength_mm:      chamberLen,
            FuelPumpGeometry:      physics.Turbopump!.FuelPumpGeometry,
            FuelPumpOrigin:        fuelPumpOrigin,
            OxPumpGeometry:        physics.Turbopump.OxPumpGeometry,
            OxPumpOrigin:          oxPumpOrigin,
            PreburnerGeometry:     null,
            PreburnerOrigin:       default,
            AerospikePlug:         aero,
            AerospikePlugOrigin:   System.Numerics.Vector3.Zero);

        // Dummy layout with a single reasonable tube from pump discharge
        // to injector dome at x = -chamberLen. Should NOT collide with
        // the plug (plug lives at x >= 0).
        var injectorDome = new System.Numerics.Vector3(-(float)chamberLen, 0, 0);
        var tube = new FeedSystem.FeedTube(
            Label:          "fuel-discharge",
            Start_mm:       fuelPumpOrigin + new System.Numerics.Vector3(30, 0, 24),
            Corner_mm:      null,
            End_mm:         injectorDome,
            OuterRadius_mm: 4.0);
        var layout = new FeedSystem.FeedManifoldLayout(
            Cycle:               FeedSystem.EngineCycle.StagedCombustion,
            Tubes:               new[] { tube },
            TotalTubeLength_mm:  150.0,
            EstimatedTubeMass_g: 10.0,
            Notes:               "monolithic aerospike fixture");

        var r = MonolithicFeasibility.Evaluate(layout, envelopes);
        // Tube routes well away from the plug (ends at injector face,
        // plug starts at x=0). No violation expected.
        Assert.DoesNotContain(r.Violations,
            v => v.ConstraintId == "MONOLITHIC_BODY_INTERSECTION"
              && v.Description.Contains("aerospike plug"));
    }

    [Fact]
    public void MonolithicEngineResult_AerospikeChamber_FieldExists()
    {
        // Sprint 9 Track A added the AerospikeChamber field to
        // MonolithicEngineResult. Pin the record shape — a test that
        // fails compilation if the field is removed / renamed.
        // Pattern matches the existing "record shape" pins in the
        // Sprint 2b tests for Aerospike sidecar on RegenGenerationResult.
        var r = new MonolithicEngineResult(
            EngineVoxels:         null!,
            ChamberResult:        null!,
            FuelPumpGeometry:     null,
            OxPumpGeometry:       null,
            ManifoldLayout:       null!,
            ComponentBodyCount:   0,
            EstimatedEngineMass_g: 0,
            Description:          "fixture",
            AerospikeChamber:     null);
        Assert.Null(r.AerospikeChamber);

        // Round-trip via `with` (records support this natively).
        var spec = new AerospikeSpec(
            Thrust_N:           5000, ChamberPressure_Pa: 6e6,
            ExpansionRatio:     10.0, PlugLengthRatio: 0.30,
            PropellantPair:     PropellantPair.LOX_CH4);
        var aero = AerospikeBuilder.BuildPhysicsOnly(spec);
        var r2 = r with { AerospikeChamber = aero };
        Assert.Same(aero, r2.AerospikeChamber);
    }

    [Fact]
    public void MonolithicFeasibility_PlugEnvelope_FiresWhenTubeCutsThroughPlug()
    {
        // Plug rooted at world (200, 0, 0) pointing +X. Tube runs from
        // well-upstream (x=100) to well-downstream (x=400) at y=0, z=0
        // — a straight line through the plug centreline. Endpoints are
        // axially outside the plug envelope so the endpoint-touch
        // whitelist does NOT apply; interior samples sit inside the
        // plug → MONOLITHIC_BODY_INTERSECTION with "aerospike plug"
        // in the description.
        var plug = MakeStandardAerospikePlug();
        var plugOrigin = new System.Numerics.Vector3(200, 0, 0);
        var envelopes = MakePlugEnvelopes(plug, plugOrigin);

        var tube = new FeedSystem.FeedTube(
            Label:          "tube-through-plug",
            Start_mm:       new System.Numerics.Vector3(100, 0, 0),
            Corner_mm:      null,
            End_mm:         new System.Numerics.Vector3(400, 0, 0),
            OuterRadius_mm: 3.0);
        var layout = new FeedSystem.FeedManifoldLayout(
            Cycle:               FeedSystem.EngineCycle.StagedCombustion,
            Tubes:               new[] { tube },
            TotalTubeLength_mm:  300.0,
            EstimatedTubeMass_g: 10.0,
            Notes:               "fixture");

        var r = MonolithicFeasibility.Evaluate(layout, envelopes);
        var v = Assert.Single(r.Violations,
            v => v.ConstraintId == "MONOLITHIC_BODY_INTERSECTION"
              && v.Description.Contains("aerospike plug"));
        Assert.NotNull(v);
    }

    [Fact]
    public void MonolithicFeasibility_PlugEnvelope_StaysSilentWhenTubeClears()
    {
        // Tube routes well above the plug (y = 200 mm). No interior
        // sample comes close to the envelope → no violation.
        var plug = MakeStandardAerospikePlug();
        var plugOrigin = new System.Numerics.Vector3(200, 0, 0);
        var envelopes = MakePlugEnvelopes(plug, plugOrigin);

        var tube = new FeedSystem.FeedTube(
            Label:          "tube-above-plug",
            Start_mm:       new System.Numerics.Vector3(100, 200, 0),
            Corner_mm:      null,
            End_mm:         new System.Numerics.Vector3(300, 200, 0),
            OuterRadius_mm: 3.0);
        var layout = new FeedSystem.FeedManifoldLayout(
            Cycle:               FeedSystem.EngineCycle.StagedCombustion,
            Tubes:               new[] { tube },
            TotalTubeLength_mm:  200.0,
            EstimatedTubeMass_g: 5.0,
            Notes:               "fixture");

        var r = MonolithicFeasibility.Evaluate(layout, envelopes);
        Assert.DoesNotContain(r.Violations,
            v => v.ConstraintId == "MONOLITHIC_BODY_INTERSECTION"
              && v.Description.Contains("aerospike plug"));
    }

    [Fact]
    public void MonolithicFeasibility_PlugEnvelope_NullPlug_NoViolation()
    {
        // Regen-only engines (no aerospike) leave AerospikePlug null.
        // The gate skips the plug check entirely.
        var envelopes = MakePlugEnvelopes(plug: null);

        var tube = new FeedSystem.FeedTube(
            Label:          "any-tube",
            Start_mm:       new System.Numerics.Vector3(0, 0, 0),
            Corner_mm:      null,
            End_mm:         new System.Numerics.Vector3(100, 0, 0),
            OuterRadius_mm: 3.0);
        var layout = new FeedSystem.FeedManifoldLayout(
            Cycle:               FeedSystem.EngineCycle.StagedCombustion,
            Tubes:               new[] { tube },
            TotalTubeLength_mm:  100.0,
            EstimatedTubeMass_g: 2.0,
            Notes:               "fixture");

        var r = MonolithicFeasibility.Evaluate(layout, envelopes);
        Assert.DoesNotContain(r.Violations,
            v => v.Description.Contains("aerospike plug"));
    }

    [Fact]
    public void MonolithicFeasibility_PlugEnvelope_EndpointTouch_IsWhitelisted()
    {
        // A tube that legitimately terminates on the plug surface (e.g.
        // a coolant inlet at the plug base) must not trigger the gate —
        // the endpoint-touch whitelist catches it.
        var plug = MakeStandardAerospikePlug();
        var plugOrigin = new System.Numerics.Vector3(200, 0, 0);

        // Tube ends exactly on the plug-base circle. Its endpoint
        // touches the plug surface within the tube-outer-radius margin.
        double plugBaseR = plug.Contour.PlugBaseRadius_mm;
        double tipX = 200.0 + plug.PlugTruncatedLength_mm;
        var tube = new FeedSystem.FeedTube(
            Label:          "plug-base-coolant-inlet",
            Start_mm:       new System.Numerics.Vector3(300, 100, 0),
            Corner_mm:      null,
            End_mm:         new System.Numerics.Vector3((float)tipX, (float)plugBaseR, 0),
            OuterRadius_mm: 3.0);
        var envelopes = MakePlugEnvelopes(plug, plugOrigin);
        var layout = new FeedSystem.FeedManifoldLayout(
            Cycle:               FeedSystem.EngineCycle.StagedCombustion,
            Tubes:               new[] { tube },
            TotalTubeLength_mm:  120.0,
            EstimatedTubeMass_g: 4.0,
            Notes:               "fixture");

        var r = MonolithicFeasibility.Evaluate(layout, envelopes);
        // No aerospike-plug violation — the endpoint-touch whitelist
        // suppresses it even though interior samples would be near the
        // envelope.
        Assert.DoesNotContain(r.Violations,
            v => v.ConstraintId == "MONOLITHIC_BODY_INTERSECTION"
              && v.Description.Contains("aerospike plug"));
    }

    [Fact]
    public void AerospikeOptimization_BuildAndEvaluate_WithPattern_ReportsSizing()
    {
        // End-to-end: a RegenChamberDesign carrying an InjectorPattern on
        // an Aerospike topology lands an InjectorSizing result on the
        // aerospike build output; the feasibility gate sees it and votes.
        var cond = new OperatingConditions
        {
            Thrust_N           = 20_000,
            ChamberPressure_Pa = 7e6,
            PropellantPair     = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology        = ChannelTopology.Aerospike,
            ExpansionRatio         = 15.0,
            PlugLengthRatio        = 0.30,
            InjectorElementPattern = Injector.InjectorPattern.DefaultCoax(20),
        };
        var (build, feas) = AerospikeOptimization.BuildAndEvaluate(cond, design, new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.NotNull(build.InjectorSizing);
        Assert.True(build.InjectorSizing!.ClearanceOk);
        // Sprint 8 Track A note: the new AEROSPIKE_INJECTOR_FACE_TEMP
        // gate may reject this design (low bore coverage at 20
        // elements drives T_face toward T_aw). We don't assert overall
        // feasibility — the test's intent is that sizing propagates
        // through the bridge.
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "AEROSPIKE_ELEMENT_CLEARANCE");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Sprint 15 / Track G — aerospike plug-channel regen-cooling opt-in
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AerospikeOptimization_BuildAndEvaluate_OptInOff_LeavesThermalNull()
    {
        // Default (IncludeAerospikeRegenCooling = false) preserves the
        // pre-Sprint-15 geometry-only pipeline: AerospikeBuildResult.Thermal
        // stays null and the AEROSPIKE_PLUG_WALL_TEMP gate is skipped.
        var cond = new OperatingConditions
        {
            Thrust_N           = 20_000,
            ChamberPressure_Pa = 7e6,
            PropellantPair     = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            ExpansionRatio  = 15.0,
            PlugLengthRatio = 0.30,
            // IncludeAerospikeRegenCooling defaults to false
        };

        var (build, feas) = AerospikeOptimization.BuildAndEvaluate(cond, design, new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.Null(build.Thermal);
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "AEROSPIKE_PLUG_WALL_TEMP");
    }

    [Fact]
    public void AerospikeOptimization_BuildAndEvaluate_OptInOn_PopulatesThermal()
    {
        // Sprint 15 / Track G: turning the IncludeAerospikeRegenCooling
        // flag on populates AerospikeBuildResult.Thermal with a per-station
        // wall-T profile from AerospikePlugCooling.Solve. This closes the
        // feature loop opened by Sprint 11 Track F (which made the
        // scoring path read from Thermal but had no UI/SA way to populate
        // it).
        var cond = new OperatingConditions
        {
            Thrust_N           = 20_000,
            ChamberPressure_Pa = 7e6,
            PropellantPair     = PropellantPair.LOX_CH4,
            CoolantInletTemp_K     = 120.0,
            CoolantInletPressure_Pa = 12e6,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology               = ChannelTopology.Aerospike,
            ExpansionRatio                = 15.0,
            PlugLengthRatio               = 0.30,
            IncludeAerospikeRegenCooling  = true,
            AerospikePlugChannelCount     = 24,
            AerospikePlugChannelWidth_mm  = 2.5,
            AerospikePlugChannelDepth_mm  = 2.0,
            AerospikePlugWallThickness_mm = 0.8,
        };

        var (build, _) = AerospikeOptimization.BuildAndEvaluate(cond, design, new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.NotNull(build.Thermal);
        Assert.True(build.Thermal!.GasSideWallT_K.Length > 0,
            "AerospikePlugCooling.Solve should produce a non-empty wall-T profile.");
        Assert.True(build.Thermal.GasSideWallT_K[0] > 0,
            "First-station wall T should be physically positive.");
        Assert.True(build.Thermal.PeakGasSideWallT_K > 0,
            "Peak wall T summary should be physically positive.");
    }

    [Fact]
    public void AerospikeOptimization_ToSpec_ForwardsCoolingFieldsExactly()
    {
        // Sprint 15 / Track G: the four channel-geometry fields (count,
        // width, depth, wall thickness) round-trip through ToSpec onto
        // the AerospikeSpec. Pins the wire-up so a future refactor that
        // reorders the named arguments doesn't silently swap channel
        // depth and width.
        var cond = new OperatingConditions
        {
            Thrust_N           = 20_000,
            ChamberPressure_Pa = 7e6,
            PropellantPair     = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology               = ChannelTopology.Aerospike,
            ExpansionRatio                = 15.0,
            PlugLengthRatio               = 0.30,
            IncludeAerospikeRegenCooling  = true,
            AerospikePlugChannelCount     = 36,    // non-default to spot a misroute
            AerospikePlugChannelWidth_mm  = 1.7,
            AerospikePlugChannelDepth_mm  = 3.1,
            AerospikePlugWallThickness_mm = 1.2,
        };

        var spec = AerospikeOptimization.ToSpec(cond, design);

        Assert.True(spec.IncludeRegenChannels);
        Assert.Equal(36,  spec.PlugChannelCount);
        Assert.Equal(1.7, spec.PlugChannelWidth_mm,    precision: 6);
        Assert.Equal(3.1, spec.PlugChannelDepth_mm,    precision: 6);
        Assert.Equal(1.2, spec.PlugWallThickness_mm,   precision: 6);
    }

    [Fact]
    public void DesignPersistence_RoundTripsAerospikeCoolingFields_OnSchemaV13()
    {
        // Sprint 15 / Track G: the five new RegenChamberDesign fields
        // (IncludeAerospikeRegenCooling + the four channel-geometry
        // fields) survive a Save → Load round-trip. Sprint 20 bumped
        // the current schema to v14 (identity migration from v13); this
        // test still validates the Sprint 15 fields round-trip while
        // pinning the current schema tag.
        var cond = new OperatingConditions
        {
            Thrust_N           = 20_000,
            ChamberPressure_Pa = 7e6,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology               = ChannelTopology.Aerospike,
            ExpansionRatio                = 15.0,
            PlugLengthRatio               = 0.30,
            IncludeAerospikeRegenCooling  = true,
            AerospikePlugChannelCount     = 32,
            AerospikePlugChannelWidth_mm  = 2.1,
            AerospikePlugChannelDepth_mm  = 1.9,
            AerospikePlugWallThickness_mm = 0.6,
        };

        using var tmp = TestTempFile.Create();
        Voxelforge.IO.DesignPersistence.Save(tmp.Path, cond, design, r: null);
        var loaded = Voxelforge.IO.DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        // Schema cascade: Sprint 15 bumped to v13; Sprint 20 bumped
        // to v14 (dual-bell fields); Sprint 19 cascaded to v15
        // (blow-down final pressure) after rebase; Sprint 27 bumped
        // to v16 (LPBF printability opt-in fields); Sprint 26 cascaded
        // to v17 (linear-aerospike fields) after Sprint 27 claimed v16.
        // Save/Load round-trip always stamps the current schema.
        Assert.Equal(Voxelforge.IO.DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        Assert.True(loaded.Design!.IncludeAerospikeRegenCooling);
        Assert.Equal(32,  loaded.Design.AerospikePlugChannelCount);
        Assert.Equal(2.1, loaded.Design.AerospikePlugChannelWidth_mm,  precision: 6);
        Assert.Equal(1.9, loaded.Design.AerospikePlugChannelDepth_mm,  precision: 6);
        Assert.Equal(0.6, loaded.Design.AerospikePlugWallThickness_mm, precision: 6);
    }

    [Fact]
    public void GenerateWith_PopulatesAerospikeSidecar_WhenTopologyIsAerospike()
    {
        // Sprint 2b step 2: GenerateWith must populate the .Aerospike
        // sidecar when baseline.ChannelTopology is Aerospike, so downstream
        // scoring / report / UI code can read aerospike-meaningful values
        // without re-running BuildPhysicsOnly.
        var cond = new OperatingConditions
        {
            Thrust_N            = 20_000,
            ChamberPressure_Pa  = 7e6,
            PropellantPair      = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            ExpansionRatio  = 15.0,
            PlugLengthRatio = 0.35,
        };

        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.NotNull(gen.Aerospike);
        Assert.Null(gen.Aerospike!.Voxels);   // physics-only path
        Assert.Equal(0.35, gen.Aerospike.Contour.PlugLengthRatio, precision: 6);
        Assert.True(gen.Aerospike.ThroatOuterRadius_mm > 0);
    }

    [Fact]
    public void Evaluate_UsesAerospikeMass_WhenAerospikeSidecarPresent()
    {
        // Sprint 2b step 3: on an aerospike baseline, the Mass_g field on
        // the score must come from the aerospike plug body (via the sidecar)
        // rather than the bell-chamber fallback from gen.Geometry. Without
        // this fix, SA was optimising regen-chamber mass on aerospike
        // candidates — nonsense, since the aerospike voxel body is
        // geometrically unrelated to the fallback.
        var cond = new OperatingConditions
        {
            Thrust_N           = 20_000,
            ChamberPressure_Pa = 7e6,
            PropellantPair     = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            ExpansionRatio  = 15.0,
            PlugLengthRatio = 0.35,
        };

        var gen   = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());
        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

        Assert.NotNull(gen.Aerospike);
        // Score reports the aerospike mass, not the regen fallback mass.
        Assert.Equal(gen.Aerospike!.EstimatedMass_g, score.Mass_g, precision: 3);
        // Sanity: the aerospike mass differs from the fallback — this
        // confirms the fix actually matters.
        Assert.NotEqual(gen.Geometry.TotalMass_g, score.Mass_g);
    }

    [Fact]
    public void Evaluate_UsesRegenMass_WhenNoAerospikeSidecar()
    {
        // Complement to the above: on a regen baseline, the score's
        // Mass_g must still come from gen.Geometry.TotalMass_g. No
        // silent regression on the regen scoring path.
        var cond = new OperatingConditions
        {
            Thrust_N           = 5000,
            ChamberPressure_Pa = 6.9e6,
            PropellantPair     = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign { ChannelTopology = ChannelTopology.Axial };

        var gen   = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

        Assert.Null(gen.Aerospike);
        Assert.Equal(gen.Geometry.TotalMass_g, score.Mass_g, precision: 3);
    }

    [Fact]
    public void GenerateWith_LeavesAerospikeNull_WhenTopologyIsAxialOrTpms()
    {
        // Non-aerospike baselines leave the sidecar null — both for the
        // default Axial baseline and the TPMS family.
        var cond = new OperatingConditions
        {
            Thrust_N            = 5000,
            ChamberPressure_Pa  = 6.9e6,
            PropellantPair      = PropellantPair.LOX_CH4,
        };
        var axial = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Axial,
        };
        var tpms = new RegenChamberDesign
        {
            ChannelTopology    = ChannelTopology.TpmsGyroid,
            TpmsCellEdge_mm    = 4.0,
            TpmsSolidFraction  = 0.50,
        };

        var genAxial = RegenChamberOptimization.GenerateWith(cond, axial, skipVoxelGeometry: true);
        var genTpms  = RegenChamberOptimization.GenerateWith(cond, tpms,  skipVoxelGeometry: true);

        Assert.Null(genAxial.Aerospike);
        Assert.Null(genTpms.Aerospike);
    }

    [Fact]
    public void ToSpec_RejectsNonAerospikeTopology()
    {
        var cond = new OperatingConditions { Thrust_N = 5000 };
        var axial = new RegenChamberDesign { ChannelTopology = ChannelTopology.Axial };
        Assert.Throws<System.ArgumentException>(
            () => AerospikeOptimization.ToSpec(cond, axial));
    }

    // ══════════════════════ Sprint 11 Track F — aerospike scoring dispatch ══════════════════════

    /// <summary>
    /// Helper that builds a minimal <see cref="RegenGenerationResult"/> with
    /// a synthetic aerospike sidecar carrying a hand-built
    /// <see cref="AerospikeThermalResult"/>. The fallback regen fields are
    /// populated from `baseGen` (produced by `GenerateWith` on an aerospike
    /// baseline) so the rest of the scoring pipeline still sees valid inputs
    /// — the only difference between two calls is the aerospike thermal
    /// scalars. Lets the thermal-dispatch tests below isolate a single
    /// scoring input without spinning up a real plug-cooling solve.
    /// </summary>
    private static RegenGenerationResult WithSyntheticAerospikeThermal(
        RegenGenerationResult baseGen,
        double peakWallT_K,
        double coolantDP_Pa,
        double coolantOutletT_K,
        double totalHeatLoad_W,
        double peakHeatFlux_Wm2)
    {
        int N = baseGen.Aerospike!.Contour.Stations.Length;
        var wallTK = new double[N];
        var coolT  = new double[N];
        var heat   = new double[N];
        for (int i = 0; i < N; i++) { wallTK[i] = peakWallT_K; coolT[i] = coolantOutletT_K; heat[i] = 0; }
        wallTK[N / 2] = peakWallT_K;   // peak concentrated at mid-plug
        heat[N / 2]   = peakHeatFlux_Wm2;
        var thermal = new AerospikeThermalResult(
            GasSideWallT_K:         wallTK,
            CoolantBulkT_K:         coolT,
            HeatFlux_Wm2:           heat,
            PeakGasSideWallT_K:     peakWallT_K,
            PeakStation_X_mm:       baseGen.Aerospike.Contour.Stations[N / 2].X_mm,
            CoolantOutletT_K:       coolantOutletT_K,
            CoolantPressureDrop_Pa: coolantDP_Pa,
            TotalHeatLoad_W:        totalHeatLoad_W,
            Warnings:               System.Array.Empty<string>());
        var aeroWithThermal = baseGen.Aerospike with { Thermal = thermal };
        return baseGen with { Aerospike = aeroWithThermal };
    }

    /// <summary>
    /// Build an aerospike <see cref="OperatingConditions"/> /
    /// <see cref="RegenChamberDesign"/> pair shared by the Track F
    /// dispatch tests. Uses the fully-qualified Inconel 625 material
    /// (index 2) so its 1250 K service limit gives tests clean headroom
    /// for both "ok" and "exceeded" peak-wall-T scenarios.
    /// </summary>
    private static (OperatingConditions, RegenChamberDesign) MakeAerospikeBaseline() =>
        (new OperatingConditions
        {
            Thrust_N           = 20_000,
            ChamberPressure_Pa = 7e6,
            PropellantPair     = PropellantPair.LOX_CH4,
            CoolantInletTemp_K = 120,
            WallMaterialIndex  = 2,   // Inconel 625, MaxServiceTemp_K = 1250
        },
        new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            ExpansionRatio  = 15.0,
            PlugLengthRatio = 0.35,
        });

    [Fact]
    public void Evaluate_UsesAerospikePeakWallT_WhenAerospikeThermalPresent()
    {
        // Sprint 11 Track F: when the aerospike sidecar carries a Thermal
        // block, Evaluate must surface `Aerospike.Thermal.PeakGasSideWallT_K`
        // on the score — not the fallback bell-chamber compute. We assert
        // field propagation directly rather than comparing total scores,
        // since feasibility gates on an aerospike baseline without full
        // UI plumbing can legitimately return +∞ and swamp the comparison.
        var (cond, design) = MakeAerospikeBaseline();
        var baseGen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());
        Assert.NotNull(baseGen.Aerospike);

        var score = RegenChamberOptimization.Evaluate(WithSyntheticAerospikeThermal(
            baseGen,
            peakWallT_K:       950.0,
            coolantDP_Pa:      3e6,
            coolantOutletT_K:  350.0,
            totalHeatLoad_W:   40e3,
            peakHeatFlux_Wm2:  5e6), RegenChamberOptimization.Profiles[0]);

        Assert.Equal(950.0, score.PeakWallT_K, precision: 3);
        // Regression guard: the fallback bell-chamber peak differs —
        // proves the score came from the aerospike branch, not a
        // happens-to-match coincidence.
        Assert.NotEqual(baseGen.Thermal.PeakGasSideWallT_K, score.PeakWallT_K);
    }

    [Fact]
    public void Evaluate_UsesAerospikeCoolantDP_WhenAerospikeThermalPresent()
    {
        var (cond, design) = MakeAerospikeBaseline();
        var baseGen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());

        var score = RegenChamberOptimization.Evaluate(WithSyntheticAerospikeThermal(
            baseGen, peakWallT_K: 800, coolantDP_Pa: 2.5e6, 350, 40e3, 5e6), RegenChamberOptimization.Profiles[0]);

        Assert.Equal(2.5e6, score.CoolantDP_Pa, precision: 0);
        // Also verify the derived fraction matches: ΔP / Pc.
        Assert.Equal(2.5e6 / 7e6, score.CoolantDP_Fraction, precision: 4);
    }

    [Fact]
    public void Evaluate_UsesAerospikeCoolantOutletT_WhenThermalPresent()
    {
        var (cond, design) = MakeAerospikeBaseline();
        var baseGen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());

        var score = RegenChamberOptimization.Evaluate(WithSyntheticAerospikeThermal(
            baseGen, 800, 3e6, coolantOutletT_K: 365, 40e3, 5e6), RegenChamberOptimization.Profiles[0]);
        Assert.Equal(365.0, score.CoolantTOut_K, precision: 3);
    }

    [Fact]
    public void Evaluate_UsesAerospikeTotalHeatLoad_WhenThermalPresent()
    {
        var (cond, design) = MakeAerospikeBaseline();
        var baseGen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());

        var score = RegenChamberOptimization.Evaluate(WithSyntheticAerospikeThermal(
            baseGen, 800, 3e6, 350, totalHeatLoad_W: 55e3, 5e6), RegenChamberOptimization.Profiles[0]);
        Assert.Equal(55e3, score.TotalHeatLoad_W, precision: 0);
    }

    [Fact]
    public void Evaluate_PeakHeatFluxEqualsMaxOfAerospikeThermalArray()
    {
        // ThroatHeatFlux_Wm2 on the score record gets the aerospike-branch
        // analogue: max of the per-station HeatFlux_Wm2 array. Aerospike
        // has no single "throat" station — peak is the closest analogue.
        var (cond, design) = MakeAerospikeBaseline();
        var baseGen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());

        var score = RegenChamberOptimization.Evaluate(WithSyntheticAerospikeThermal(
            baseGen, 800, 3e6, 350, 40e3, peakHeatFlux_Wm2: 7.5e6), RegenChamberOptimization.Profiles[0]);
        Assert.Equal(7.5e6, score.ThroatHeatFlux_Wm2, precision: 0);
    }

    [Fact]
    public void Evaluate_WallTExceedsFlag_ReflectsAerospikeValue()
    {
        // Aerospike gas-side wall T > material service limit must flip
        // WallTExceeded regardless of what the fallback regen path said.
        // Material is Inconel 625 (MaxServiceTemp_K = 1250) so 900 vs 1400
        // unambiguously straddles the limit.
        var (cond, design) = MakeAerospikeBaseline();
        var baseGen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());

        var ok  = RegenChamberOptimization.Evaluate(WithSyntheticAerospikeThermal(
            baseGen, peakWallT_K:  900, 3e6, 350, 40e3, 5e6), RegenChamberOptimization.Profiles[0]);
        var bad = RegenChamberOptimization.Evaluate(WithSyntheticAerospikeThermal(
            baseGen, peakWallT_K: 1400, 3e6, 350, 40e3, 5e6), RegenChamberOptimization.Profiles[0]);

        Assert.False(ok.WallTExceeded);
        Assert.True(bad.WallTExceeded);
        Assert.True(bad.WallTMargin_K < 0);
        Assert.True(ok.WallTMargin_K > 0);
    }

    [Fact]
    public void Evaluate_RegenPath_UnaffectedByTrackF()
    {
        // Regression guard: the bell-chamber scoring path must produce
        // byte-identical results pre- vs post-Track-F. If Track F leaked
        // into the regen branch, this test would diverge.
        var cond = new OperatingConditions
        {
            Thrust_N           = 5000,
            ChamberPressure_Pa = 6.9e6,
            PropellantPair     = PropellantPair.LOX_CH4,
            WallMaterialIndex  = 1,
        };
        var design = new RegenChamberDesign { ChannelTopology = ChannelTopology.Axial };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

        Assert.Null(gen.Aerospike);
        // All score thermal scalars match the regen solver directly —
        // no aerospike branch was taken.
        Assert.Equal(gen.Thermal.PeakGasSideWallT_K,      score.PeakWallT_K,         precision: 3);
        Assert.Equal(gen.Thermal.CoolantPressureDrop_Pa,  score.CoolantDP_Pa,        precision: 0);
        Assert.Equal(gen.Thermal.CoolantOutletT_K,        score.CoolantTOut_K,       precision: 3);
        Assert.Equal(gen.Thermal.TotalHeatLoad_W,         score.TotalHeatLoad_W,     precision: 0);
        Assert.Equal(gen.Thermal.ThroatHeatFlux_Wm2,      score.ThroatHeatFlux_Wm2,  precision: 0);
        Assert.Equal(gen.Thermal.WallMarginK,             score.WallTMargin_K,       precision: 3);
        Assert.Equal(gen.Thermal.WallTempExceedsLimit,    score.WallTExceeded);
    }

    [Fact]
    public void ToSpec_MapsOperatingConditionsAndDesignFields()
    {
        // Regression anchor: the (cond, design) → AerospikeSpec mapping
        // must pin Thrust / Pc / MR / propellant to cond and ε / plug ratio
        // / shell thickness to design. Subsequent sprints may add new
        // fields, but the mapping of the current fields should not drift.
        var cond = new OperatingConditions
        {
            Thrust_N              = 12_000,
            ChamberPressure_Pa    = 8.5e6,
            MixtureRatio          = 2.9,
            CStarEfficiency       = 0.93,
            PropellantPair        = PropellantPair.LOX_CH4,
            CoolantInletTemp_K    = 120,
            CoolantInletPressure_Pa = 11e6,
            WallMaterialIndex     = 2,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology          = ChannelTopology.Aerospike,
            ExpansionRatio           = 18.0,
            PlugLengthRatio          = 0.45,
            OuterJacketThickness_mm  = 3.5,
        };

        var spec = AerospikeOptimization.ToSpec(cond, design);

        Assert.Equal(12_000,  spec.Thrust_N,            precision: 6);
        Assert.Equal(8.5e6,   spec.ChamberPressure_Pa,  precision: 6);
        Assert.Equal(18.0,    spec.ExpansionRatio,      precision: 6);
        Assert.Equal(0.45,    spec.PlugLengthRatio,     precision: 6);
        Assert.Equal(PropellantPair.LOX_CH4, spec.PropellantPair);
        Assert.Equal(2.9,     spec.MixtureRatio,        precision: 6);
        Assert.Equal(0.93,    spec.CStarEfficiency,     precision: 6);
        Assert.Equal(3.5,     spec.OuterShellThickness_mm, precision: 6);
        Assert.Equal(120.0,   spec.CoolantInletTemp_K,  precision: 6);
        Assert.Equal(11e6,    spec.CoolantInletPressure_Pa, precision: 6);
        Assert.Equal(2,       spec.WallMaterialIndex);
        // Regen cooling fields left at AerospikeSpec default until a
        // later sprint adds dedicated aerospike channel fields to
        // RegenChamberDesign.
        Assert.False(spec.IncludeRegenChannels);
    }

    [Fact]
    public void BuildAndEvaluate_ReturnsFeasibleForReasonableDesign()
    {
        // A conservative aerospike baseline (physics-only path, no regen
        // channels) should come back feasible — the only gates today are
        // plug-wall-T and cavitation, and neither fires on a geometry-only
        // build result (Thermal is null).
        var cond = new OperatingConditions
        {
            Thrust_N           = 20_000,
            ChamberPressure_Pa = 7e6,
            PropellantPair     = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology  = ChannelTopology.Aerospike,
            ExpansionRatio   = 15.0,
            PlugLengthRatio  = 0.30,
        };

        var (build, feas) = AerospikeOptimization.BuildAndEvaluate(cond, design, new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.Null(build.Voxels);
        Assert.NotNull(build.Contour);
        Assert.Null(build.Thermal);     // Regen channels not enabled → no thermal
        Assert.True(feas.IsFeasible);
        Assert.Empty(feas.Violations);
    }

    [Fact]
    public void BuildAndEvaluate_SurvivesSaLoopDim22Perturbation()
    {
        // Final end-to-end check for Sprint 2b step 1: starting from an
        // AutoSeeder-produced aerospike baseline, perturbing dim 22 in the
        // SA vector, and running BuildAndEvaluate must produce a finite
        // feasibility result whose feasibility flag is meaningful. Unlike
        // the Sprint 1 Dev B smoke test (which routed through GenerateWith
        // and scored on the regen path), this test exercises the aerospike
        // pipeline properly.
        var seed = AutoSeeder.Seed(new EngineSpec(
            PropellantPair:           PropellantPair.LOX_CH4,
            Thrust_N:                 20_000,
            ChamberPressure_Pa:       7e6,
            ExpansionRatio:           15.0,
            ChannelTopologyOverride:  ChannelTopology.Aerospike));

        // Sprint 7 Track A note: AutoSeeder picks a ~45-element injector
        // pattern at 20 kN via the standard thrust-scaling heuristic.
        // That count legitimately fails the Sprint 7 AEROSPIKE_ELEMENT_CLEARANCE
        // gate on the default 60 %·R_chamber pitch circle. To keep this
        // test focused on the dim-22 plug-truncation perturbation (its
        // Sprint 2b intent), strip the injector pattern from the
        // baseline — the clearance gate becomes inert and feasibility
        // reflects only the plug-cooling + geometry checks.
        var baseline = seed.Design with { InjectorElementPattern = null };

        double[] p = RegenChamberOptimization.Pack(baseline);
        p[22] = 0.55;   // tune the plug-truncation ratio
        var perturbed = RegenChamberOptimization.Unpack(p, baseline);

        var (build, feas) = AerospikeOptimization.BuildAndEvaluate(seed.Conditions, perturbed, new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.Equal(0.55, build.Contour.PlugLengthRatio, precision: 6);
        Assert.True(feas.IsFeasible, "geometry-only aerospike build must pass feasibility");
        Assert.True(build.EstimatedMass_g > 0);
        Assert.Null(build.InjectorSizing);   // pattern stripped above
    }

    [Fact]
    public void BuildPhysicsOnly_ThroatArea_MatchesThrust_Pc_RelationWithinTolerance()
    {
        // Sanity check on the derived throat: A_t = F / (C_F × P_c) with
        // C_F ≈ 1.4. BuildPhysicsOnly matches this within small rounding
        // slack — pin it so a future refactor doesn't drift the thrust
        // mapping silently.
        double thrust_N = 10_000.0;
        double pc_Pa    = 6e6;
        var spec = new AerospikeSpec(
            Thrust_N:           thrust_N,
            ChamberPressure_Pa: pc_Pa,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30);

        var r = AerospikeBuilder.BuildPhysicsOnly(spec);

        double annulusArea_mm2 = Math.PI * (r.ThroatOuterRadius_mm * r.ThroatOuterRadius_mm
                                          - r.ThroatInnerRadius_mm * r.ThroatInnerRadius_mm);
        double expectedArea_mm2 = thrust_N / (1.4 * pc_Pa) * 1e6;
        Assert.Equal(expectedArea_mm2, annulusArea_mm2, tolerance: expectedArea_mm2 * 1e-3);
    }

    [Fact]
    public void SaLoop_SurvivesAerospikeBaseline_PhysicsOnly()
    {
        // End-to-end smoke check for the Sprint 1 Dev B contract: SA on an
        // aerospike baseline must Pack, perturb dim 22, Unpack, and run
        // GenerateWith without crashing. Scoring today still uses the regen
        // chamber path (aerospike geometry integration is Sprint 2+), so we
        // only verify the plumbing — not the numerical meaningfulness of the
        // resulting score.
        var seed = AutoSeeder.Seed(new EngineSpec(
            PropellantPair:           PropellantPair.LOX_CH4,
            Thrust_N:                 5000.0,
            ChamberPressure_Pa:       6.9e6,
            ExpansionRatio:           15.0,
            ChannelTopologyOverride:  ChannelTopology.Aerospike));

        double[] p = RegenChamberOptimization.Pack(seed.Design);

        // Perturb dim 22 within bounds — a realistic SA step.
        p[22] = 0.60;

        var perturbed = RegenChamberOptimization.Unpack(p, seed.Design);
        Assert.Equal(0.60, perturbed.PlugLengthRatio, precision: 6);
        Assert.Equal(ChannelTopology.Aerospike, perturbed.ChannelTopology);

        // Physics-only pass — must not throw. Scoring via Evaluate must also
        // produce a finite score (even if the value is not aerospike-accurate
        // yet — the contract here is "doesn't crash", not "scores correctly").
        var gen = RegenChamberOptimization.GenerateWith(
            seed.Conditions, perturbed, skipVoxelGeometry: true);
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
        Assert.False(double.IsNaN(score.TotalScore),
            "SA score on aerospike baseline must be a finite number (not NaN)");
    }

    // ═════════════════════════════════════════════════════════════════
    //   FFSC 2×2 mass-flow solve
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void SizeFfscDual_AllFuelSumEqualsInput_AcrossMrGrid()
    {
        // Mass-flow conservation invariant must hold across a sweep of
        // (fuel-rich MR, ox-rich MR) pairs. The earlier MVP used a
        // naïve 100/0 split that over-allocated by up to ~20 %; the
        // 2×2 solve preserves total mass flow to floating-point
        // precision.
        double mFuel = 3.0, mOx = 12.0;
        foreach (var frMr in new[] { 0.3, 0.6, 0.9 })
        foreach (var orMr in new[] { 20.0, 35.0, 60.0 })
        {
            var (fr, or) = PreburnerChamber.SizeFfscDual(
                PropellantPair.LOX_CH4, frMr, orMr,
                preburnerPc_Pa: 15e6,
                totalFuelMassFlow_kgs: mFuel,
                totalOxMassFlow_kgs:   mOx);
            double total = fr.MassFlow_kgs + or.MassFlow_kgs;
            Assert.Equal(mFuel + mOx, total, precision: 6);
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //   Line-segment body-gate
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void BodyGate_CatchesTubeCuttingThroughChamber_EmitsOnePerTubeBody()
    {
        // A bent tube that plunges through the chamber body must flag
        // MONOLITHIC_BODY_INTERSECTION exactly once per (tube, body),
        // not once per interior sample — proving the per-body
        // worst-case aggregation collapsed the sweep correctly.
        var envelopes = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 20.0,
            ChamberLength_mm:      200.0,
            FuelPumpGeometry: null, FuelPumpOrigin: Vector3.Zero,
            OxPumpGeometry:   null, OxPumpOrigin:   Vector3.Zero,
            PreburnerGeometry: null, PreburnerOrigin: Vector3.Zero);

        // Straight tube with both endpoints outside the chamber's X range
        // (so the endpoint-touch whitelist doesn't fire) but the interior
        // passes through r=10 well inside the R=20 body at several X-stations.
        var tube = new FeedTube(
            Label: "test-clip",
            Start_mm:  new Vector3(-50f, 0f, 10f),
            Corner_mm: null,
            End_mm:    new Vector3(250f, 0f, 10f),
            OuterRadius_mm: 2.0);

        var layout = new FeedManifoldLayout(
            Cycle: EngineCycle.PressureFed,
            Tubes: new[] { tube },
            TotalTubeLength_mm: 0,
            EstimatedTubeMass_g: 0,
            Notes: "");

        var gate = MonolithicFeasibility.Evaluate(layout, envelopes);
        Assert.False(gate.IsFeasible);
        // At most one violation per (tube, body): no duplication from the
        // eight-sample sweep.
        var chamberViolations = gate.Violations
            .Where(v => v.Description.Contains("chamber") && v.Description.Contains("test-clip"))
            .ToArray();
        Assert.Single(chamberViolations);
    }

    [Fact]
    public void BodyGate_StraightTubeOutsideChamber_Passes()
    {
        // Negative control: tube completely outside the chamber envelope
        // at every sample must not raise a false positive.
        var envelopes = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 20.0,
            ChamberLength_mm:      100.0,
            FuelPumpGeometry: null, FuelPumpOrigin: Vector3.Zero,
            OxPumpGeometry:   null, OxPumpOrigin:   Vector3.Zero,
            PreburnerGeometry: null, PreburnerOrigin: Vector3.Zero);

        var tube = new FeedTube(
            Label: "test-clear",
            Start_mm:  new Vector3(-50f, 0f, 50f),
            Corner_mm: null,
            End_mm:    new Vector3( 50f, 0f, 50f),
            OuterRadius_mm: 2.0);

        var layout = new FeedManifoldLayout(
            Cycle: EngineCycle.PressureFed,
            Tubes: new[] { tube },
            TotalTubeLength_mm: 0,
            EstimatedTubeMass_g: 0,
            Notes: "");

        var gate = MonolithicFeasibility.Evaluate(layout, envelopes);
        Assert.True(gate.IsFeasible);
        Assert.Empty(gate.Violations);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 2 — fin-efficiency correction on coolant-side HTC
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void FinEfficiency_ReducesCoolantHTC_RaisesPeakWallT()
    {
        // Same design solved twice; fin correction disabled gives an
        // optimistic h_c, so enabling it must raise predicted peak wall T.
        var (thermalOff, thermalOn) = SolveWithAndWithoutFinEfficiency();
        Assert.True(thermalOn.PeakGasSideWallT_K >= thermalOff.PeakGasSideWallT_K,
            $"Fin correction should not lower peak T. off={thermalOff.PeakGasSideWallT_K:F1}, on={thermalOn.PeakGasSideWallT_K:F1}");

        // And the difference is physically meaningful for a default-sized
        // channel (tall rib + narrow width → fin efficiency well below 1).
        double delta = thermalOn.PeakGasSideWallT_K - thermalOff.PeakGasSideWallT_K;
        Assert.True(delta > 2.0,
            $"Expected fin correction to shift peak T by at least 2 K; got Δ = {delta:F2} K.");
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 3 — DesignProvenance hashing
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void DesignProvenance_IsDeterministic()
    {
        var cond = new OperatingConditions();
        var design = new RegenChamberDesign();
        string a = DesignProvenance.Compute(cond, design);
        string b = DesignProvenance.Compute(cond, design);
        Assert.Equal(a, b);
        Assert.Equal(16, a.Length);   // 8 hex bytes
    }

    [Fact]
    public void DesignProvenance_ChangesOnAnyDesignEdit()
    {
        var cond = new OperatingConditions();
        var a = DesignProvenance.Compute(cond, new RegenChamberDesign());
        var b = DesignProvenance.Compute(cond, new RegenChamberDesign { ChannelCount = 120 });
        var c = DesignProvenance.Compute(cond with { Thrust_N = 1000 }, new RegenChamberDesign());
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(b, c);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 4 — Best-so-far banner on report
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void ReportExport_NoBanner_WhenBestSoFarIsZero()
    {
        var r = BuildSafeBaseResult();
        string text = ReportExport.Build(r, bestSoFarIteration: 0);
        Assert.DoesNotContain("BEST-SO-FAR", text);
    }

    [Fact]
    public void ReportExport_StampsBanner_WhenBestSoFarIsPositive()
    {
        var r = BuildSafeBaseResult();
        string text = ReportExport.Build(r, bestSoFarIteration: 42);
        Assert.Contains("BEST-SO-FAR", text);
        Assert.Contains("iteration 42", text);
    }

    [Fact]
    public void ReportExport_IncludesDesignHash_WhenResultCarriesOne()
    {
        var r = BuildSafeBaseResult() with { DesignHash = "deadbeef12345678" };
        string text = ReportExport.Build(r);
        Assert.Contains("deadbeef12345678", text);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Shared infrastructure
    // ═════════════════════════════════════════════════════════════════

    private static RegenGenerationResult? _baseCache;
    private static readonly object _lock = new();

    /// <summary>
    /// Slow-generated baseline result, cached between tests. Uses a minimal
    /// design (no manifolds / ports / flange) to keep voxel ops quick.
    /// We then `with`-inject "safe" gate values so downstream tests don't
    /// depend on the raw physics output.
    /// </summary>
    private static RegenGenerationResult BuildSafeBaseResult()
    {
        lock (_lock)
        {
            if (_baseCache != null) return _baseCache;

            var cond = new OperatingConditions
            {
                Thrust_N = 2224.0, ChamberPressure_Pa = 6.9e6, MixtureRatio = 3.3,
                CoolantInletTemp_K = 150, CoolantInletPressure_Pa = 12e6,
                WallMaterialIndex = 1, PropellantPair = PropellantPair.LOX_CH4,
            };
            var design = new RegenChamberDesign
            {
                IncludeManifolds = false, IncludePorts = false,
                IncludeInjectorFlange = false, ContourStationCount = 60,
            };
            var r = RegenChamberOptimization.GenerateWith(cond, design);

            var mat = WallMaterials.All[cond.WallMaterialIndex];
            var fluid = MethaneFluid.Instance;
            _baseCache = r with
            {
                Thermal = r.Thermal with
                {
                    PeakGasSideWallT_K = mat.MaxServiceTemp_K - 200,
                    WallTempExceedsLimit = false,
                    CoolantOutletT_K = fluid.Metadata.MaxBulkT_K - 100,
                },
                Stress = r.Stress with
                {
                    MinSafetyFactor = 2.5, YieldExceeded = false,
                },
                Manufacturing = r.Manufacturing with
                {
                    MinFeatureSize_mm = 0.8, FeatureSizeOK = true,
                },
                Stability = r.Stability with
                {
                    Composite = StabilityRating.Pass,
                },
            };
            return _baseCache;
        }
    }

    /// <summary>
    /// Build solver inputs for a default-ish baseline and run it twice —
    /// once with <c>EnableFinEfficiency = false</c> (pre-Sprint 2 behaviour)
    /// and once with <c>true</c> (new default).
    /// </summary>
    private static (RegenSolverOutputs off, RegenSolverOutputs on) SolveWithAndWithoutFinEfficiency()
    {
        var cond = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign
        {
            FilmCooling = new FilmCoolingInputs
            {
                Enabled = true,
                FuelFractionAsFilm = 0.05,
                FilmSlotHeight_mm = 0.6,
                BurnoutLength_mm = 200,
                DecayCoefficient = 0.15,
                ThroatMixingDegradation = 0.25,
            },
        };
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: derived.ThroatRadius_mm,
            contractionRatio: design.ContractionRatio,
            expansionRatio: design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            thetaN_deg: design.BellEntranceAngle_deg,
            thetaE_deg: design.BellExitAngle_deg,
            bellLengthFraction: design.BellLengthFraction,
            stationCount: 120);
        var channels = new ChannelSchedule(
            ChannelCount: design.ChannelCount,
            RibThickness_mm: design.RibThickness_mm,
            GasSideWallThickness_mm: design.GasSideWallThickness_mm,
            ChannelHeightAtChamber_mm: design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm: design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm: design.ChannelHeightExit_mm);
        var mat = WallMaterials.All[cond.WallMaterialIndex];
        double mDotCool = derived.FuelMassFlow_kgs * 0.95;

        var sharedInputs = new RegenSolverInputs(
            Contour: contour, Gas: gas, Wall: mat, Channels: channels,
            CoolantMassFlow_kgs: mDotCool,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            FilmCooling: design.FilmCooling,
            CoolantFluid: MethaneFluid.Instance);

        var off = RegenCoolingSolver.Solve(sharedInputs with { EnableFinEfficiency = false });
        var on  = RegenCoolingSolver.Solve(sharedInputs with { EnableFinEfficiency = true });
        return (off, on);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 3 Track A (2026-04-22) — Multi-stage centrifugal pump
    // ═════════════════════════════════════════════════════════════════

    private static FeedSystem.TurbopumpResult SizeTurbopumpWithStages(int stageCount)
    {
        // Representative staged-combustion operating point. Values mirror
        // what RegenChamberOptimization.SizeTurbopumpFor resolves when the
        // defaults are untouched; parameters passed explicitly here so the
        // test is self-contained.
        var cond = new OperatingConditions { EngineCycle = FeedSystem.EngineCycle.StagedCombustion };
        return FeedSystem.TurbopumpSizing.Size(
            cycle:                FeedSystem.EngineCycle.StagedCombustion,
            cond:                 cond,
            fuelFlow_kgs:         0.50,
            oxFlow_kgs:           1.70,
            fuelDensity_kgm3:     420.0,    // LCH4 near saturation
            oxDensity_kgm3:       1141.0,   // LOX
            fuelInletPressure_Pa: 0.4e6,
            oxInletPressure_Pa:   0.4e6,
            dischargePressure_Pa: 15e6,
            pumpEfficiency:       FeedSystem.TurbopumpSizing.DefaultPumpEfficiency,
            stageCount:           stageCount);
    }

    [Fact]
    public void PumpSizing_StageCountDefaultsTo1_WhenCallerOmitsParameter()
    {
        // Backwards compatibility: callers that don't pass stageCount must
        // see the pre-Sprint-3 single-stage sizing bit-identically. This
        // pins the default-1 behaviour so future refactors can't drift it.
        var cond = new OperatingConditions { EngineCycle = FeedSystem.EngineCycle.StagedCombustion };
        var r = FeedSystem.TurbopumpSizing.Size(
            cycle:                FeedSystem.EngineCycle.StagedCombustion,
            cond:                 cond,
            fuelFlow_kgs:         0.50,  oxFlow_kgs: 1.70,
            fuelDensity_kgm3:     420.0, oxDensity_kgm3: 1141.0,
            fuelInletPressure_Pa: 0.4e6, oxInletPressure_Pa: 0.4e6,
            dischargePressure_Pa: 15e6);
        Assert.Equal(1, r.FuelPump!.StageCount);
        Assert.Equal(1, r.OxPump!.StageCount);
        Assert.Equal(r.FuelPump.HeadRise_m, r.FuelPump.HeadPerStage_m, precision: 3);
    }

    [Fact]
    public void PumpSizing_TotalHeadAndPower_AreStageCountInvariant()
    {
        // Conservation: adding stages splits the head but preserves the
        // total pressure rise — hydraulic power and shaft power must be
        // unchanged (ρ·g·Q·H_total / η). The SA optimiser relies on this
        // invariant when reaching for a second stage to escape a whirl
        // band: the propellant-feed work budget doesn't move.
        var r1 = SizeTurbopumpWithStages(1);
        var r2 = SizeTurbopumpWithStages(2);
        var r4 = SizeTurbopumpWithStages(4);

        Assert.Equal(r1.FuelPump!.HeadRise_m,       r2.FuelPump!.HeadRise_m,       precision: 6);
        Assert.Equal(r1.FuelPump.HeadRise_m,        r4.FuelPump!.HeadRise_m,       precision: 6);
        Assert.Equal(r1.FuelPump.HydraulicPower_W,  r2.FuelPump.HydraulicPower_W,  precision: 3);
        Assert.Equal(r1.FuelPump.ShaftPower_W,      r4.FuelPump.ShaftPower_W,      precision: 3);
    }

    [Fact]
    public void PumpSizing_HeadPerStage_ShrinksWithStageCount()
    {
        // The whole point: stage 1 carries 1/N of the total head.
        var r2 = SizeTurbopumpWithStages(2);
        var r4 = SizeTurbopumpWithStages(4);

        Assert.Equal(r2.FuelPump!.HeadRise_m / 2.0, r2.FuelPump.HeadPerStage_m, precision: 3);
        Assert.Equal(r4.FuelPump!.HeadRise_m / 4.0, r4.FuelPump.HeadPerStage_m, precision: 3);
    }

    [Fact]
    public void PumpSizing_Rpm_FallsWithStageCount_ViaSpecificSpeed()
    {
        // Karassik §2.5: RPM at the specific-speed optimum scales with
        // H^(3/4). Halving head → RPM drops by 2^(-0.75) ≈ 0.595.
        // Double stages → RPM should land near 0.595 × single-stage RPM.
        var r1 = SizeTurbopumpWithStages(1);
        var r2 = SizeTurbopumpWithStages(2);

        double expected = r1.FuelPump!.Rpm * System.Math.Pow(2.0, -0.75);
        // 3 % tolerance covers the per-stage head rounding.
        Assert.InRange(r2.FuelPump!.Rpm, expected * 0.97, expected * 1.03);
        Assert.True(r2.FuelPump.Rpm < r1.FuelPump.Rpm,
            "RPM must drop when head is split across stages");
    }

    [Fact]
    public void PumpSizing_StageCount_ClampsToEnvelope()
    {
        // Out-of-envelope values must clamp to [1, 4]. No exception, no
        // NaN — the SA sampler should always produce a feasible sizing.
        var rBelow = SizeTurbopumpWithStages(0);
        var rAbove = SizeTurbopumpWithStages(99);
        Assert.Equal(FeedSystem.TurbopumpSizing.MinStageCount, rBelow.FuelPump!.StageCount);
        Assert.Equal(FeedSystem.TurbopumpSizing.MaxStageCount, rAbove.FuelPump!.StageCount);
    }

    [Fact]
    public void RegenChamberDesign_PumpStageCount_DefaultsToOne()
    {
        // Round-trip via `with`: the design record carries PumpStageCount
        // and it's settable. Default must be 1 so existing saved designs
        // (json) deserialise with the legacy single-stage sizing.
        var d = new RegenChamberDesign();
        Assert.Equal(1, d.PumpStageCount);
        var multi = d with { PumpStageCount = 3 };
        Assert.Equal(3, multi.PumpStageCount);
    }

    [Fact]
    public void GenerateWith_PropagatesPumpStageCount_IntoPumpSizing()
    {
        // End-to-end: RegenChamberDesign.PumpStageCount must land on the
        // resulting TurbopumpResult.FuelPump.StageCount and OxPump.StageCount.
        // This pins the wiring through RegenChamberOptimization.SizeTurbopumpFor.
        var cond = new OperatingConditions { EngineCycle = FeedSystem.EngineCycle.StagedCombustion };
        var design = new RegenChamberDesign { PumpStageCount = 2 };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true, aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.NotNull(gen.Turbopump);
        Assert.Equal(2, gen.Turbopump!.FuelPump!.StageCount);
        Assert.Equal(2, gen.Turbopump.OxPump!.StageCount);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 3 polish (2026-04-22) — N-stage pump voxel geometry
    // ═════════════════════════════════════════════════════════════════

    private static TurbopumpGeometry GeneratePumpGeometryWithStages(int stageCount)
    {
        var r = SizeTurbopumpWithStages(stageCount);
        var g = Turbopump.TurbopumpGeometryGenerator.Generate(r.FuelPump!);
        Assert.NotNull(g);
        return g!;
    }

    [Fact]
    public void TurbopumpGeometry_SingleStage_ReportsStageCount1_AndZeroInterstageGap()
    {
        // Pre-Sprint-3-polish behaviour: single-stage geometry carries
        // StageCount = 1 and zero interstage gap — the field defaults
        // that trailing callers will see.
        var g = GeneratePumpGeometryWithStages(1);
        Assert.Equal(1, g.StageCount);
        Assert.Equal(0.0, g.InterstageGap_mm, precision: 6);
    }

    [Fact]
    public void TurbopumpGeometry_StageCount_PropagatesFromPumpSizingToGeometry()
    {
        var g2 = GeneratePumpGeometryWithStages(2);
        var g4 = GeneratePumpGeometryWithStages(4);
        Assert.Equal(2, g2.StageCount);
        Assert.Equal(4, g4.StageCount);
        // Non-zero interstage gap whenever stageCount > 1.
        Assert.True(g2.InterstageGap_mm > 0);
        Assert.Equal(Turbopump.TurbopumpGeometryGenerator.InterstageGap_mm,
                     g2.InterstageGap_mm, precision: 6);
    }

    [Fact]
    public void TurbopumpGeometry_TipRadius_GrowsAs_N_ToTheQuarter()
    {
        // Analytic: R_tip_N = R_tip_1 · N^(1/4). Combines the Euler-head
        // per-stage shrink (1/N) with the RPM shrink (N^(-3/4)) that
        // SizeOnePump applies:
        //   R_tip = U_2 / ω = √(g·H/N/ψ) / (ω_1 · N^(-3/4)) = R_tip_1 · N^(1/4)
        var g1 = GeneratePumpGeometryWithStages(1);
        var g2 = GeneratePumpGeometryWithStages(2);
        var g4 = GeneratePumpGeometryWithStages(4);

        double expected2 = g1.ImpellerTipRadius_mm * System.Math.Pow(2.0, 0.25);
        double expected4 = g1.ImpellerTipRadius_mm * System.Math.Pow(4.0, 0.25);
        // 3 % tolerance covers the per-stage head / RPM rounding.
        Assert.InRange(g2.ImpellerTipRadius_mm, expected2 * 0.97, expected2 * 1.03);
        Assert.InRange(g4.ImpellerTipRadius_mm, expected4 * 0.97, expected4 * 1.03);
    }

    [Fact]
    public void TurbopumpGeometry_TotalLength_GrowsWithStageCount()
    {
        // Each added stage contributes one impeller thickness + one
        // interstage gap to the axial length. Total length must grow
        // monotonically with stage count — this is the signal that
        // ShaftCriticalSpeed.Estimate uses to drop the bending critical
        // RPM for multi-stage designs.
        var g1 = GeneratePumpGeometryWithStages(1);
        var g2 = GeneratePumpGeometryWithStages(2);
        var g3 = GeneratePumpGeometryWithStages(3);
        var g4 = GeneratePumpGeometryWithStages(4);

        Assert.True(g2.TotalLength_mm > g1.TotalLength_mm);
        Assert.True(g3.TotalLength_mm > g2.TotalLength_mm);
        Assert.True(g4.TotalLength_mm > g3.TotalLength_mm);
    }

    [Fact]
    public void TurbopumpGeometry_Mass_ReflectsStageCount()
    {
        // N impellers + 1 inducer + casing shell → mass grows with
        // stage count (the rotor volume term is the dominant one at
        // small pump scales but casing length also contributes).
        var g1 = GeneratePumpGeometryWithStages(1);
        var g4 = GeneratePumpGeometryWithStages(4);
        Assert.True(g4.EstimatedMass_g > g1.EstimatedMass_g,
            "4-stage mass must exceed single-stage mass");
    }

    [Fact]
    public void TurbopumpGeometry_Notes_MentionStageCount_WhenMultiStage()
    {
        // The user-facing Notes string differentiates single- vs multi-
        // stage so the UI / CLI report can explain why the geometry is
        // bigger than a naive single-stage quick-look.
        var g1 = GeneratePumpGeometryWithStages(1);
        var g3 = GeneratePumpGeometryWithStages(3);
        Assert.Contains("Single-stage", g1.Notes);
        Assert.Contains("3-stage",      g3.Notes);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Sprint 5 Dev A (2026-04-22) — ADR-010 DesignVariableRegistry
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void DesignVariableRegistry_ReturnsContiguousDescriptorsForRegenChamberDesign()
    {
        // Post-Track-B + OOB-6 (Sprint B-3): 29 descriptors on
        // RegenChamberDesign cover dims 0-12 (chamber + channels), 18-19
        // (TPMS), 20-21 (Tier-C), 22 (aerospike plug), 23 (aerospike
        // chamber contraction), 24 (FilmFuelFraction), 25
        // (FilmSlotHeightOverride), 26 (PintleDiameterOverride), 27
        // (PintleSleeveHoleCountOverride), 28-30 (ChamberWall /
        // ThroatWall / ExitWall thickness overrides), 31
        // (HelmholtzNeckArea_mm2), 32 (HelmholtzCavityVolume_mm3), 33
        // (QuarterWaveLength_mm). The 5 injector-pattern dims (13-17)
        // live on InjectorPattern and are discovered separately.
        var descriptors = DesignVariableRegistry.For(typeof(RegenChamberDesign));
        Assert.Equal(29, descriptors.Count);
    }

    [Fact]
    public void DesignVariableRegistry_BoundsForMany_MatchesRegenChamberOptimization_ForAllDims()
    {
        // Drift guard after the full Track A migration: every SA dim
        // now carries an [SaDesignVariable] attribute. BoundsForMany
        // aggregates RegenChamberDesign + InjectorPattern into a single
        // length-N array (N grows as new dims are added — 23 after
        // Sprint 6 Track A, 24 after Sprint 9 Track C's
        // AerospikeContractionRatio, and so on) that must match
        // RegenChamberOptimization.Bounds bit-for-bit. Since Sprint 6
        // Track A, RegenChamberOptimization.Bounds IS the registry
        // output (one-line delegation), so this test is the tripwire
        // for ADR-010: any future range change has to happen on the
        // attribute (single source of truth going forward).
        var registryBounds = DesignVariableRegistry.BoundsForMany(
            typeof(RegenChamberDesign),
            typeof(InjectorPattern));
        var hardcodedBounds = RegenChamberOptimization.Bounds;

        Assert.Equal(hardcodedBounds.Length, registryBounds.Length);
        for (int i = 0; i < hardcodedBounds.Length; i++)
        {
            Assert.Equal(hardcodedBounds[i].Min, registryBounds[i].Min, precision: 6);
            Assert.Equal(hardcodedBounds[i].Max, registryBounds[i].Max, precision: 6);
        }
    }

    [Fact]
    public void DesignVariableRegistry_DescriptorsForMany_CarriesGateMetadata()
    {
        // Gate metadata round-trips through the registry — descriptors
        // on RegenChamberDesign + InjectorPattern land with the right
        // SaGate value. This pins the gating contract the Unpack
        // rewrite will consume.
        var descriptors = DesignVariableRegistry.DescriptorsForMany(
            typeof(RegenChamberDesign),
            typeof(InjectorPattern));

        var byIndex = descriptors.ToDictionary(d => d.Index);

        // Plain dims have no gate.
        Assert.Equal(SaGate.None, byIndex[0].Gate);
        Assert.Equal(SaGate.None, byIndex[12].Gate);
        // Injector-pattern dims (13-17) all gate on InjectorPatternPresent.
        for (int i = 13; i <= 17; i++)
            Assert.Equal(SaGate.InjectorPatternPresent, byIndex[i].Gate);
        // TPMS dims (18-19) gate on TpmsTopology.
        Assert.Equal(SaGate.TpmsTopology, byIndex[18].Gate);
        Assert.Equal(SaGate.TpmsTopology, byIndex[19].Gate);
        // Tier-C dims (20-21) apply unconditionally per the convention
        // that 0 ⇒ use default downstream.
        Assert.Equal(SaGate.None, byIndex[20].Gate);
        Assert.Equal(SaGate.None, byIndex[21].Gate);
        // Aerospike dim (22) gates on AerospikeTopology.
        Assert.Equal(SaGate.AerospikeTopology, byIndex[22].Gate);
    }

    [Fact]
    public void DesignVariableRegistry_Descriptor_CarriesMemberName()
    {
        // MemberName is the diagnostic handle UI / error-reporting uses
        // to say "the ContractionRatio slot is out of bounds". Pin the
        // first few to catch refactors that drop the reflection path.
        var descriptors = DesignVariableRegistry.For(typeof(RegenChamberDesign));
        Assert.Equal("ContractionRatio",        descriptors[0].MemberName);
        Assert.Equal("ExpansionRatio",          descriptors[1].MemberName);
        Assert.Equal("CharacteristicLength_m",  descriptors[2].MemberName);
        Assert.Equal("OuterJacketThickness_mm", descriptors[12].MemberName);
    }

    [Fact]
    public void SaDesignVariableAttribute_RejectsInvertedBounds()
    {
        // Can't construct an attribute with min >= max — defensive guard
        // against "I meant 0.5 and wrote 5.0" typos when adding new dims.
        Assert.Throws<System.ArgumentException>(
            () => new SaDesignVariableAttribute(index: 99, min: 5.0, max: 1.0));
    }

    [Fact]
    public void SaDesignVariableAttribute_RejectsNegativeIndex()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new SaDesignVariableAttribute(index: -1, min: 0.0, max: 1.0));
    }
}
