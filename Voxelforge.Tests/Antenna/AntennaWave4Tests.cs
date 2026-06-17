// AntennaWave4Tests.cs — Sprint ANT.W4 acceptance tests for the
// Helical, Patch, and CrossedDipole topology extensions.
//
// Published anchors:
//   Helical — Kraus end-fire formula G = 15·N·(C/λ)²·(S/λ)
//     [Kraus J.D. (1988). "Antennas," 2nd ed., §7-4.]
//     N=10, C/λ=1.0, S/λ=0.25: G = 37.5 linear → 15.74 dBi.
//     Acceptance band [14, 18] dBi for this configuration.
//
//   Patch — microstrip resonant patch.
//     Published range 6.5–8.5 dBi; cluster centroid 7.5 dBi.
//     [Balanis C. (2016). "Antenna Theory," 4th ed., §14.2.]
//
//   CrossedDipole — circular-polarisation crossed dipole.
//     Gain = HalfWaveDipoleGain_dBi = 2.15 dBi. Quadrature feed
//     selects CP sense, does not increase gain.
//
// LEO cubesat UHF fixture — helical UHF uplink to an LEO cubesat.
//   f = 437.5 MHz (UHF AMSAT band), P_tx = 5 W, R = 600 km (typical
//   LEO pass at 30° elevation), helical Tx (N=10, Kraus optimal),
//   helical Rx on cubesat (N=3, compact). Both Bpsk-LdpcR12 at 9.6 kbps.
//   Anchor: a healthy UHF cubesat uplink with high-gain ground station
//   should close; marginal 3-turn compact helix on Rx is the test point.

using System;
using Voxelforge.Antenna;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaWave4Tests
{
    // ── Helical — Kraus end-fire formula ─────────────────────────────────

    [Fact]
    public void Helical_DefaultParams_GainInPublishedBand()
    {
        // N=10, C/λ=1, S/λ=0.25 → G = 15·10·1·0.25 = 37.5 → 15.74 dBi.
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Helical, dishDiameter_m: 0.0, wavelength_m: 0.1,
            dishApertureEfficiency: 0.65);
        Assert.InRange(g, 14.0, 18.0);
    }

    [Fact]
    public void Helical_KrausFormula_ExactValue()
    {
        // G = 15 · N · (C/λ)² · (S/λ) for N=10, C/λ=1, S/λ=0.25.
        // G_linear = 37.5 → G_dBi = 10·log10(37.5) ≈ 15.7399 dBi.
        double expected = 10.0 * Math.Log10(15.0 * 10 * 1.0 * 1.0 * 0.25);
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Helical, dishDiameter_m: 0.0, wavelength_m: 0.1,
            dishApertureEfficiency: 0.65,
            helicalTurns: 10, helicalCircumference_rel: 1.0,
            helicalTurnSpacing_rel: 0.25);
        Assert.Equal(expected, g, precision: 10);
    }

    [Fact]
    public void Helical_GainScalesLinearlyWithTurns()
    {
        // Doubling N doubles G_linear → adds 10·log10(2) ≈ 3.01 dB.
        double g10 = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Helical, 0.0, 0.1, 0.65,
            helicalTurns: 10, helicalCircumference_rel: 1.0,
            helicalTurnSpacing_rel: 0.25);
        double g20 = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Helical, 0.0, 0.1, 0.65,
            helicalTurns: 20, helicalCircumference_rel: 1.0,
            helicalTurnSpacing_rel: 0.25);
        Assert.Equal(10.0 * Math.Log10(2.0), g20 - g10, precision: 6);
    }

    [Fact]
    public void Helical_SingleTurn_ProducesFinitePositiveGain()
    {
        // N=1 is the minimum. G = 15·1·1·0.25 = 3.75 → 5.74 dBi.
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Helical, 0.0, 0.1, 0.65,
            helicalTurns: 1, helicalCircumference_rel: 1.0,
            helicalTurnSpacing_rel: 0.25);
        Assert.True(g > 0.0, $"Single-turn helix gain {g:F2} dBi must be > 0.");
        Assert.True(double.IsFinite(g));
    }

    [Fact]
    public void Helical_MoreTurns_HigherGain()
    {
        // More turns always increases gain (monotonic in N).
        double g3  = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Helical, 0.0, 0.1, 0.65, 3);
        double g10 = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Helical, 0.0, 0.1, 0.65, 10);
        double g20 = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Helical, 0.0, 0.1, 0.65, 20);
        Assert.True(g3 < g10 && g10 < g20,
            $"Gain must increase with turns: {g3:F1} < {g10:F1} < {g20:F1} dBi.");
    }

    [Fact]
    public void Helical_IsHigherGainThanYagi_AtTenTurns()
    {
        // A 10-turn Kraus-optimal helix (15.7 dBi) exceeds the Yagi cluster (7 dBi).
        double g_helix = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Helical, 0.0, 0.1, 0.65, 10);
        Assert.True(g_helix > AntennaSolver.YagiUdaGain_dBi,
            $"10-turn helix ({g_helix:F1} dBi) must exceed Yagi ({AntennaSolver.YagiUdaGain_dBi} dBi).");
    }

    // ── Patch antenna ─────────────────────────────────────────────────────

    [Fact]
    public void Patch_GainEqualsClusterCentroid()
    {
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.Patch, dishDiameter_m: 0.0, wavelength_m: 0.19,
            dishApertureEfficiency: 0.65);
        Assert.Equal(AntennaSolver.PatchGain_dBi, g, precision: 10);
    }

    [Fact]
    public void Patch_GainInPublishedRange()
    {
        // Published range for resonant microstrip patch: 6.5–8.5 dBi
        // (Balanis 4e §14.2). The cluster centroid 7.5 dBi must be in range.
        Assert.InRange(AntennaSolver.PatchGain_dBi, 6.5, 8.5);
    }

    [Fact]
    public void Patch_HasHigherGainThanDipole()
    {
        Assert.True(AntennaSolver.PatchGain_dBi > AntennaSolver.HalfWaveDipoleGain_dBi,
            "Patch directional gain must exceed the half-wave dipole.");
    }

    // ── CrossedDipole ─────────────────────────────────────────────────────

    [Fact]
    public void CrossedDipole_GainEqualsHalfWaveDipole()
    {
        double g = AntennaSolver.ComputeAntennaGain_dBi(
            AntennaKind.CrossedDipole, dishDiameter_m: 0.0, wavelength_m: 0.68,
            dishApertureEfficiency: 0.65);
        Assert.Equal(AntennaSolver.CrossedDipoleGain_dBi, g, precision: 10);
        Assert.Equal(AntennaSolver.HalfWaveDipoleGain_dBi, g, precision: 10);
    }

    [Fact]
    public void CrossedDipole_GainIsLessThanYagi()
    {
        Assert.True(AntennaSolver.CrossedDipoleGain_dBi < AntennaSolver.YagiUdaGain_dBi,
            "Crossed dipole must have lower gain than a Yagi array.");
    }

    // ── Solve() — round-trip through all three new topologies ─────────────

    [Fact]
    public void Solve_HelicalBothEnds_ProducesFiniteResult()
    {
        var d = new AntennaLinkDesign(
            TransmitAntennaKind: AntennaKind.Helical,
            ReceiveAntennaKind:  AntennaKind.Helical,
            Frequency_Hz:        437.5e6,   // UHF AMSAT band
            TransmitPower_W:     5.0,
            LinkDistance_m:      600e3,     // 600 km LEO pass
            HelicalTurns:        10);
        var r = AntennaSolver.Solve(d);
        Assert.True(double.IsFinite(r.TransmitAntennaGain_dBi));
        Assert.True(double.IsFinite(r.ReceivedPower_dBm));
        Assert.Equal(r.TransmitAntennaGain_dBi, r.ReceiveAntennaGain_dBi, precision: 10);
    }

    [Fact]
    public void Solve_PatchBothEnds_ProducesExpectedGain()
    {
        var d = new AntennaLinkDesign(
            TransmitAntennaKind: AntennaKind.Patch,
            ReceiveAntennaKind:  AntennaKind.Patch,
            Frequency_Hz:        1.575e9,   // GPS L1
            TransmitPower_W:     50.0,
            LinkDistance_m:      20_200e3); // GPS MEO altitude
        var r = AntennaSolver.Solve(d);
        Assert.Equal(AntennaSolver.PatchGain_dBi, r.TransmitAntennaGain_dBi, precision: 10);
        Assert.Equal(AntennaSolver.PatchGain_dBi, r.ReceiveAntennaGain_dBi,  precision: 10);
    }

    [Fact]
    public void Solve_CrossedDipoleBothEnds_ProducesExpectedGain()
    {
        var d = new AntennaLinkDesign(
            TransmitAntennaKind: AntennaKind.CrossedDipole,
            ReceiveAntennaKind:  AntennaKind.CrossedDipole,
            Frequency_Hz:        137.5e6,  // NOAA APT downlink
            TransmitPower_W:     5.0,
            LinkDistance_m:      800e3);   // typical NOAA-20 range
        var r = AntennaSolver.Solve(d);
        Assert.Equal(AntennaSolver.CrossedDipoleGain_dBi, r.TransmitAntennaGain_dBi, precision: 10);
        Assert.Equal(AntennaSolver.CrossedDipoleGain_dBi, r.ReceiveAntennaGain_dBi,  precision: 10);
    }

    [Fact]
    public void Solve_HelicalMoreTurns_IncreasesReceivedPower()
    {
        // More helix turns → higher gain → higher received power.
        var link3  = UhfCubesatLink(helicalRxTurns: 3);
        var link10 = UhfCubesatLink(helicalRxTurns: 10);
        var r3  = AntennaSolver.Solve(link3);
        var r10 = AntennaSolver.Solve(link10);
        Assert.True(r10.ReceivedPower_dBm > r3.ReceivedPower_dBm,
            $"10-turn Rx ({r10.ReceivedPower_dBm:F1} dBm) must exceed 3-turn ({r3.ReceivedPower_dBm:F1} dBm).");
    }

    [Fact]
    public void Solve_MixedTopology_HelicalTxDishRx_Roundtrip()
    {
        // Helical Tx + Dish Rx (common for LEO ground station with tracking dish).
        var d = new AntennaLinkDesign(
            TransmitAntennaKind:   AntennaKind.Helical,
            ReceiveAntennaKind:    AntennaKind.ParabolicDish,
            Frequency_Hz:          2.4e9,
            TransmitPower_W:       10.0,
            LinkDistance_m:        500e3,
            ReceiveDishDiameter_m: 1.2,
            HelicalTurns:          8);
        var r = AntennaSolver.Solve(d);
        Assert.True(double.IsFinite(r.ReceivedPower_dBm));
        // Dish Rx gain at 2.4 GHz / 1.2 m / η=0.65 ≈ 34 dBi >> helix Tx ≈ 15 dBi.
        Assert.True(r.ReceiveAntennaGain_dBi > r.TransmitAntennaGain_dBi,
            "1.2 m dish Rx should have higher gain than 8-turn helix Tx.");
    }

    // ── ValidateSelf — new helical-field validation ───────────────────────

    [Fact]
    public void ValidateSelf_RejectsZeroHelicalTurns()
    {
        var d = BaseDesign() with { HelicalTurns = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_RejectsNegativeHelicalTurns()
    {
        var d = BaseDesign() with { HelicalTurns = -1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_RejectsZeroCircumferenceRatio()
    {
        var d = BaseDesign() with { HelicalCircumference_rel = 0.0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_RejectsNegativeTurnSpacingRatio()
    {
        var d = BaseDesign() with { HelicalTurnSpacing_rel = -0.1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void ValidateSelf_AcceptsHelicalDefaults()
    {
        var d = new AntennaLinkDesign(
            TransmitAntennaKind: AntennaKind.Helical,
            ReceiveAntennaKind:  AntennaKind.Helical,
            Frequency_Hz:        437.5e6,
            TransmitPower_W:     5.0,
            LinkDistance_m:      600e3);
        d.ValidateSelf(); // must not throw with default helical params
    }

    // ── Backwards-compatibility guard ─────────────────────────────────────

    [Fact]
    public void PreAnt4DesignConstructors_StillCompileAndSolve()
    {
        // Pre-ANT.W4 call sites use only the original fields. The new
        // helical fields have defaults and must not break existing syntax.
        var d = new AntennaLinkDesign(
            TransmitAntennaKind: AntennaKind.HalfWaveDipole,
            ReceiveAntennaKind:  AntennaKind.ParabolicDish,
            Frequency_Hz:        8.4e9,
            TransmitPower_W:     20.0,
            LinkDistance_m:      1.496e11,
            ReceiveDishDiameter_m: 34.0);
        var r = AntennaSolver.Solve(d);
        // Wave-1 outputs still in band.
        Assert.InRange(r.FreeSpacePathLoss_dB, 273.0, 277.0);
        Assert.True(double.IsFinite(r.ReceivedPower_dBm));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // UHF cubesat uplink: 437.5 MHz, 5 W, 600 km LEO, 10-turn helical
    // ground-station Tx, N-turn helical cubesat Rx. BPSK LDPC R-1/2.
    private static AntennaLinkDesign UhfCubesatLink(int helicalRxTurns) => new(
        TransmitAntennaKind: AntennaKind.Helical,
        ReceiveAntennaKind:  AntennaKind.Helical,
        Frequency_Hz:        437.5e6,
        TransmitPower_W:     5.0,
        LinkDistance_m:      600e3,
        Modulation:          ModulationScheme.BpskLdpcR12,
        BandwidthOccupancy_Hz: 9600.0,
        HelicalTurns:        helicalRxTurns);

    // Minimal design for ValidateSelf tests.
    private static AntennaLinkDesign BaseDesign() => new(
        TransmitAntennaKind: AntennaKind.IdealIsotropic,
        ReceiveAntennaKind:  AntennaKind.IdealIsotropic,
        Frequency_Hz:        1e9,
        TransmitPower_W:     1.0,
        LinkDistance_m:      1e5);
}
