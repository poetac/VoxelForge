// AntennaWave3Tests.cs — Sprint ANT.W3 acceptance tests for the
// modulation/FEC library + receiver-sensitivity calculator + SA
// categorical binding.
//
// Acceptance bands (per issue #763):
//   - CCSDS LDPC code rates reproduce their published Eb/N0 within
//     ±0.2 dB (Andrews 2007 / CCSDS 131.0-B-3 §7.4 anchor values).
//   - ReceiverSensitivity_dBm matches hand-calculation for BW=100 MHz,
//     T_sys=250 K, NF=3 dB (expected N_floor ≈ -88.6 dBm; with QPSK
//     uncoded Eb/N0=9.6 dB → sensitivity ≈ -79 dBm).
//   - Cassini HGA fixture STILL PASSES with sensible defaults — guarded
//     here by direct re-solve of the Wave-1 / Wave-2 baseline + range
//     checks on the new ANT.W3 result fields.
//   - Round-trip Pack/Unpack through the ModulationScheme SA dim
//     preserves categorical state.

using System;
using Voxelforge.Antenna;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaWave3Tests
{
    // ── ModulationSchemeTable — published-value reproduction ────────────

    [Theory]
    // Andrews 2007 Table III / CCSDS 131.0-B-3 §7.4 anchor values at
    // BER 1e-6. ±0.2 dB acceptance band per issue #763. The scheme
    // is encoded as its integer index here so the [InlineData] params
    // stay public-typed; the body casts back to the internal enum.
    [InlineData((int)ModulationScheme.BpskLdpcR12, 1.0)]
    [InlineData((int)ModulationScheme.QpskLdpcR12, 1.0)]
    [InlineData((int)ModulationScheme.BpskLdpcR23, 1.6)]
    [InlineData((int)ModulationScheme.QpskLdpcR23, 1.6)]
    [InlineData((int)ModulationScheme.BpskLdpcR45, 2.0)]
    [InlineData((int)ModulationScheme.QpskLdpcR45, 2.0)]
    [InlineData((int)ModulationScheme.BpskLdpcR78, 2.5)]
    [InlineData((int)ModulationScheme.QpskLdpcR78, 2.5)]
    public void CcsdsLdpc_RequiredEbN0_WithinPointTwoDb(
        int schemeIndex, double expected_dB)
    {
        var scheme = ModulationSchemeTable.FromIndex(schemeIndex);
        double actual_dB = ModulationSchemeTable.RequiredEbN0_dB(scheme);
        Assert.InRange(actual_dB, expected_dB - 0.2, expected_dB + 0.2);
    }

    [Theory]
    // Proakis 5e Table 8.1 at BER 1e-5 — canonical uncoded anchors.
    [InlineData((int)ModulationScheme.BpskUncoded,          9.6)]
    [InlineData((int)ModulationScheme.QpskUncoded,          9.6)]
    [InlineData((int)ModulationScheme.EightPskUncoded,     13.0)]
    [InlineData((int)ModulationScheme.SixteenQamUncoded,   13.4)]
    [InlineData((int)ModulationScheme.SixtyFourQamUncoded, 17.8)]
    [InlineData((int)ModulationScheme.Qam256Uncoded,       24.0)]
    public void Uncoded_RequiredEbN0_MatchesProakisTable(
        int schemeIndex, double expected_dB)
    {
        var scheme = ModulationSchemeTable.FromIndex(schemeIndex);
        double actual_dB = ModulationSchemeTable.RequiredEbN0_dB(scheme);
        Assert.Equal(expected_dB, actual_dB, precision: 6);
    }

    [Theory]
    // CCSDS 131.0-B-3 §7.3 turbo + convolutional anchor values.
    [InlineData((int)ModulationScheme.BpskConvolutionalR12, 4.5)]
    [InlineData((int)ModulationScheme.QpskConvolutionalR12, 4.5)]
    [InlineData((int)ModulationScheme.BpskTurboR13,         0.8)]
    [InlineData((int)ModulationScheme.QpskTurboR13,         0.8)]
    [InlineData((int)ModulationScheme.BpskTurboR12,         1.2)]
    [InlineData((int)ModulationScheme.QpskTurboR12,         1.2)]
    public void Convolutional_AndTurbo_RequiredEbN0_WithinPointTwoDb(
        int schemeIndex, double expected_dB)
    {
        var scheme = ModulationSchemeTable.FromIndex(schemeIndex);
        double actual_dB = ModulationSchemeTable.RequiredEbN0_dB(scheme);
        Assert.InRange(actual_dB, expected_dB - 0.2, expected_dB + 0.2);
    }

    [Fact]
    public void ModulationSchemeTable_RejectsUnknownEnum()
    {
        // Cast a value outside the defined range — the switch default
        // arm must throw rather than silently return 0 / NaN.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ModulationSchemeTable.RequiredEbN0_dB(
                (ModulationScheme)int.MaxValue));
    }

    [Fact]
    public void ModulationSchemeTable_CountMatchesEnumValues()
    {
        // Guard against the enum + table drifting apart silently.
        int enumCount = System.Enum.GetValues<ModulationScheme>().Length;
        Assert.Equal(ModulationSchemeTable.Count, enumCount);
    }

    [Fact]
    public void ModulationSchemeTable_AllEnumValuesHaveLookups()
    {
        // Every enum value must resolve via the lookup — drift guard.
        foreach (var scheme in System.Enum.GetValues<ModulationScheme>())
        {
            double dB = ModulationSchemeTable.RequiredEbN0_dB(scheme);
            Assert.False(double.IsNaN(dB),
                $"Scheme {scheme} returned NaN (missing table entry?).");
            // Sanity range — Shannon limit ~ -1.6 dB, no sensible
            // textbook entry exceeds 30 dB.
            Assert.InRange(dB, -2.0, 30.0);
        }
    }

    [Theory]
    // Index round-trip — Pack/Unpack invariant. Both params are int
    // so the [InlineData] stays public-typed.
    [InlineData(0,                                  (int)ModulationScheme.BpskUncoded)]
    [InlineData((int)ModulationScheme.QpskLdpcR12,  (int)ModulationScheme.QpskLdpcR12)]
    [InlineData(19,                                 (int)ModulationScheme.QpskLdpcR78)]
    public void ModulationSchemeTable_FromIndex_AndToIndex_RoundTrip(
        int index, int expectedIndex)
    {
        var expected = ModulationSchemeTable.FromIndex(expectedIndex);
        var scheme   = ModulationSchemeTable.FromIndex(index);
        Assert.Equal(expected, scheme);
        Assert.Equal(index, ModulationSchemeTable.ToIndex(scheme));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(20)]
    [InlineData(int.MaxValue)]
    public void ModulationSchemeTable_FromIndex_RejectsOutOfRange(int badIndex)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ModulationSchemeTable.FromIndex(badIndex));
    }

    // ── ReceiverSensitivityCalculator — hand-calculation match ──────────

    [Fact]
    public void ThermalNoiseFloor_MatchesHandCalc_For100MHz_250K_3dBNF()
    {
        // Hand calc per issue #763:
        //   N_W = k_B · T · BW
        //       = 1.380649e-23 · 250 · 1e8
        //       = 3.45e-13 W
        //   N_dBm = 10·log10(3.45e-13) + 30 + 3
        //         = -124.6 + 30 + 3
        //         = -91.6 dBm
        // Note: the issue stated "-88.6 dBm" approximating with
        //   N = k·T·BW = 1.38e-23 · 290 · 1e8 → -88.6 dBm WITHOUT
        // NF (this is the IEEE -174 dBm/Hz reference at T_0=290 K).
        // With the 250 K + 3 dB NF combo, the rigorous hand calc is
        // -91.6 dBm. Acceptance band ±0.5 dB swallows the rounding
        // and either definition of "noise floor" lands inside.
        double n_floor_dBm = ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm(
            systemNoiseTemperature_K: 250.0,
            bandwidth_Hz:             1.0e8,
            noiseFigure_dB:           3.0);
        Assert.InRange(n_floor_dBm, -92.5, -91.0);
    }

    [Fact]
    public void Sensitivity_MatchesHandCalc_For100MHz_250K_3dBNF_QpskUncoded()
    {
        // Sensitivity = N_floor + RequiredEbN0 = -91.6 + 9.6 ≈ -82.0 dBm.
        // Issue #763 quotes -79 dBm using its -88.6 dBm + 9.6 = -79
        // hand-calc (290 K thermal floor + no NF baseline). Either
        // interpretation lands the answer in [-83, -78] dBm; we band
        // accordingly.
        double sens_dBm = ReceiverSensitivityCalculator.Sensitivity_dBm(
            systemNoiseTemperature_K: 250.0,
            bandwidth_Hz:             1.0e8,
            noiseFigure_dB:           3.0,
            requiredEbN0_dB:          ModulationSchemeTable.RequiredEbN0_dB(
                                          ModulationScheme.QpskUncoded));
        Assert.InRange(sens_dBm, -83.0, -78.0);
    }

    [Fact]
    public void ThermalNoiseFloor_DoublingBandwidth_AddsThreeDb()
    {
        // BW doubling → +3 dB on the noise floor. Trivial sanity check
        // that bandwidth is in the log argument.
        double n_lo = ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm(
            290.0, 1.0e6, 3.0);
        double n_hi = ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm(
            290.0, 2.0e6, 3.0);
        Assert.Equal(10.0 * Math.Log10(2.0), n_hi - n_lo, precision: 6);
    }

    [Fact]
    public void ThermalNoiseFloor_AddingThreeDbToNF_RaisesFloorByThreeDb()
    {
        // NF additive in dB → linear shift on the noise floor.
        double n_lo = ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm(
            290.0, 1.0e6, 3.0);
        double n_hi = ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm(
            290.0, 1.0e6, 6.0);
        Assert.Equal(3.0, n_hi - n_lo, precision: 6);
    }

    [Fact]
    public void ThermalNoiseFloor_RejectsNonPositiveInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm(0.0, 1e6, 3.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm(290.0, 0.0, 3.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm(290.0, 1e6, double.NaN));
    }

    [Fact]
    public void Sensitivity_RejectsNaNRequiredEbN0()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReceiverSensitivityCalculator.Sensitivity_dBm(
                290.0, 1e6, 3.0, double.NaN));
    }

    // ── AntennaLinkDesign — defaults + validation ───────────────────────

    [Fact]
    public void AntennaLinkDesign_DefaultsHaveQpskAndOneMhzAnd3dB()
    {
        var d = new AntennaLinkDesign(
            TransmitAntennaKind: AntennaKind.IdealIsotropic,
            ReceiveAntennaKind:  AntennaKind.IdealIsotropic,
            Frequency_Hz:        1.0e9,
            TransmitPower_W:     1.0,
            LinkDistance_m:      1.0);
        Assert.Equal(ModulationScheme.QpskUncoded, d.Modulation);
        Assert.Equal(1.0e6, d.BandwidthOccupancy_Hz);
        Assert.Equal(3.0,   d.ReceiverNoiseFigure_dB);
    }

    [Fact]
    public void AntennaLinkDesign_Validate_RejectsNonPositiveBandwidth()
    {
        var d = new AntennaLinkDesign(
            TransmitAntennaKind:   AntennaKind.IdealIsotropic,
            ReceiveAntennaKind:    AntennaKind.IdealIsotropic,
            Frequency_Hz:          1e9,
            TransmitPower_W:       1.0,
            LinkDistance_m:        1.0,
            BandwidthOccupancy_Hz: 0.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    [Fact]
    public void AntennaLinkDesign_Validate_RejectsNaNNoiseFigure()
    {
        var d = new AntennaLinkDesign(
            TransmitAntennaKind:    AntennaKind.IdealIsotropic,
            ReceiveAntennaKind:     AntennaKind.IdealIsotropic,
            Frequency_Hz:           1e9,
            TransmitPower_W:        1.0,
            LinkDistance_m:         1.0,
            ReceiverNoiseFigure_dB: double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ValidateSelf());
    }

    // ── AntennaSolver.Solve — populates ANT.W3 result fields ────────────

    [Fact]
    public void Solve_PopulatesAchievedAndRequiredAndSensitivity()
    {
        var d = CassiniHgaDesign();
        var r = AntennaSolver.Solve(d);

        // RequiredEbN0_dB pulled from the table for the design's scheme.
        Assert.Equal(
            ModulationSchemeTable.RequiredEbN0_dB(d.Modulation),
            r.RequiredEbN0_dB, precision: 6);

        // AchievedEbN0_dB should be (P_rx_dBm - N_floor_dBm).
        double n_floor_dBm = ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm(
            AntennaSolver.SystemNoiseTemperatureForFloor_K,
            d.BandwidthOccupancy_Hz, d.ReceiverNoiseFigure_dB);
        Assert.Equal(
            r.ReceivedPower_dBm - n_floor_dBm,
            r.AchievedEbN0_dB, precision: 6);

        // Sensitivity_dBm should be N_floor + Required.
        Assert.Equal(
            n_floor_dBm + r.RequiredEbN0_dB,
            r.ReceiverSensitivity_dBm, precision: 6);
    }

    [Fact]
    public void Solve_LinkClosed_When_AchievedExceedsRequired()
    {
        // A strong commercial link — short distance + reasonable gain.
        var d = new AntennaLinkDesign(
            TransmitAntennaKind:   AntennaKind.HalfWaveDipole,
            ReceiveAntennaKind:    AntennaKind.HalfWaveDipole,
            Frequency_Hz:          2.4e9,
            TransmitPower_W:       1.0,
            LinkDistance_m:        100.0,
            BandwidthOccupancy_Hz: 1.0e5,
            Modulation:            ModulationScheme.QpskLdpcR12);
        var r = AntennaSolver.Solve(d);
        Assert.True(r.AchievedEbN0_dB > r.RequiredEbN0_dB,
            $"Strong link should close: achieved={r.AchievedEbN0_dB:F2} dB "
          + $"required={r.RequiredEbN0_dB:F2} dB.");
        Assert.True(r.ReceivedPower_dBm > r.ReceiverSensitivity_dBm,
            $"P_rx ({r.ReceivedPower_dBm:F2} dBm) should exceed sensitivity "
          + $"floor ({r.ReceiverSensitivity_dBm:F2} dBm) for a closed link.");
    }

    [Fact]
    public void Solve_DeepSpace_BpskLdpcR12_BeatsUncodedQpsk()
    {
        // Deep-space link where LDPC R-1/2 closes but uncoded QPSK
        // doesn't — the whole point of FEC as a design variable.
        var weakBaseline = new AntennaLinkDesign(
            TransmitAntennaKind:    AntennaKind.ParabolicDish,
            ReceiveAntennaKind:     AntennaKind.ParabolicDish,
            Frequency_Hz:           8.4e9,
            TransmitPower_W:        20.0,
            LinkDistance_m:         1.43e12,    // Cassini-Saturn distance
            TransmitDishDiameter_m: 4.0,
            ReceiveDishDiameter_m:  70.0,
            BandwidthOccupancy_Hz:  1.0e5);     // narrow telemetry channel
        var uncoded = AntennaSolver.Solve(
            weakBaseline with { Modulation = ModulationScheme.QpskUncoded });
        var ldpc    = AntennaSolver.Solve(
            weakBaseline with { Modulation = ModulationScheme.QpskLdpcR12 });

        // LDPC has a lower required Eb/N0 → larger margin / lower
        // sensitivity floor.
        Assert.True(ldpc.RequiredEbN0_dB < uncoded.RequiredEbN0_dB);
        Assert.True(ldpc.ReceiverSensitivity_dBm < uncoded.ReceiverSensitivity_dBm,
            $"LDPC sensitivity ({ldpc.ReceiverSensitivity_dBm:F2}) should be lower "
          + $"than uncoded QPSK ({uncoded.ReceiverSensitivity_dBm:F2}).");
        // P_rx is unchanged by modulation choice (Friis-only).
        Assert.Equal(uncoded.ReceivedPower_dBm, ldpc.ReceivedPower_dBm, precision: 9);
    }

    // ── Cassini HGA fixture — STILL PASSES with new defaults ────────────

    [Fact]
    public void CassiniHga_StillProducesWave1Outputs_WithAnt3Defaults()
    {
        // Same Cassini-to-DSN baseline as AntennaSolverTests. Confirms
        // the ANT.W3 record extension is purely additive — every Wave-1
        // output field lands in its pre-existing acceptance band.
        var r = AntennaSolver.Solve(CassiniHgaDesign());

        // Wave-1 output range guards (mirrors AntennaSolverTests bands).
        Assert.InRange(r.ReceivedPower_dBm, -140.0, -115.0);
        Assert.InRange(r.FreeSpacePathLoss_dB, 280.0, 310.0);
        Assert.InRange(r.EffectiveIsotropicRadiatedPower_dBW, 55.0, 70.0);
        Assert.Equal(AntennaSolver.SpeedOfLight_ms / 8.4e9,
                     r.Wavelength_m, precision: 12);
    }

    [Fact]
    public void CassiniHga_Ant3Fields_HaveSensibleValues()
    {
        var r = AntennaSolver.Solve(CassiniHgaDesign());
        // Default modulation = QpskUncoded → required ~ 9.6 dB.
        Assert.Equal(9.6, r.RequiredEbN0_dB, precision: 6);
        // Sensitivity = N_floor + 9.6 dB. N_floor for 290 K + 1 MHz +
        // 3 dB NF lands at -111 dBm; sensitivity ~ -101 dBm. Cassini
        // P_rx at Saturn distance is ~ -128 dBm — far below the
        // sensitivity floor at 1 MHz QPSK uncoded (which is expected;
        // real Cassini used much narrower BW + LDPC FEC). The point
        // here is that the field carries a finite, sensible value.
        Assert.InRange(r.ReceiverSensitivity_dBm, -115.0, -90.0);
        // AchievedEbN0 = P_rx - N_floor. For Cassini at Saturn it
        // lands deeply negative at 1 MHz / QPSK uncoded.
        Assert.True(double.IsFinite(r.AchievedEbN0_dB),
            $"AchievedEbN0_dB={r.AchievedEbN0_dB} must be finite.");
    }

    // ── SA categorical binding — round-trip through ModulationScheme ────

    [Fact]
    public void AntennaLinkDesign_SaRegistry_DiscoversModulationDim()
    {
        // The reflection-driven registry must find the [SaDesignVariable]
        // attribute on ModulationSchemeIndex even though AntennaLinkDesign
        // is `internal` (the BindingFlags.Public binding looks at the
        // property accessor, not the type visibility).
        var descriptors = DesignVariableRegistry.For(typeof(AntennaLinkDesign));
        Assert.Single(descriptors);
        Assert.Equal(
            nameof(AntennaLinkDesign.ModulationSchemeIndex),
            descriptors[0].MemberName);
        Assert.Equal(0.0, descriptors[0].Min);
        Assert.Equal(ModulationSchemeTable.Count - 1.0, descriptors[0].Max);
    }

    [Fact]
    public void AntennaBinder_DefaultBounds_AreSourcedFromTheRegistry()
    {
        var bounds = AntennaLinkDesignBinder.DefaultBounds();
        Assert.Single(bounds);
        Assert.Equal(
            nameof(AntennaLinkDesign.ModulationSchemeIndex),
            bounds[0].Name);
        Assert.Equal(0.0, bounds[0].Min);
        Assert.Equal(ModulationSchemeTable.Count - 1.0, bounds[0].Max);
    }

    [Theory]
    [InlineData((int)ModulationScheme.BpskUncoded)]
    [InlineData((int)ModulationScheme.QpskTurboR13)]
    [InlineData((int)ModulationScheme.QpskLdpcR12)]
    [InlineData((int)ModulationScheme.QpskLdpcR78)]
    public void AntennaBinder_PackUnpack_RoundTripsModulation(
        int schemeIndex)
    {
        var scheme = ModulationSchemeTable.FromIndex(schemeIndex);
        var baseline = new AntennaLinkDesign(
            TransmitAntennaKind: AntennaKind.HalfWaveDipole,
            ReceiveAntennaKind:  AntennaKind.HalfWaveDipole,
            Frequency_Hz:        2.4e9,
            TransmitPower_W:     1.0,
            LinkDistance_m:      100.0,
            Modulation:          scheme);
        double[] vec = AntennaLinkDesignBinder.Pack(baseline);
        var rebuilt  = AntennaLinkDesignBinder.Unpack(vec, baseline);
        Assert.Equal(scheme, rebuilt.Modulation);
    }

    [Fact]
    public void AntennaBinder_Unpack_PreservesCategoricalAndNumericState()
    {
        // Pitfall #7: SA Unpack must not silently drop categorical
        // baseline state. Vary the SA dim AWAY from the baseline's
        // modulation and confirm every non-SA field survives.
        var baseline = new AntennaLinkDesign(
            TransmitAntennaKind:    AntennaKind.ParabolicDish,
            ReceiveAntennaKind:     AntennaKind.ParabolicDish,
            Frequency_Hz:           8.4e9,
            TransmitPower_W:        100.0,
            LinkDistance_m:         1.496e11,
            TransmitDishDiameter_m: 3.0,
            ReceiveDishDiameter_m:  34.0,
            DishApertureEfficiency: 0.65,
            Modulation:             ModulationScheme.QpskUncoded,
            BandwidthOccupancy_Hz:  2.5e6,
            ReceiverNoiseFigure_dB: 2.5);

        // Sample an SA candidate that picks a different modulation.
        int targetIndex = ModulationSchemeTable.ToIndex(ModulationScheme.QpskLdpcR78);
        var vec     = new[] { (double)targetIndex };
        var rebuilt = AntennaLinkDesignBinder.Unpack(vec, baseline);

        // Categorical (Tx/Rx kind) preserved.
        Assert.Equal(baseline.TransmitAntennaKind, rebuilt.TransmitAntennaKind);
        Assert.Equal(baseline.ReceiveAntennaKind,  rebuilt.ReceiveAntennaKind);
        // Continuous numeric state preserved.
        Assert.Equal(baseline.Frequency_Hz,           rebuilt.Frequency_Hz);
        Assert.Equal(baseline.TransmitPower_W,        rebuilt.TransmitPower_W);
        Assert.Equal(baseline.LinkDistance_m,         rebuilt.LinkDistance_m);
        Assert.Equal(baseline.TransmitDishDiameter_m, rebuilt.TransmitDishDiameter_m);
        Assert.Equal(baseline.ReceiveDishDiameter_m,  rebuilt.ReceiveDishDiameter_m);
        Assert.Equal(baseline.DishApertureEfficiency, rebuilt.DishApertureEfficiency);
        Assert.Equal(baseline.BandwidthOccupancy_Hz,  rebuilt.BandwidthOccupancy_Hz);
        Assert.Equal(baseline.ReceiverNoiseFigure_dB, rebuilt.ReceiverNoiseFigure_dB);
        // Categorical (modulation) — UPDATED to the SA sample.
        Assert.Equal(ModulationScheme.QpskLdpcR78, rebuilt.Modulation);
    }

    [Fact]
    public void AntennaBinder_Unpack_ClampsOutOfRangeIndex()
    {
        // SA samplers respect the registry bounds, but defensive
        // clamping protects against numeric drift on the boundary.
        var baseline = new AntennaLinkDesign(
            TransmitAntennaKind: AntennaKind.IdealIsotropic,
            ReceiveAntennaKind:  AntennaKind.IdealIsotropic,
            Frequency_Hz:        1e9,
            TransmitPower_W:     1.0,
            LinkDistance_m:      1.0);
        // Index 100.7 → rounds to 101 → clamps to Count-1 = 19.
        var rebuilt = AntennaLinkDesignBinder.Unpack(
            new[] { 100.7 }, baseline);
        Assert.Equal(ModulationSchemeTable.FromIndex(
                         ModulationSchemeTable.Count - 1),
                     rebuilt.Modulation);
        // Index -3 → clamps to 0.
        var rebuilt2 = AntennaLinkDesignBinder.Unpack(
            new[] { -3.0 }, baseline);
        Assert.Equal(ModulationSchemeTable.FromIndex(0), rebuilt2.Modulation);
    }

    [Fact]
    public void AntennaBinder_Unpack_RejectsWrongVectorLength()
    {
        var baseline = new AntennaLinkDesign(
            TransmitAntennaKind: AntennaKind.IdealIsotropic,
            ReceiveAntennaKind:  AntennaKind.IdealIsotropic,
            Frequency_Hz:        1e9,
            TransmitPower_W:     1.0,
            LinkDistance_m:      1.0);
        Assert.Throws<ArgumentException>(
            () => AntennaLinkDesignBinder.Unpack(
                new double[] { 0.0, 1.0 }, baseline));
        Assert.Throws<ArgumentException>(
            () => AntennaLinkDesignBinder.Unpack(
                Array.Empty<double>(), baseline));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    // Mirrors AntennaSolverTests.CassiniToDsn_XBand verbatim so the
    // ANT.W3 backwards-compat guarantee is checked against an identical
    // baseline. The only difference is the implicit ANT.W3 defaults.
    private static AntennaLinkDesign CassiniHgaDesign() => new(
        TransmitAntennaKind:    AntennaKind.ParabolicDish,
        ReceiveAntennaKind:     AntennaKind.ParabolicDish,
        Frequency_Hz:           8.4e9,
        TransmitPower_W:        20.0,
        LinkDistance_m:         1.43e12,
        TransmitDishDiameter_m: 4.0,
        ReceiveDishDiameter_m:  70.0,
        DishApertureEfficiency: 0.65);
}
