// RocketPipelineSmokeTests.cs — headless end-to-end coverage of the rocket
// generate pipeline. AutoSeeder.Seed (pure-math, no PicoGK) builds a baseline
// design + operating conditions from a high-level spec; GenerateWith (run with
// skipVoxelGeometry, so only the physics executes) turns that into a full
// RegenGenerationResult. Both live in Voxelforge.Core, so the whole chain runs
// on the Linux CI 'core' leg — the first cross-platform exercise of the
// integrated rocket physics, not just its primitives.
//
// Assertions check physically defensible bands for a LOX/CH4 point (flame
// temperature, C*, vacuum-over-sea-level Isp, nozzle expansion, mass balance)
// plus the project's bit-for-bit determinism guarantee. Pc = 8 MPa keeps the
// seeder below its 10 MPa equilibrium-correction threshold, so no global
// PropellantTables state is touched and the run stays deterministic.

using Voxelforge.Combustion;
using Voxelforge.Optimization;

namespace Voxelforge.Core.Tests;

public sealed class RocketPipelineSmokeTests
{
    private static EngineSpec Spec() => new(
        PropellantPair:     PropellantPair.LOX_CH4,
        Thrust_N:           100_000.0,   // 100 kN
        ChamberPressure_Pa: 8.0e6,       // 8 MPa
        ExpansionRatio:     40.0);

    private static RegenGenerationResult Generate()
    {
        AutoSeedResult seed = AutoSeeder.Seed(Spec());
        return RegenChamberOptimization.GenerateWith(
            seed.Conditions, seed.Design, skipVoxelGeometry: true);
    }

    [Fact]
    public void Seed_ProducesUsableDesign()
    {
        AutoSeedResult seed = AutoSeeder.Seed(Spec());
        Assert.NotEmpty(seed.Rationale);            // narrative trail populated
        Assert.True(seed.Conditions.Thrust_N > 0.0, $"target thrust {seed.Conditions.Thrust_N}");
    }

    [Fact]
    public void Pipeline_RunsHeadless_AndSolvesEveryStage()
    {
        var gen = Generate();
        Assert.False(string.IsNullOrEmpty(gen.DesignHash));      // provenance hash computed
        Assert.True(gen.Derived.ThroatRadius_mm > 0.0, "geometry");
        Assert.True(gen.Derived.TotalMassFlow_kgs > 0.0, "performance");
        Assert.True(gen.Gas.ChamberTemp_K > 0.0, "combustion");
    }

    [Fact]
    public void Pipeline_CombustionTemperature_IsInLoxMethaneBand()
    {
        var gen = Generate();
        // LOX/CH4 near-peak-C* flame temperature is ~3500 K.
        Assert.InRange(gen.Gas.ChamberTemp_K, 2800.0, 4000.0);
        Assert.InRange(gen.Gas.Gamma, 1.05, 1.40);
    }

    [Fact]
    public void Pipeline_VacuumIsp_ExceedsSeaLevel_AndIsPhysical()
    {
        var gen = Generate();
        double ispVac = gen.Derived.IdealIspVacuum_s;
        double ispSl = gen.Derived.IdealIspSeaLevel_s;
        Assert.True(ispVac > ispSl, $"vacuum Isp {ispVac} should exceed sea-level {ispSl}");
        // Preliminary-design LOX/CH4 vacuum Isp at ε=40 lands well inside this band.
        Assert.InRange(ispVac, 250.0, 400.0);
    }

    [Fact]
    public void Pipeline_CStar_IsInLoxMethaneBand()
    {
        var gen = Generate();
        // LOX/CH4 characteristic velocity is ~1800 m/s.
        Assert.InRange(gen.Derived.CStarActual_ms, 1500.0, 2100.0);
    }

    [Fact]
    public void Pipeline_NozzleGeometry_DivergesAndRecoversExpansionRatio()
    {
        var gen = Generate();
        double rt = gen.Derived.ThroatRadius_mm;
        double re = gen.Derived.ExitRadius_mm;
        Assert.True(rt > 0.0, $"throat radius {rt}");
        Assert.True(re > rt, $"exit {re} should exceed throat {rt}");
        // Area ratio (re/rt)² should recover the requested ε = 40.
        double areaRatio = (re / rt) * (re / rt);
        Assert.InRange(areaRatio, 0.8 * 40.0, 1.2 * 40.0);
    }

    [Fact]
    public void Pipeline_MassFlow_BalancesAndMatchesMixtureRatio()
    {
        var gen = Generate();
        double total = gen.Derived.TotalMassFlow_kgs;
        double fuel = gen.Derived.FuelMassFlow_kgs;
        double ox = gen.Derived.OxidizerMassFlow_kgs;
        Assert.True(total > 0.0, $"total mass flow {total}");
        Assert.True(Math.Abs(total - (fuel + ox)) <= 1e-6 * total,
            $"mass balance: {total} vs {fuel} + {ox}");
        // O/F for LOX/CH4 sits in the ~2.5–4 band.
        Assert.InRange(ox / fuel, 2.0, 4.5);
    }

    [Fact]
    public void Pipeline_IsDeterministic()
    {
        var a = Generate();
        var b = Generate();
        Assert.Equal(a.DesignHash, b.DesignHash);
        Assert.Equal(a.Derived.IdealIspVacuum_s, b.Derived.IdealIspVacuum_s);
        Assert.Equal(a.Gas.ChamberTemp_K, b.Gas.ChamberTemp_K);
    }
}
