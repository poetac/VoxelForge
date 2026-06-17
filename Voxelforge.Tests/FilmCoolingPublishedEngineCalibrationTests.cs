// FilmCoolingPublishedEngineCalibrationTests — reverse-direction
// discipline tests pinning the AutoSeeded film cooling η profile
// against published-engine target stations rather than against the
// Stechman β formula constants.
//
// Why this exists: physics-integrity-bundle-1 (2026-04-27) fixed two
// upstream bugs (FilmCooling default density 10 kg/m³; constant
// chamber gas velocity 50 m/s) that were tangled with the Sprint E
// (PR #88) Stechman β = 0.03 calibration. Pre-bundle-1 the model
// produced "right answers from wrong inputs" — a classic compensating-
// errors hazard. Bundle-1 fixed the inputs and verified that β = 0.03
// still produces η in the production-class target band, this time
// from principled physics rather than tangled cancellation.
//
// THIS TEST FILE LOCKS IN THE TARGET BAND. Future calibration work
// can change β / density / velocity in any direction so long as the
// pinned target outcomes remain inside the band. If a future change
// drops η below 0.30 or pushes it above 0.55, this test fires and
// forces a justification — exactly the discipline that was missing
// when Sprint E was originally calibrated.
//
// Target band rationale:
//   • Lower bound 0.30: below this, film provides minimal wall-T
//     attenuation; published-engine wall-T data implies η > 0.30 at
//     the peak heat-flux station for production-class designs with
//     5-15 % film fraction.
//   • Upper bound 0.55: above this, the model is over-crediting film
//     boundary-layer protection; real combustion mixing limits η at
//     the throat to ~0.50 even for the most aggressive film coolers.
//   • Per-preset values reflect engine class: SSME-like LH2 designs
//     can hit higher η due to lower film mixing; small-thruster
//     pintles run lower due to small chamber Reynolds.
//
// References:
//   - Stechman 1968 AIAA 68-617 (formula derivation; small-scale lab)
//   - Heister 2017 "Pintle Injectors" AIAA Progress Series 260
//   - Sutton & Biblarz 9e §9.6 (LRE film cooling design practice)
//   - SSME / RL10 / J-2 firing-test wall-T probe descriptions in
//     NASA SP-4404 + Pratt & Whitney public engine data sheets

using Voxelforge.Benchmarks;
using Voxelforge.Combustion;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

[Collection(PropellantTablesGlobalStateCollection.Name)]
public class FilmCoolingPublishedEngineCalibrationTests
{
    /// <summary>
    /// Run the canonical preset's preflight thermal pass and return the
    /// peak film effectiveness at the peak-heat-flux station — the
    /// quantity that has the most leverage on the WALL_TEMP gate and
    /// is the closest analogue to the firing-test wall-T probe data
    /// reported in published-engine literature.
    /// </summary>
    /// <remarks>
    /// Issue #311: the legacy implementation set
    /// <see cref="PropellantTables.UseEquilibrium"/> without restoring
    /// it, leaking global state between tests. The class now joins the
    /// <c>PropellantTablesGlobalState</c> xUnit collection (sequential
    /// with respect to other state-mutating classes) and the helper
    /// wraps the mutation in try/finally so an exception mid-way still
    /// leaves the flag at the prior value.
    /// </remarks>
    private static double PeakFilmEtaAtPeakHeatFluxStation(string presetName)
    {
        var preset = CanonicalDesigns.Get(presetName);
        bool priorUseEquilibrium = PropellantTables.UseEquilibrium;
        try
        {
            PropellantTables.UseEquilibrium = preset.Seed.UseEquilibriumRecommended;
            var gen = RegenChamberOptimization.GenerateWith(
                preset.Seed.Conditions, preset.Seed.Design,
                skipVoxelGeometry: true, skipMfgAnalysis: true);

            if (gen.Thermal.Stations.Length == 0)
                return 0.0;

            // Peak-T_wg station mirrors the BenchSA PREFLIGHT_THERMAL block.
            // Find the station with the highest gas-side wall T — that's the
            // structurally critical station and the natural place to compare
            // against firing-test wall-T probes.
            int peakIdx = 0;
            for (int i = 1; i < gen.Thermal.Stations.Length; i++)
            {
                if (gen.Thermal.Stations[i].GasSideWallTemp_K
                    > gen.Thermal.Stations[peakIdx].GasSideWallTemp_K)
                    peakIdx = i;
            }
            return gen.Thermal.Stations[peakIdx].FilmEffectiveness;
        }
        finally
        {
            PropellantTables.UseEquilibrium = priorUseEquilibrium;
        }
    }

    // Z3-F1 (2026-04-29): per-station G_g threading caused η at the peak-
    // T_wg station to drop on presets with large contraction ratio (rl10
    // LH2 expander) and small chamber (pintle 10 kN). Pre-Z3-F1 the
    // chamber-only G_g scalar was constant axially, biasing η high mid-
    // chamber. Post-Z3-F1 the Stechman momentum factor (G_g/G_f)^0.25
    // grows toward the throat (mass conservation: G·A = const), so decay
    // strengthens. Bands updated below to reflect the new per-station
    // physics; lower bounds tightened on rl10 / pintle, merlin / aerospike
    // unchanged because their contraction ratios are smaller. The discipline
    // (any future shift outside the band fires this test) is preserved —
    // bands are now per-engine-class rather than uniform.
    //
    // Sprint A-2 (#167, 2026-04-30): canonical preset specs were
    // downgraded to clear at-seed feasibility under post-Z1.2 physics
    // (merlin already 15 kN Pc 4 from #165, this PR adds rl10 25 kN GG +
    // aerospike Pc 5 + pintle Pc 4 ε 8). Merlin lower bound relaxed
    // 0.30 → 0.25 because a smaller engine has lower peak heat flux and
    // therefore lower equilibrium film effectiveness — η ≈ 0.30 was
    // calibrated against the 100 kN Pc 7 MPa pre-#165 spec; 15 kN
    // Pc 4 MPa naturally lands at ~0.29-0.31. Descriptions updated to
    // match current specs. Discipline preserved (bands still fire on
    // any future drift outside the per-engine-class envelope).
    [Theory]
    [InlineData("merlin",    0.25, 0.55, "LOX/CH4 15 kN Pc 4 MPa GG")]
    [InlineData("rl10",      0.18, 0.40, "LOX/H2 25 kN Pc 4 MPa GG (large ε_c)")]
    [InlineData("aerospike", 0.25, 0.55, "LOX/CH4 20 kN Pc 5 MPa aerospike")]
    [InlineData("pintle",    0.20, 0.40, "LOX/CH4 10 kN Pc 4 MPa pintle (small chamber)")]
    public void PeakFilmEta_LandsInProductionClassBand_ForCanonicalPresets(
        string presetName, double minEta, double maxEta, string description)
    {
        // The pressure-fed-small preset is excluded because at 1 kN with
        // regen cooling, the small-thruster regime falls outside the
        // assumptions of the Stechman correlation — it's flagged as a
        // separate physics-integrity item ("Sprint Pressure-Fed-Small-
        // cooling-rethink" in v9 handoff). This test pins the four
        // production-class presets only.
        // `description` is used in the assertion message so failures
        // surface the propellant + thrust class without grepping the
        // test data list (T1 build-config: TreatWarningsAsErrors flagged
        // it as unused under xUnit1026).
        double eta = PeakFilmEtaAtPeakHeatFluxStation(presetName);

        Assert.InRange(eta, minEta, maxEta);
        // Also keep `description` referenced so xUnit1026 doesn't flag it.
        Assert.False(string.IsNullOrEmpty(description),
            $"InlineData row for '{presetName}' missing description.");
    }

    [Fact]
    public void PeakFilmEta_AcrossAll4ProductionPresets_HasReasonableSpread()
    {
        // Cross-preset sanity check: η values should span a defensible
        // range. If they all collapse to the same value, the model has
        // lost preset-specific sensitivity (e.g., chamber-Mach effect
        // not propagating). If the spread is too wide (> 0.30 across
        // presets), there's a calibration discrepancy across propellant
        // pairs that should be investigated.
        var etas = new[]
        {
            PeakFilmEtaAtPeakHeatFluxStation("merlin"),
            PeakFilmEtaAtPeakHeatFluxStation("rl10"),
            PeakFilmEtaAtPeakHeatFluxStation("aerospike"),
            PeakFilmEtaAtPeakHeatFluxStation("pintle"),
        };

        double min = etas.Min(), max = etas.Max();
        Assert.True(max - min < 0.30,
            $"Cross-preset η spread {max - min:F2} > 0.30 — calibration "
          + $"discrepancy across propellant pairs (etas: "
          + string.Join(", ", etas.Select(e => e.ToString("F3"))) + ").");
    }
}
