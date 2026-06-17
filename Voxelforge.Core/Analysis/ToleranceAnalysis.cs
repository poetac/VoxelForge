// ToleranceAnalysis.cs — Monte-Carlo sweep over LPBF manufacturing
// tolerances to produce a distribution of thermal + structural outcomes
// rather than a single nominal design point.
//
// Why this matters: LPBF part-to-part variability on wall thickness is
// typically ±0.10 mm (3σ) even with well-controlled parameters. A regen
// chamber with 0.8 mm nominal wall therefore sees ±12.5 % variation in
// hoop stress and ±40 % variation in radial thermal resistance. A
// nominal-design-only analysis can silently blow past yield on the
// worst-case print.
//
// Approach:
//   • Perturb the key geometric inputs (wall thickness, channel height,
//     rib thickness, jacket thickness) by normal draws with σ = tol/3
//     so the ±tol band corresponds to 3σ (≈ 99.7 % of samples).
//   • For each sample, re-run the THERMAL and STRUCTURAL passes only —
//     do NOT regenerate the voxel geometry. Voxel build is too slow
//     (~10 s) to Monte-Carlo; we use the existing contour + channels +
//     material cards and just vary dimensions in the physics solver.
//   • Collect peak wall T, min safety factor, coolant ΔP, coolant
//     outlet T. Report p10 / p50 / p90 / p99 quantiles + count of
//     samples that would yield (SF < 1).
//
// This runs on the UI thread — it's pure math and N = 500 samples
// finishes in 2–5 s for a 120-station design.
//
// NOT modelled (would need voxel regen):
//   • Port threading tolerance
//   • Surface roughness effect on h_c
//   • Bolt-hole positional tolerance
//   • Thermal expansion mismatch at flanges

using System.Threading;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;
using Voxelforge.Structure;

namespace Voxelforge.Analysis;

// ToleranceInputs, ToleranceQuantile, and ToleranceResult records were
// extracted to Voxelforge.Core/Analysis/ToleranceTypes.cs as
// part of A2 to break the App ↔ Voxels dependency loop (RegenChamberOptimization
// in Voxels references these types).

public static class ToleranceAnalysis
{
    public static ToleranceResult Run(
        ChamberContour contour,
        OperatingConditions cond,
        RegenChamberDesign nominalDesign,
        ToleranceInputs inputs,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        int N = Math.Max(inputs.SampleCount, 10);

        var peakTwg = new double[N];
        var minSF = new double[N];
        var dP = new double[N];
        var coolantOut = new double[N];
        var throatQ = new double[N];
        int yieldCount = 0, tlimCount = 0;
        long ticksTotal = 0;

        var gas = PropellantTables.Lookup(
            cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, nominalDesign);
        var material = WallMaterials.All[
            Math.Clamp(cond.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
        var pairMeta = PropellantPairs.GetMeta(cond.PropellantPair);
        var fluid = CoolantRegistry.Get(pairMeta.CoolantFluidKey);

        // Parallel.For over the Monte Carlo loop. Each iteration writes
        // only `[i]` in the result arrays so there are no shared-write
        // hazards. The RNG is seeded per-iteration with `RandomSeed + i`
        // so per-index draws are bit-identical to a sequential reference
        // run with the same seed — the parallelisation re-orders WHEN
        // samples compute, not WHAT they compute. Counters (yieldCount,
        // tlimCount, ticksTotal) use Interlocked.Add via a local-state
        // aggregator to stay deterministic in their final value.
        // Honour the user's active resource budget
        // (ResourceBudget.MaxParallelism) and cancellation token.
        // MaxParallelism defaults to cores-2 on a fresh run; Maximum
        // mode restores all cores. cancellationToken lets the Stop
        // button abort mid-sweep instead of waiting for the current
        // batch of samples to finish.
        var parallelOpts = new ParallelOptions
        {
            MaxDegreeOfParallelism = Voxelforge.UI.ResourceBudget.MaxParallelism,
            CancellationToken = cancellationToken,
        };

        Parallel.For(
            fromInclusive: 0,
            toExclusive:   N,
            parallelOptions: parallelOpts,
            localInit: () => (yield: 0, tlim: 0, ticks: 0L),
            body: (i, _, local) =>
            {
                var swLocal = System.Diagnostics.Stopwatch.StartNew();
                var rng = new Random(inputs.RandomSeed + i);

                double twall = Perturb(rng, nominalDesign.GasSideWallThickness_mm,
                                       inputs.WallThicknessTolerance_mm);
                double hChamber = Perturb(rng, nominalDesign.ChannelHeightChamber_mm,
                                          inputs.ChannelHeightTolerance_mm);
                double hThroat = Perturb(rng, nominalDesign.ChannelHeightThroat_mm,
                                         inputs.ChannelHeightTolerance_mm);
                double hExit = Perturb(rng, nominalDesign.ChannelHeightExit_mm,
                                       inputs.ChannelHeightTolerance_mm);
                double rib = Perturb(rng, nominalDesign.RibThickness_mm,
                                     inputs.RibThicknessTolerance_mm);
                double jkt = Perturb(rng, nominalDesign.OuterJacketThickness_mm,
                                     inputs.JacketThicknessTolerance_mm);

                // Clamp to physically sensible floors so the perturbation
                // never crosses zero.
                twall = Math.Max(twall, 0.2);
                hChamber = Math.Max(hChamber, 0.3);
                hThroat = Math.Max(hThroat, 0.3);
                hExit = Math.Max(hExit, 0.3);
                rib = Math.Max(rib, 0.3);
                jkt = Math.Max(jkt, 0.5);

                var channels = new ChannelSchedule(
                    ChannelCount: nominalDesign.ChannelCount,
                    RibThickness_mm: rib,
                    GasSideWallThickness_mm: twall,
                    ChannelHeightAtChamber_mm: hChamber,
                    ChannelHeightAtThroat_mm: hThroat,
                    ChannelHeightAtExit_mm: hExit);

                double filmFrac = nominalDesign.FilmCooling.Enabled
                    ? nominalDesign.FilmCooling.FuelFractionAsFilm : 0;
                double coolantMass = derived.FuelMassFlow_kgs * (1.0 - filmFrac);

                // Sprint 33 (2026-04-24): propagate the design's helix
                // pitch + LPBF roughness into the tolerance solver so the
                // Monte-Carlo cloud uses the same physics as the nominal
                // optimizer pass. Helical was a latent miss pre-Sprint-33;
                // LPBF roughness is the new PH-7 control surface.
                double mcHelixDeg = nominalDesign.ChannelTopology == ChannelTopology.Helical
                    ? Math.Clamp(nominalDesign.HelixPitchAngle_deg, 0.0, 45.0)
                    : 0.0;
                var solverInputs = new RegenSolverInputs(
                    Contour: contour,
                    Gas: gas,
                    Wall: material,
                    Channels: channels,
                    CoolantMassFlow_kgs: coolantMass,
                    CoolantInletTemp_K: cond.CoolantInletTemp_K,
                    CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
                    FilmCooling: nominalDesign.FilmCooling,
                    AxialConductionSweeps: nominalDesign.AxialConductionSweeps,
                    RadialWallNodes: nominalDesign.RadialWallNodes,
                    EnableBartzBLCorrections: nominalDesign.EnableBartzBLCorrections,
                    CoolantFluid: fluid,
                    HelixPitchAngle_deg: mcHelixDeg,
                    LpbfRelativeRoughness: Math.Max(nominalDesign.LpbfRelativeRoughness, 0.0),
                    EnableTranspirationCooling: nominalDesign.EnableTranspirationCooling,
                    TranspirationBleedFraction: nominalDesign.TranspirationBleedFraction,
                    TranspirationEfficiency:    nominalDesign.TranspirationEfficiency);

                var thermal = RegenCoolingSolver.Solve(solverInputs);
                // Sprint feasibility-audit-G' (2026-04-27): credit perturbed
                // outer jacket + use γ_throat for per-station gas static P.
                // Tolerance Monte-Carlo perturbs `jkt` already (line 116), so
                // the jacket-thickness sensitivity now flows through hoop
                // stress as it should.
                var stress = StructuralCheck.Evaluate(
                    thermal, material, twall, cond.ChamberPressure_Pa,
                    outerJacketThickness_mm: jkt,
                    gasGamma: gas.GammaThroat);

                peakTwg[i] = thermal.PeakGasSideWallT_K;
                minSF[i] = stress.MinSafetyFactor;
                dP[i] = thermal.CoolantPressureDrop_Pa;
                coolantOut[i] = thermal.CoolantOutletT_K;
                throatQ[i] = thermal.ThroatHeatFlux_Wm2;

                int dy = stress.YieldExceeded ? 1 : 0;
                int dt = thermal.WallTempExceedsLimit ? 1 : 0;
                swLocal.Stop();
                return (local.yield + dy, local.tlim + dt, local.ticks + swLocal.ElapsedTicks);
            },
            localFinally: final =>
            {
                Interlocked.Add(ref yieldCount, final.yield);
                Interlocked.Add(ref tlimCount,  final.tlim);
                Interlocked.Add(ref ticksTotal, final.ticks);
            });

        double meanMs = (double)ticksTotal / System.Diagnostics.Stopwatch.Frequency * 1000.0 / N;

        if (yieldCount > 0)
            warnings.Add($"{yieldCount}/{N} samples ({100.0*yieldCount/N:F1}%) yielded at nominal load.");
        if (tlimCount > 0)
            warnings.Add($"{tlimCount}/{N} samples ({100.0*tlimCount/N:F1}%) exceeded wall T limit.");
        if ((double)yieldCount / N > 0.10)
            warnings.Add("Yield-exceedance rate > 10% — design not robust to print tolerances.");

        return new ToleranceResult(
            SampleCount: N,
            PeakWallT_K: Quantiles(peakTwg),
            MinSafetyFactor: Quantiles(minSF),
            CoolantPressureDrop_Pa: Quantiles(dP),
            CoolantOutletT_K: Quantiles(coolantOut),
            ThroatHeatFlux_Wm2: Quantiles(throatQ),
            YieldExceededCount: yieldCount,
            WallTLimitExceededCount: tlimCount,
            MeanComputeTime_ms: meanMs,
            Warnings: warnings.ToArray(),
            // Surface the raw per-sample draws for the
            // ToleranceHistogramPanel. Arrays are already allocated +
            // written above — just hand them off.
            Samples_PeakWallT_K:              peakTwg,
            Samples_MinSafetyFactor:          minSF,
            Samples_CoolantPressureDrop_Pa:   dP,
            Samples_CoolantOutletT_K:         coolantOut,
            Samples_ThroatHeatFlux_Wm2:       throatQ);
    }

    /// <summary>Box-Muller normal draw × σ + μ, clamped to ±3σ to avoid
    /// unphysical tails.</summary>
    private static double Perturb(Random rng, double nominal, double tolBand)
    {
        double sigma = tolBand / 3.0;
        double u1 = Math.Max(rng.NextDouble(), 1e-12);
        double u2 = rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        z = Math.Clamp(z, -3.0, 3.0);
        return nominal + z * sigma;
    }

    private static ToleranceQuantile Quantiles(double[] xs)
    {
        var sorted = (double[])xs.Clone();
        Array.Sort(sorted);
        return new ToleranceQuantile(
            P10: Percentile(sorted, 0.10),
            P50: Percentile(sorted, 0.50),
            P90: Percentile(sorted, 0.90),
            P99: Percentile(sorted, 0.99));
    }

    private static double Percentile(double[] sortedAsc, double p)
    {
        int n = sortedAsc.Length;
        double pos = (n - 1) * p;
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        double frac = pos - lo;
        return sortedAsc[lo] * (1 - frac) + sortedAsc[hi] * frac;
    }
}
