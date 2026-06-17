// Phase1To7Tests.cs — Contract tests for the 2026-04-20 implementation
// round (seven features landed in a single sprint):
//
//   Phase 1 — entrance + exit ΔP loss coefficients
//   Phase 2 — injector face temperature model + 7th feasibility gate
//   Phase 3 — per-element voxel geometry in step 8.5 (smoke only)
//   Phase 4 — helical cooling channels (friction + effective length)
//   Phase 5 — coolant-to-fuel-plenum crossover (thermal coupling)
//   Phase 6 — Pareto-front tracking during SA
//   Phase 7 — 3MF export ZIP writer
//
// Only the non-voxel physics, the voxel-adequacy-gate-independent geometry
// switches, and the serialisers are exercised directly — voxel-heavy paths
// require PicoGK init and live in BuildSafeBaseResult.

using System.IO.Compression;
using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.IO;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.Optimization;
using Voxelforge.Structure;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class Phase1To7Tests
{
    // ─────────────────────────────────────────────────────────────────
    //  Phase 1 — entrance / exit ΔP
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EntranceExit_LossCoefficients_AddToTotalDP()
    {
        var (off, on) = SolveWithAndWithoutEntranceExit();
        Assert.True(on.CoolantPressureDrop_Pa > off.CoolantPressureDrop_Pa,
            $"Enabling entrance+exit losses must add to ΔP. off={off.CoolantPressureDrop_Pa:E2} on={on.CoolantPressureDrop_Pa:E2}");
        // Loss budget: 0.5·ρ·v² + 1.0·ρ·v² = 1.5 velocity heads.
        // Typical LRE jacket: ρ ≈ 100 kg/m³ × v ≈ 30 m/s → ≈ 135 kPa on each head.
        // Be generous: assert non-zero but under 2 MPa.
        double extra = on.CoolantPressureDrop_Pa - off.CoolantPressureDrop_Pa;
        Assert.InRange(extra, 1e3, 2e6);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Phase 2 — injector face thermal + 7th gate
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void InjectorFace_Null_WhenNoPatternSet()
    {
        var r = BuildBaseResult(withPattern: false);
        Assert.Null(r.InjectorFace);
    }

    [Fact]
    public void InjectorFace_Populated_WhenPatternActive()
    {
        var r = BuildBaseResult(withPattern: true);
        Assert.NotNull(r.InjectorFace);
        Assert.True(r.InjectorFace!.TFace_K > 200,
            $"Face T should be a physically sensible number; got {r.InjectorFace.TFace_K}");
        Assert.True(r.InjectorFace.TFace_K < r.InjectorFace.TAwCore_K,
            "Back-cooling means T_face ≤ T_aw_core.");
    }

    [Fact]
    public void FeasibilityGate_FiresINJECTOR_FACE_T_Exceeded_WhenFaceTIsAbsurd()
    {
        var r = BuildSafeBaseResult() with
        {
            // Clamp the face T artificially above CuCrZr 800 K limit.
            InjectorFace = new InjectorFaceResult(
                TFace_K: 1500,
                TAwCore_K: 2800,
                TPropAvg_K: 150,
                HeatFlux_Wm2: 5e6,
                HGasSide_Wm2K: 8000,
                HPropSide_Wm2K: 500,
                FaceArea_cm2: 10.0,
                BoreAreaFraction: 0.08,
                Method: "test",
                Warnings: Array.Empty<string>()),
        };
        var gate = FeasibilityGate.Evaluate(r);
        Assert.Contains(gate.Violations, v => v.ConstraintId == "INJECTOR_FACE_T_EXCEEDED");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Phase 4 — helical channels
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Helical_Channels_RaiseCoolantDP_VsAxial()
    {
        var (axial, helical) = SolveAxialVsHelical();
        Assert.True(helical.CoolantPressureDrop_Pa > axial.CoolantPressureDrop_Pa,
            $"Helical should raise ΔP vs axial. axial={axial.CoolantPressureDrop_Pa:E2} helical={helical.CoolantPressureDrop_Pa:E2}");
    }

    [Fact]
    public void Helical_Channels_RaiseCoolantOutletT_VsAxial()
    {
        // Longer effective wetted length → more heat uptake → higher outlet T.
        var (axial, helical) = SolveAxialVsHelical();
        Assert.True(helical.CoolantOutletT_K >= axial.CoolantOutletT_K,
            $"Helical should not drop outlet T. axial={axial.CoolantOutletT_K:F1} helical={helical.CoolantOutletT_K:F1}");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Phase 5 — coolant crossover (thermal coupling only)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Crossover_On_Raises_PredictedFaceT_VsOff()
    {
        var rOff = BuildBaseResult(withPattern: true, crossover: false);
        var rOn  = BuildBaseResult(withPattern: true, crossover: true);
        Assert.NotNull(rOff.InjectorFace);
        Assert.NotNull(rOn.InjectorFace);
        Assert.True(rOn.InjectorFace!.TFace_K > rOff.InjectorFace!.TFace_K,
            $"Crossover feeds hot fuel to the injector — face T should rise. off={rOff.InjectorFace.TFace_K:F0} on={rOn.InjectorFace.TFace_K:F0}");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Phase 6 — Pareto front
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Pareto_Empty_Initially()
    {
        var pf = new ParetoFront();
        Assert.Equal(0, pf.Count);
    }

    [Fact]
    public void Pareto_DropsDominatedOffers()
    {
        var pf = new ParetoFront();
        bool added1 = pf.Offer(new ParetoPoint(1000, 1e6, 100, Array.Empty<double>(), 1));
        // Dominated on all three axes.
        bool added2 = pf.Offer(new ParetoPoint(1100, 2e6, 120, Array.Empty<double>(), 2));
        Assert.True(added1);
        Assert.False(added2);
        Assert.Equal(1, pf.Count);
    }

    [Fact]
    public void Pareto_KeepsAndEvictsCorrectly()
    {
        var pf = new ParetoFront();
        pf.Offer(new ParetoPoint(1000, 1e6, 100, Array.Empty<double>(), 1));
        pf.Offer(new ParetoPoint(900,  1e6, 100, Array.Empty<double>(), 2));   // dominates prior
        // The point at (900, 1e6, 100) strictly dominates the original.
        Assert.Equal(1, pf.Count);
        Assert.Equal(900, pf.Points[0].PeakWallT_K);
    }

    [Fact]
    public void Pareto_RejectsNonFiniteValues()
    {
        var pf = new ParetoFront();
        Assert.False(pf.Offer(new ParetoPoint(double.PositiveInfinity, 1e6, 100, Array.Empty<double>(), 0)));
        Assert.False(pf.Offer(new ParetoPoint(1000, double.NaN, 100, Array.Empty<double>(), 0)));
        Assert.Equal(0, pf.Count);
    }

    [Fact]
    public void Pareto_MultiplePointsOnFront_AreKept()
    {
        var pf = new ParetoFront();
        // Three mutually-non-dominated points — each is best on one axis.
        pf.Offer(new ParetoPoint(800, 2e6, 200, Array.Empty<double>(), 1));   // low T
        pf.Offer(new ParetoPoint(1200, 0.5e6, 200, Array.Empty<double>(), 2)); // low ΔP
        pf.Offer(new ParetoPoint(1200, 2e6, 50, Array.Empty<double>(), 3));    // low mass
        Assert.Equal(3, pf.Count);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Phase 7 — 3MF export
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ThreeMF_RoundTrip_WritesValidZipWithModel()
    {
        var r = BuildSafeBaseResult();

        using var tempStl = TestTempFile.WithUniqueName("phase7_probe", "stl");
        using var temp3Mf = TestTempFile.WithUniqueName("phase7_probe", "3mf");
        // Write a minimal valid binary STL: 80 header + 1 triangle.
        using (var bw = new BinaryWriter(File.Create(tempStl.Path)))
        {
            bw.Write(new byte[80]);              // header
            bw.Write((uint)1);                   // triangle count
            for (int i = 0; i < 3; i++)          // normal + 3 vertices (12 floats)
                bw.Write(0f);
            // three distinct vertices so dedup keeps all three
            bw.Write(0f);  bw.Write(0f);  bw.Write(0f);
            bw.Write(10f); bw.Write(0f);  bw.Write(0f);
            bw.Write(0f);  bw.Write(10f); bw.Write(0f);
            bw.Write((ushort)0);                 // attribute
        }

        ThreeMFExport.SaveFromStl(tempStl.Path, temp3Mf.Path, r);

        Assert.True(File.Exists(temp3Mf.Path));
        using var zip = ZipFile.OpenRead(temp3Mf.Path);
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("_rels/.rels"));
        var model = zip.GetEntry("3D/3dmodel.model");
        Assert.NotNull(model);
        using var reader = new StreamReader(model!.Open());
        string xml = reader.ReadToEnd();
        Assert.Contains("<mesh>", xml);
        Assert.Contains("<vertex", xml);
        Assert.Contains("<triangle ", xml);
        Assert.Contains("Material", xml);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Shared helpers
    // ═════════════════════════════════════════════════════════════════

    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N = 2224.0, ChamberPressure_Pa = 6.9e6, MixtureRatio = 3.3,
        CoolantInletTemp_K = 150, CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex = 1, PropellantPair = PropellantPair.LOX_CH4,
    };

    private static RegenChamberDesign MinimalDesign() => new()
    {
        IncludeManifolds = false, IncludePorts = false,
        IncludeInjectorFlange = false, ContourStationCount = 60,
    };

    private static readonly object _baseLock = new();
    private static RegenGenerationResult? _baseNoPat;
    private static RegenGenerationResult? _basePat;
    private static RegenGenerationResult? _basePatCross;

    private static RegenGenerationResult BuildBaseResult(
        bool withPattern, bool crossover = false)
    {
        lock (_baseLock)
        {
            if (!withPattern)
                return _baseNoPat ??= RegenChamberOptimization.GenerateWith(
                    DefaultConditions(), MinimalDesign());

            if (crossover)
            {
                return _basePatCross ??= RegenChamberOptimization.GenerateWith(
                    DefaultConditions(),
                    MinimalDesign() with
                    {
                        InjectorElementPattern = InjectorPattern.DefaultCoax(18),
                        IncludeInjectorFlange = true,
                        IncludeCoolantCrossover = true,
                    });
            }

            return _basePat ??= RegenChamberOptimization.GenerateWith(
                DefaultConditions(),
                MinimalDesign() with
                {
                    InjectorElementPattern = InjectorPattern.DefaultCoax(18),
                    IncludeInjectorFlange = true,
                });
        }
    }

    /// <summary>Inject safe gate-relevant values on top of a real GenerateWith.</summary>
    private static RegenGenerationResult BuildSafeBaseResult()
    {
        var r   = BuildBaseResult(withPattern: false);
        var mat = WallMaterials.All[DefaultConditions().WallMaterialIndex];
        var ch4 = MethaneFluid.Instance;
        return r with
        {
            Thermal = r.Thermal with
            {
                PeakGasSideWallT_K = mat.MaxServiceTemp_K - 200,
                WallTempExceedsLimit = false,
                CoolantOutletT_K = ch4.Metadata.MaxBulkT_K - 100,
            },
            Stress = r.Stress with { MinSafetyFactor = 2.5, YieldExceeded = false },
            Manufacturing = r.Manufacturing with
            { MinFeatureSize_mm = 0.8, FeatureSizeOK = true },
            Stability = r.Stability with { Composite = StabilityRating.Pass },
        };
    }

    private static (RegenSolverOutputs off, RegenSolverOutputs on)
        SolveWithAndWithoutEntranceExit() => SolvePair(
            offMutator: i => i with { CoolantEntranceLossK = 0.0, CoolantExitLossK = 0.0 },
            onMutator:  i => i with { CoolantEntranceLossK = 0.5, CoolantExitLossK = 1.0 });

    private static (RegenSolverOutputs axial, RegenSolverOutputs helical)
        SolveAxialVsHelical() => SolvePair(
            offMutator: i => i with { HelixPitchAngle_deg = 0.0 },
            onMutator:  i => i with { HelixPitchAngle_deg = 20.0 });

    private static (RegenSolverOutputs a, RegenSolverOutputs b) SolvePair(
        Func<RegenSolverInputs, RegenSolverInputs> offMutator,
        Func<RegenSolverInputs, RegenSolverInputs> onMutator)
    {
        var cond = DefaultConditions() with { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign
        {
            FilmCooling = new FilmCoolingInputs
            {
                Enabled = true, FuelFractionAsFilm = 0.05,
                FilmSlotHeight_mm = 0.6, BurnoutLength_mm = 200,
                DecayCoefficient = 0.15, ThroatMixingDegradation = 0.25,
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

        var baseInputs = new RegenSolverInputs(
            Contour: contour, Gas: gas, Wall: mat, Channels: channels,
            CoolantMassFlow_kgs: mDotCool,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            FilmCooling: design.FilmCooling,
            CoolantFluid: MethaneFluid.Instance);

        var off = RegenCoolingSolver.Solve(offMutator(baseInputs));
        var on  = RegenCoolingSolver.Solve(onMutator(baseInputs));
        return (off, on);
    }
}
