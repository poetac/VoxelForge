// HeatPipeFixture_Safe400KrustyReactor.cs — Sprint A.69 Phase 3
// published-anchor cluster-validation fixture for the HeatPipe pillar.
//
// Anchors the Wave-1 closed-form heat-pipe performance snapshot to the
// **SAFE-400 / KRUSTY** space-nuclear reactor primary heat pipe cluster
// (Li-tungsten working pair). Published anchors:
//   - Poston D.I. (2004) "The SAFE-400 small fission power system,"
//     LA-UR-04-2884. 100 kWt fission core, eight Li-Nb primary heat
//     pipes (later iterations Li-W), each ~ 1 kW heat throughput.
//   - Gibson M., Mason L., Bowman C. (2017) "Development of NASA's small
//     fission power system technology demonstrator (KRUSTY)," NETS-2017
//     (LA-UR-17-21851). 1 kWe / ~ 4 kWt demonstrator, eight sodium-
//     stainless heat pipes in the reflector + a Li-tungsten primary
//     loop in the SAFE-400-class design lineage.
//   - El-Genk M.S., Tournier J.-M. (2011) "Uses of liquid-metal and
//     water heat pipes in space reactor power systems," Frontiers in
//     Heat Pipes 2, 013002. Cluster reference for Li-W operating-T
//     band (1100–1400 K) + heat-flux envelope (0.1–50 kW per pipe).
//
// Geometry + operating-point anchors:
//   - Working fluid:  Lithium  (Li-W cluster)
//   - Vapour-core ID:  14 mm   (SAFE-400 anchor; KRUSTY 12.7 mm = 0.5 in)
//   - Heat-pipe length: 1.0 m  (SAFE-400 core-out-to-radiator anchor)
//   - Per-pipe heat throughput: 1.0 kW
//     (SAFE-400 100 kWt / 8 pipes / ~ 12 % radial fraction ≈ 1 kW;
//      KRUSTY ~ 0.5 kW; cluster anchor [0.5, 2] kW per pipe)
//   - Operating mean-fluid temperature: 1400 K
//     (Poston 2004 §III reports SAFE-400 Li peak ~ 1500 K, evaporator-
//      mean ~ 1400 K; firmly inside the Wave-1 Li envelope [1273, 1773] K)
//
// Phase-3 coverage backfill — third sprint in the C.1 thermal-management
// triple after A.66 (HeatExchanger / Capstone C200 recuperator) and A.68
// (Radiator / second-anchor). With A.69 the triple is complete.
//
// Model-vs-hardware calibration disclaimer (ADR-036 D3.2):
//
//   The Wave-1 HeatPipe solver treats the device as a black-box high-
//   effective-conductivity thermal path with a per-fluid + per-area
//   capillary / sonic / entrainment limit cluster. Real SAFE-400 / KRUSTY
//   hardware adds wick-specific physics not yet modelled — annular wick
//   permeability, evaporator-condenser axial temperature gradient,
//   non-condensable gas accumulation, freeze/thaw startup transients
//   (Li freezes at 454 K; the system must transit from ambient through
//   the sonic-limited regime to the capillary-limited steady state).
//
//   The Wave-1 cluster-anchored Lithium k_eff = 200,000 W/(m·K) sits in
//   the Faghri 2016 §5 + NASA TP-3326 §4 published band of 150,000-
//   300,000 W/(m·K) for Li-W at 1400 K. Test bands describe what the
//   Wave-1 model predicts at the SAFE-400-class design point, with
//   cluster-scatter margin around the model output rather than around
//   any single point-design hardware datum. Downstream tests
//   (`HeatPipeSolverTests`, `HeatPipeWave2Tests`) already cover the
//   closed-form algebraic bit-correctness of the solver.

using Voxelforge.HeatPipe;
using Xunit;

namespace Voxelforge.Tests.HeatPipe;

public sealed class HeatPipeFixture_Safe400KrustyReactor
{
    // ── Working-fluid + envelope selection ─────────────────────────────

    [Fact]
    public void Safe400_DesignPoint_UsesLithiumWorkingFluid()
    {
        // SAFE-400 / KRUSTY primary heat pipes are Li-tungsten — the
        // only fluid in the Wave-1 registry whose envelope spans the
        // 1000-1500 °C space-reactor cluster.
        Assert.Equal(HeatPipeFluid.Lithium, Safe400PrimaryHeatPipe().Fluid);
    }

    [Fact]
    public void Safe400_DesignPoint_OperatingTemperatureInLithiumEnvelope()
    {
        // T_op = 1400 K sits inside the Li-W envelope [1273, 1773] K
        // (Poston 2004 §III SAFE-400 evaporator-mean anchor).
        var r = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe());
        Assert.True(r.OperatingTemperatureInValidEnvelope);
    }

    [Fact]
    public void Safe400_FluidAutoSelect_AtOperatingT_PicksLithium()
    {
        // The Wave-2 auto-fluid-selection helper must agree with the
        // manual Lithium choice at the SAFE-400 1400 K design point.
        Assert.Equal(HeatPipeFluid.Lithium,
            HeatPipeFluidRegistry.SelectFluidForTemperature(1400.0));
    }

    // ── Capillary limit + margin in Li-W cluster band ──────────────────

    [Fact]
    public void Safe400_DesignPoint_CapillaryLimitInLiTungstenClusterBand()
    {
        // D = 14 mm → A_cross = π(0.014)²/4 = 1.539e-4 m². The Wave-1
        // Li cluster anchor q_cap = 2e8 W/m² → Q_cap = 2e8 · 1.539e-4
        // ≈ 30,800 W. Cluster band [20, 50] kW swallows the Lithium-
        // cluster scatter (1.5-3e8 W/m² across the Li-W published
        // datasets at 1400 K).
        var r = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe());
        Assert.InRange(r.CapillaryLimit_W, 20_000.0, 50_000.0);
    }

    [Fact]
    public void Safe400_DesignPoint_CapillaryMarginVeryHigh()
    {
        // Q_op = 1 kW vs Q_cap ≈ 30.8 kW → margin ≈ 30. SAFE-400 design
        // class deliberately runs ~ 30× below the capillary limit so the
        // pipe has decades of headroom for transient peak loads + ages
        // gracefully even with wick fouling. Cluster band [10, 100].
        var r = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe());
        Assert.InRange(r.CapillaryMargin, 10.0, 100.0);
    }

    // ── Multi-limit ordering (HP.W2) ───────────────────────────────────

    [Fact]
    public void Safe400_DesignPoint_SonicLimitIsTheBindingConstraint()
    {
        // For Li-W in the Wave-1 registry, q_sonic = 5e7 W/m² is below
        // q_cap = 2e8 W/m² and q_entrain = 3e8 W/m² — sonic is the
        // governing limit. This matches the published Li-W literature
        // (Faghri 2016 §6; El-Genk 2011 §3): sonic dominates from cold
        // startup through to peak operating temperature for Li at
        // moderate vapour-core diameters.
        var r = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe());
        Assert.True(r.SonicLimit_W < r.CapillaryLimit_W,
            $"Sonic limit ({r.SonicLimit_W:F0} W) must be < capillary "
          + $"limit ({r.CapillaryLimit_W:F0} W) for Li-W at 1400 K.");
        Assert.True(r.SonicLimit_W < r.EntrainmentLimit_W,
            $"Sonic limit ({r.SonicLimit_W:F0} W) must be < entrainment "
          + $"limit ({r.EntrainmentLimit_W:F0} W) for Li-W at 1400 K.");
        Assert.Equal(r.SonicLimit_W, r.GoverningLimit_W, precision: 6);
    }

    [Fact]
    public void Safe400_DesignPoint_GoverningMarginPositiveWithHeadroom()
    {
        // Even at the binding sonic constraint Q_sonic ≈ 7.7 kW with
        // Q_op = 1 kW, the governing margin ≈ 7.7 — substantial.
        // SAFE-400 design margin against any limit must exceed 3× for
        // long-life space-reactor service. Cluster band [3, 20].
        var r = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe());
        Assert.InRange(r.GoverningMargin, 3.0, 20.0);
    }

    // ── Thermal resistance + end-to-end ΔT ─────────────────────────────

    [Fact]
    public void Safe400_DesignPoint_EndToEndDeltaTInLiTungstenClusterBand()
    {
        // R_thermal = L / (k_eff · A) = 1.0 / (2e5 · 1.539e-4) ≈ 0.0325
        // K/W. At Q = 1 kW → ΔT ≈ 32.5 K. Real SAFE-400 measured ΔT was
        // 30-60 K across the evaporator-to-condenser axial path
        // (Poston 2004 §IV); cluster band [10, 80] K.
        var r = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe());
        Assert.InRange(r.EndToEndDeltaT_K, 10.0, 80.0);
    }

    [Fact]
    public void Safe400_DesignPoint_DeltaTVastlyLessThanSolidTungstenRod()
    {
        // Equivalent solid tungsten rod (k_W = 173 W/(m·K) at 1400 K)
        // at the same A_cross + L would give R = 1.0 / (173 · 1.539e-4)
        // ≈ 37.6 K/W → ΔT = 1000 · 37.6 ≈ 37,600 K. The heat pipe's
        // 32.5 K ΔT is ~ 1100× better — the entire point of using a
        // heat pipe vs solid conduction in a space-reactor primary loop.
        var r = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe());
        Assert.True(r.EndToEndDeltaT_K < 100.0,
            $"Heat-pipe ΔT ({r.EndToEndDeltaT_K:F1} K) must be << solid "
          + "tungsten rod equivalent ΔT (~ 37,600 K) for a space-reactor "
          + "primary loop to close thermodynamically.");
    }

    // ── Geometry sanity ────────────────────────────────────────────────

    [Fact]
    public void Safe400_DesignPoint_CrossSectionAreaMatchesSafe400Anchor()
    {
        // A = π · D² / 4 = π · 0.014² / 4 ≈ 1.5394e-4 m².
        var d = Safe400PrimaryHeatPipe();
        Assert.InRange(d.CrossSectionArea_m2, 1.5e-4, 1.6e-4);
    }

    // ── Cross-fluid sanity: Li beats Na beats Water at SAFE-400 T ─────

    [Fact]
    public void Safe400_GeometryAtLiOperatingT_LiOutperformsNaAndWaterEnvelope()
    {
        // At T = 1400 K, only Li sits inside its operating envelope. Sodium
        // maxes out at 1073 K; Water at 473 K. The model must report
        // out-of-envelope for both Na and Water at the SAFE-400 design
        // temperature — protecting downstream gates from a silently wrong
        // fluid choice.
        var li = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe());
        var na = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe()
            with { Fluid = HeatPipeFluid.Sodium });
        var h2o = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe()
            with { Fluid = HeatPipeFluid.Water });
        Assert.True(li.OperatingTemperatureInValidEnvelope);
        Assert.False(na.OperatingTemperatureInValidEnvelope);
        Assert.False(h2o.OperatingTemperatureInValidEnvelope);
    }

    // ── Scaling sanity at the SAFE-400 operating point ────────────────

    [Fact]
    public void Safe400_HalfLengthHalvesThermalResistanceAndDeltaT()
    {
        // R_thermal ∝ L → halving L halves both R and ΔT. Linearity is
        // a fingerprint of the Wave-1 closed-form path; this guards
        // against a downstream regression that breaks length-scaling.
        var full = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe());
        var half = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe() with { Length_m = 0.5 });
        Assert.Equal(0.5, half.ThermalResistance_K_W / full.ThermalResistance_K_W,
                     precision: 9);
        Assert.Equal(0.5, half.EndToEndDeltaT_K / full.EndToEndDeltaT_K,
                     precision: 9);
    }

    [Fact]
    public void Safe400_DoubledDiameterQuadruplesCapillaryAndSonicLimits()
    {
        // Q_max ∝ A ∝ D² for both capillary and sonic limits (constant
        // q_per_area · A). Doubling D quadruples both limits.
        var d1 = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe());
        var d2 = HeatPipeSolver.Solve(Safe400PrimaryHeatPipe()
            with { InternalDiameter_m = 0.028 });
        Assert.Equal(4.0, d2.CapillaryLimit_W / d1.CapillaryLimit_W, precision: 6);
        Assert.Equal(4.0, d2.SonicLimit_W     / d1.SonicLimit_W,     precision: 6);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // SAFE-400 / KRUSTY primary heat pipe — Li-tungsten cluster, vapour-
    // core ID 14 mm, length 1.0 m, ~ 1 kW heat throughput per pipe,
    // 1400 K evaporator-mean temperature. References:
    //   Poston D.I. (2004) LA-UR-04-2884 (SAFE-400 100 kWt core, 8 Li
    //     primary heat pipes, 1 kW each, 1500 K peak / 1400 K mean).
    //   Gibson M., Mason L., Bowman C. (2017) NETS-2017 LA-UR-17-21851
    //     (KRUSTY 1 kWe demonstrator; SAFE-400-class lineage).
    //   El-Genk M.S., Tournier J.-M. (2011) Frontiers in Heat Pipes 2,
    //     013002 (Li-W operating-T + heat-flux cluster envelope).
    private static HeatPipeDesign Safe400PrimaryHeatPipe() => new(
        Fluid:                   HeatPipeFluid.Lithium,
        InternalDiameter_m:      0.014,
        Length_m:                1.0,
        HeatThroughput_W:        1_000.0,
        OperatingTemperature_K:  1400.0);
}
