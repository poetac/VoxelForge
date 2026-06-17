// AcousticDamperTests — pure-data + persistence + gate coverage for
// the OOB-6 (#200) acoustic-damper module. Sprint B-3 (2026-04-30).
//
// Voxel-build verification deliberately lives elsewhere (subprocess-
// driven, gated on the StlExporter exe being on disk; CLAUDE.md
// pitfall #8). This file pins the closed-form physics + the data
// layer (RegenChamberDesign / DesignPersistence v23 → v24 round trip)
// + the two advisory gates' firing behaviour.

using System.IO;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Xunit;

namespace Voxelforge.Tests;

public class AcousticDamperTests
{
    // ── Closed-form physics ──────────────────────────────────────────

    [Fact]
    public void Helmholtz_ResonanceFrequency_MatchesTextbookExample()
    {
        // Standard 1-litre bottle Helmholtz example. V = 1.0 L (1e-3 m³),
        // neck Ø = 2 cm → A = π·0.01² ≈ 3.14e-4 m², neck L = 4 cm
        // (0.04 m), sea-level air c = 343 m/s. Closed form (with the
        // 0.85·r unflanged end correction → L_eff = 0.04 + 0.0085 ≈
        // 0.0485 m) gives f₀ ≈ 139 Hz — matches the consensus textbook
        // result for the standard "wine bottle" demonstration. Tolerance
        // ±5 Hz covers float rounding in the sqrt path.
        double f0 = AcousticDamper.HelmholtzFrequency_Hz(
            soundSpeed_ms:    343.0,
            neckArea_m2:      3.14e-4,
            neckLength_m:     0.04,
            cavityVolume_m3:  1.0e-3);
        Assert.InRange(f0, 134.0, 144.0);
    }

    [Fact]
    public void QuarterWave_ResonanceFrequency_MatchesClosedForm()
    {
        // Closed pipe one end open: f₀ = c / (4·L). L = 0.10 m, c =
        // 343 m/s → f₀ = 857.5 Hz. Six-sig-fig accuracy expected
        // because there's no end correction in the open-closed form.
        double f0 = AcousticDamper.QuarterWaveFrequency_Hz(
            soundSpeed_ms:  343.0,
            cavityLength_m: 0.10);
        Assert.Equal(857.5, f0, precision: 1);
    }

    [Theory]
    [InlineData(0.0, 343.0, 0.10)]   // zero soundSpeed
    [InlineData(343.0, 0.0, 0.10)]   // zero neck area
    [InlineData(343.0, 1e-4, 0.0)]   // zero cavity volume
    public void Helmholtz_DegenerateInputs_Yield_Zero(double c, double A, double V)
    {
        Assert.Equal(0.0, AcousticDamper.HelmholtzFrequency_Hz(c, A, 0.04, V));
    }

    [Fact]
    public void DampingRatio_IsPeak_AtPerfectTune()
    {
        // f_d = f_m, single resonator → Δζ = peak * envelope(0) * √1 = peak.
        // Use exact equality since envelope(0) = 1.
        double dz = AcousticDamper.DampingRatioForMode(
            damperFreq_Hz: 5000.0, modeFreq_Hz: 5000.0, resonatorCount: 1);
        Assert.Equal(AcousticDamper.PeakDampingRatio_PerResonator, dz, precision: 6);
    }

    [Fact]
    public void DampingRatio_ScalesAsSqrtN_UpToCap()
    {
        double single = AcousticDamper.DampingRatioForMode(5000, 5000, 1);
        double four   = AcousticDamper.DampingRatioForMode(5000, 5000, 4);
        // √4 / √1 = 2. Tolerance covers float rounding in the sqrt path.
        Assert.Equal(2.0, four / single, precision: 6);

        // Beyond the cap (8), Δζ saturates: Δζ(N=16) == Δζ(N=8).
        double eight   = AcousticDamper.DampingRatioForMode(5000, 5000, 8);
        double sixteen = AcousticDamper.DampingRatioForMode(5000, 5000, 16);
        Assert.Equal(eight, sixteen, precision: 6);
    }

    [Fact]
    public void DampingRatio_FallsOff_OffResonance()
    {
        // 20 % detune at Q = 15 → Lorentzian envelope ≈
        // 1 / (1 + 15² · 0.2²) = 1 / 10 ≈ 0.10
        double tuned   = AcousticDamper.DampingRatioForMode(5000, 5000, 1);
        double detuned = AcousticDamper.DampingRatioForMode(6000, 5000, 1);
        double ratio   = detuned / tuned;
        Assert.InRange(ratio, 0.08, 0.12);
    }

    [Fact]
    public void Evaluate_NonHelmholtzNoneConfig_ReturnsNull()
    {
        var screech = new ScreechModeResult(
            SoundSpeed_ms: 1100, L1_Hz: 5000, T1_Hz: 7500, T2_Hz: 12000);
        var none = new AcousticDamperConfig(
            Type: AcousticDamperType.None,
            Count: 8,
            NeckArea_mm2: 30, NeckLength_mm: 6, CavityVolume_mm3: 1500,
            QuarterWaveLength_mm: 0, QuarterWaveDiameter_mm: 0);
        Assert.Null(AcousticDamper.Evaluate(none, screech));
    }

    [Fact]
    public void Evaluate_TunedHelmholtz_HitsTunedFlag()
    {
        // Closed-form solve for a Helmholtz tuned to T1 = 7500 Hz at
        // c = 1100 m/s, with V = 1500 mm³ + L_neck = 6 mm:
        //   A/(V·L_eff) = (2π · 7500 / 1100)² ≈ 1837 m⁻¹
        //   L_eff = 0.006 + 0.85·√(A/π); iterate → A ≈ 24 mm²
        //   gives f₀ ≈ 7530 Hz — squarely in the ±10 % T1 band.
        var screech = new ScreechModeResult(
            SoundSpeed_ms: 1100, L1_Hz: 5000, T1_Hz: 7500, T2_Hz: 12000);
        var config = AcousticDamperConfig.Helmholtz(
            count: 8, neckArea_mm2: 24, neckLength_mm: 6, cavityVolume_mm3: 1500);
        var result = AcousticDamper.Evaluate(config, screech);
        Assert.NotNull(result);
        Assert.Equal(AcousticDamperType.Helmholtz, result!.Type);
        Assert.True(result.IsTunedToAnyMode,
            $"f₀ = {result.ResonanceFrequency_Hz:F0} Hz must land in tuning band of L1/T1/T2 ({screech.L1_Hz:F0}, {screech.T1_Hz:F0}, {screech.T2_Hz:F0}).");
        Assert.True(result.DampingRatio_T1 > 0.01,
            $"Tuned T1 damping must be > 0.01 (got {result.DampingRatio_T1}).");
    }

    [Fact]
    public void Evaluate_DetunedHelmholtz_FlagsNotTuned()
    {
        var screech = new ScreechModeResult(
            SoundSpeed_ms: 1100, L1_Hz: 1000, T1_Hz: 2000, T2_Hz: 3000);
        // Way off — tiny cavity so f₀ shoots into 20+ kHz range, far
        // above any chamber mode. Pick A_neck large + V tiny.
        var config = AcousticDamperConfig.Helmholtz(
            count: 8, neckArea_mm2: 100, neckLength_mm: 2, cavityVolume_mm3: 50);
        var result = AcousticDamper.Evaluate(config, screech);
        Assert.NotNull(result);
        Assert.False(result!.IsTunedToAnyMode);
    }

    [Fact]
    public void TotalVolume_HelmholtzAggregatesCavityPlusNeck()
    {
        var config = AcousticDamperConfig.Helmholtz(
            count: 4, neckArea_mm2: 30, neckLength_mm: 6, cavityVolume_mm3: 1500);
        // Per-resonator = 1500 + 30·6 = 1680 mm³; ×4 = 6720 mm³.
        Assert.Equal(6720.0, config.TotalVolume_mm3, precision: 3);
    }

    [Fact]
    public void TotalVolume_QuarterWaveIsCylinder()
    {
        var config = AcousticDamperConfig.QuarterWave(count: 4, length_mm: 20, diameter_mm: 4);
        // π·(2)²·20 = 80π mm³ ≈ 251.327; ×4 = ~1005.31.
        double expected = 4 * System.Math.PI * 2.0 * 2.0 * 20.0;
        Assert.Equal(expected, config.TotalVolume_mm3, precision: 3);
    }

    // ── Data layer (RegenChamberDesign defaults + persistence) ───────

    [Fact]
    public void RegenChamberDesign_DamperDefaultsOff()
    {
        var d = new RegenChamberDesign();
        // Legacy behaviour: no damper unless explicitly opted in.
        Assert.Equal(AcousticDamperType.None, d.DamperType);
        Assert.Equal(8,      d.DamperCount);
        Assert.Equal(30.0,   d.HelmholtzNeckArea_mm2);
        Assert.Equal(6.0,    d.HelmholtzNeckLength_mm);
        Assert.Equal(1500.0, d.HelmholtzCavityVolume_mm3);
        Assert.Equal(20.0,   d.QuarterWaveLength_mm);
        Assert.Equal(4.0,    d.QuarterWaveDiameter_mm);
    }

    [Fact]
    public void DesignPersistence_RoundTripsDamperFields()
    {
        var cond = new OperatingConditions
        {
            Thrust_N = 5_000, ChamberPressure_Pa = 6.9e6, MixtureRatio = 3.3,
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var d = new RegenChamberDesign
        {
            DamperType                  = AcousticDamperType.Helmholtz,
            DamperCount                 = 6,
            HelmholtzNeckArea_mm2       = 45.0,
            HelmholtzNeckLength_mm      = 5.0,
            HelmholtzCavityVolume_mm3   = 2000.0,
            QuarterWaveLength_mm        = 25.0,
            QuarterWaveDiameter_mm      = 3.5,
        };

        using var tmp = TestTempFile.WithUniqueName("damper-roundtrip", "json");
        DesignPersistence.Save(tmp.Path, cond, d, r: null);
        var loaded = DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        var ld = loaded.Design!;
        Assert.Equal(AcousticDamperType.Helmholtz, ld.DamperType);
        Assert.Equal(6,      ld.DamperCount);
        Assert.Equal(45.0,   ld.HelmholtzNeckArea_mm2);
        Assert.Equal(5.0,    ld.HelmholtzNeckLength_mm);
        Assert.Equal(2000.0, ld.HelmholtzCavityVolume_mm3);
        Assert.Equal(25.0,   ld.QuarterWaveLength_mm);
        Assert.Equal(3.5,    ld.QuarterWaveDiameter_mm);
    }

    [Fact]
    public void DesignPersistence_PreV24File_LoadsWithDamperDefaults()
    {
        // Legacy v23 input must climb the migration chain to v24 with
        // damper fields at their C# init-only defaults.
        const string v23Json = """
            {
              "Schema": "v23",
              "Version": "1.0",
              "Conditions": {
                "Thrust_N": 5000,
                "ChamberPressure_Pa": 6900000,
                "MixtureRatio": 3.3,
                "WallMaterialIndex": 1
              },
              "Design": { "ChamberRadius_mm": 30, "ThroatRadius_mm": 15 }
            }
            """;

        using var tmp = TestTempFile.WithUniqueName("damper-pre-v24", "json");
        File.WriteAllText(tmp.Path, v23Json);
        var loaded = DesignPersistence.Load(tmp.Path);
        Assert.NotNull(loaded);
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        Assert.Equal(AcousticDamperType.None, loaded.Design!.DamperType);
    }

    // ── Gate registration ───────────────────────────────────────────

    [Fact]
    public void GateRegistry_ContainsBothDamperGates()
    {
        var ids = new System.Collections.Generic.HashSet<string>(
            System.Linq.Enumerable.Select(GateRegistry.All, g => g.Id),
            System.StringComparer.Ordinal);
        Assert.Contains("ACOUSTIC_DAMPER_DETUNED",  ids);
        Assert.Contains("ACOUSTIC_DAMPER_OVERSIZED", ids);
    }

    [Fact]
    public void GateRegistry_DamperGates_AreAdvisorySeverity()
    {
        // Both must be Advisory (not Hard) so SA never rejects on them.
        // The model is empirical and a Hard fail would over-claim.
        var detuned = GateRegistry.ById("ACOUSTIC_DAMPER_DETUNED");
        var oversized = GateRegistry.ById("ACOUSTIC_DAMPER_OVERSIZED");
        Assert.Equal(GateSeverity.Advisory, detuned.Severity);
        Assert.Equal(GateSeverity.Advisory, oversized.Severity);
        Assert.Equal(GateKind.AdvisoryHeuristic, detuned.Kind);
        Assert.Equal(GateKind.AdvisoryHeuristic, oversized.Kind);
    }
}
