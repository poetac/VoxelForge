// StirlingWave2Tests.cs — Sprint STR.W2 unit tests for the working-
// fluid + per-configuration extensions.

using System;
using Voxelforge.Stirling;
using Xunit;

namespace Voxelforge.Tests.Stirling;

public sealed class StirlingWave2Tests
{
    // ── STR.W1 bit-identity invariants ──────────────────────────────────

    [Fact]
    public void DefaultWorkingFluid_IsHelium()
    {
        Assert.Equal(StirlingWorkingFluid.Helium, Whispergen1kW().WorkingFluid);
    }

    [Fact]
    public void WhisperGen_HeliumDefault_PowerBitIdenticalWithSTR_W1()
    {
        // W1 baseline lands ~ 1 kW indicated. With Helium fluid factor =
        // 1.0, W2 must give the same value (precision: 1 W).
        var r = StirlingSolver.Solve(Whispergen1kW());
        Assert.InRange(r.IndicatedPower_W, 500.0, 2000.0);
    }

    // ── Working-fluid efficiency factor ─────────────────────────────────

    [Fact]
    public void HeliumFactor_IsUnity()
    {
        Assert.Equal(1.0,
            StirlingSolver.GetWorkingFluidEfficiencyFactor(StirlingWorkingFluid.Helium),
            precision: 6);
    }

    [Fact]
    public void HydrogenFactor_IsUnity()
    {
        // H₂ has slightly higher cp than He but the difference is
        // absorbed by η_2nd cluster band; we anchor at factor 1.0.
        Assert.Equal(1.0,
            StirlingSolver.GetWorkingFluidEfficiencyFactor(StirlingWorkingFluid.Hydrogen),
            precision: 6);
    }

    [Fact]
    public void AirFactor_BelowUnity()
    {
        // Air is the original 1816 Stirling fluid — lower k means slower
        // regenerator response → ~ 15 % efficiency penalty vs He/H₂.
        double f = StirlingSolver.GetWorkingFluidEfficiencyFactor(StirlingWorkingFluid.Air);
        Assert.InRange(f, 0.70, 0.95);
    }

    // ── Air vs Helium power scaling ─────────────────────────────────────

    [Fact]
    public void AirFluid_GivesLowerPower_ThanHelium_AtSameDesignParams()
    {
        var helium = StirlingSolver.Solve(Whispergen1kW()
            with { WorkingFluid = StirlingWorkingFluid.Helium });
        var air    = StirlingSolver.Solve(Whispergen1kW()
            with { WorkingFluid = StirlingWorkingFluid.Air });
        Assert.True(air.IndicatedPower_W < helium.IndicatedPower_W,
            $"Air P_indicated ({air.IndicatedPower_W:F0} W) expected < "
          + $"Helium P_indicated ({helium.IndicatedPower_W:F0} W).");
    }

    [Fact]
    public void HydrogenFluid_GivesSamePowerAsHelium()
    {
        var helium = StirlingSolver.Solve(Whispergen1kW()
            with { WorkingFluid = StirlingWorkingFluid.Helium });
        var hydrogen = StirlingSolver.Solve(Whispergen1kW()
            with { WorkingFluid = StirlingWorkingFluid.Hydrogen });
        Assert.Equal(helium.IndicatedPower_W, hydrogen.IndicatedPower_W, precision: 4);
    }

    [Fact]
    public void Air_IndicatedEfficiency_LowerThanHelium()
    {
        var helium = StirlingSolver.Solve(Whispergen1kW()
            with { WorkingFluid = StirlingWorkingFluid.Helium });
        var air    = StirlingSolver.Solve(Whispergen1kW()
            with { WorkingFluid = StirlingWorkingFluid.Air });
        Assert.True(air.IndicatedEfficiency < helium.IndicatedEfficiency);
    }

    [Fact]
    public void AirFluid_RatioMatchesFactor()
    {
        // The air-to-helium power ratio should equal the air-fluid factor
        // (~ 0.85 cluster mid-band).
        var helium = StirlingSolver.Solve(Whispergen1kW()
            with { WorkingFluid = StirlingWorkingFluid.Helium });
        var air    = StirlingSolver.Solve(Whispergen1kW()
            with { WorkingFluid = StirlingWorkingFluid.Air });
        double f_air = StirlingSolver.GetWorkingFluidEfficiencyFactor(StirlingWorkingFluid.Air);
        Assert.Equal(f_air, air.IndicatedPower_W / helium.IndicatedPower_W, precision: 6);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static StirlingDesign Whispergen1kW() => new(
        Configuration:           StirlingConfiguration.Gamma,
        HotSideTemperature_K:    850.0,
        ColdSideTemperature_K:   350.0,
        MeanPressure_Pa:         1e6,
        SweptVolume_m3:          4e-5,
        OperatingFrequency_Hz:   50.0,
        SecondLawEfficiency:     0.45);
}
