// Su2ConfigWriterTests.cs — Unit tests for Su2ConfigWriter.

using System;
using System.Globalization;
using System.IO;
using Voxelforge.Cfd;
using Voxelforge.Cfd.Config;
using Voxelforge.Cfd.Mesh;
using Voxelforge.Combustion;
using Xunit;

namespace Voxelforge.Cfd.Tests.Config;

public sealed class Su2ConfigWriterTests : IDisposable
{
    private static readonly PropellantState TestGas = new(
        MixtureRatio:       3.5,
        ChamberPressure_Pa: 5_000_000,
        ChamberTemp_K:      3400,
        GammaChamber:       1.20,
        GammaThroat:        1.20,
        MolecularWeight:    21.5,
        SpecificGasConst:   8314.462618 / 21.5,
        Cp_Jkg:             2500,
        Viscosity_PaS:      8.5e-5,
        Prandtl:            0.72,
        CStar_ms:           1800,
        IspVacuum_s:        360,
        PropellantName:     "LOX/CH4");

    private readonly string _tempFile;
    private readonly string _content;

    public Su2ConfigWriterTests()
    {
        _tempFile = Path.GetTempFileName();
        var inputs = new Su2ConfigInputs(
            Gas:             TestGas,
            MeshFilePath:    "/tmp/nozzle.su2",
            OutputDirectory: "/tmp",
            Density:         Su2MeshDensity.Coarse);
        Su2ConfigWriter.Write(_tempFile, inputs);
        _content = File.ReadAllText(_tempFile);
    }

    public void Dispose() => File.Delete(_tempFile);

    [Fact]
    public void Write_ContainsAxisymmetric()
        => Assert.Contains("AXISYMMETRIC= YES", _content);

    [Fact]
    public void Write_SolverIsRansWithSst()
    {
        Assert.Contains("SOLVER= RANS", _content);
        Assert.Contains("KIND_TURB_MODEL= SST", _content);
    }

    [Fact]
    public void Write_GasConstantApproxCorrect()
    {
        double expected = 8314.462618 / 21.5;
        foreach (string line in _content.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("GAS_CONSTANT=", StringComparison.Ordinal))
                continue;

            string val = trimmed.Substring("GAS_CONSTANT=".Length).Trim();
            double actual = double.Parse(val, CultureInfo.InvariantCulture);
            Assert.InRange(actual, expected * 0.999, expected * 1.001);
            return;
        }
        Assert.Fail("GAS_CONSTANT= not found in config file.");
    }

    [Fact]
    public void Write_AdiabaticWallBc()
        => Assert.Contains("MARKER_HEATFLUX= ( wall, 0.0 )", _content);

    [Fact]
    public void Write_SupersonicOutlet()
        => Assert.Contains("MARKER_SUPERSONIC_OUTLET= ( outlet )", _content);

    [Fact]
    public void Write_CoarseDensity_Has500Iterations()
        => Assert.Contains("ITER= 500", _content);

    [Fact]
    public void SutherlandConstant_FromBartzSlope_AtLoxCh4Chamber_MatchesTOver9()
    {
        // Sprint C.2: S derived to match Bartz μ∝T^0.6 slope at T_ref = T_chamber.
        // For T_chamber = 3400 K → S ≈ 3400/9 ≈ 377.78 K.
        double s = Su2ConfigWriter.SutherlandConstantFromBartzSlope(3400.0);
        Assert.Equal(3400.0 / 9.0, s, precision: 6);

        // Cross-check: differentiating Sutherland's law at T_ref gives slope
        // (1.5 - T/(T+S)) which should equal 0.6 at the Bartz anchor.
        double slopeAtAnchor = 1.5 - 3400.0 / (3400.0 + s);
        Assert.InRange(slopeAtAnchor, 0.599, 0.601);
    }

    [Fact]
    public void SutherlandConstant_FromBartzSlope_DegenerateInputs_FallsBackToAirBaseline()
    {
        Assert.Equal(110.4, Su2ConfigWriter.SutherlandConstantFromBartzSlope(0.0));
        Assert.Equal(110.4, Su2ConfigWriter.SutherlandConstantFromBartzSlope(-100.0));
        Assert.Equal(110.4, Su2ConfigWriter.SutherlandConstantFromBartzSlope(double.NaN));
        Assert.Equal(110.4, Su2ConfigWriter.SutherlandConstantFromBartzSlope(double.PositiveInfinity));
    }

    [Fact]
    public void Write_SutherlandConstant_UsesBartzSlopeDerivation()
    {
        // Confirm the SUTHERLAND_CONSTANT line in the emitted config matches T_c/9
        // (derived in Sprint C.2; pre-C.2 used 0.5·T_c).
        double expected = 3400.0 / 9.0;
        foreach (string line in _content.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("SUTHERLAND_CONSTANT=", StringComparison.Ordinal))
                continue;

            string val = trimmed.Substring("SUTHERLAND_CONSTANT=".Length).Trim();
            double actual = double.Parse(val, CultureInfo.InvariantCulture);
            Assert.InRange(actual, expected * 0.999, expected * 1.001);
            return;
        }
        Assert.Fail("SUTHERLAND_CONSTANT= not found in config file.");
    }

    // ── Sprint C.3 tests ──────────────────────────────────────────────────────

    [Fact]
    public void Write_WithPolynomialCp_EmitsEffectiveGamma()
    {
        // (a) When a non-flat CpPolynomialResult is supplied, GAMMA_VALUE must reflect
        //     GammaEffective rather than the gas's frozen GammaChamber.
        var poly = new CpPolynomialResult(
            Coefficients:    new double[] { 2600.0, 0.05, 0.0, 0.0, 0.0 },
            GammaEffective:  1.19,
            IsFlatCp:        false);

        string tmp = Path.GetTempFileName();
        try
        {
            var inputs = new Su2ConfigInputs(
                Gas:             TestGas,
                MeshFilePath:    "/tmp/nozzle.su2",
                OutputDirectory: "/tmp",
                Density:         Su2MeshDensity.Coarse,
                PolynomialCp:    poly);
            Su2ConfigWriter.Write(tmp, inputs);
            string content = File.ReadAllText(tmp);

            foreach (string line in content.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("GAMMA_VALUE=", StringComparison.Ordinal)) continue;

                string val = trimmed["GAMMA_VALUE=".Length..].Trim();
                double actual = double.Parse(val, CultureInfo.InvariantCulture);
                Assert.InRange(actual, 1.19 * 0.999, 1.19 * 1.001);
                // Must differ from the frozen chamber γ (1.20)
                Assert.NotEqual(TestGas.GammaChamber, actual, 3);
                return;
            }
            Assert.Fail("GAMMA_VALUE= not found in config.");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Write_WithPolynomialCp_CoefficientsRoundTripViaCfg()
    {
        // (b) The polynomial coefficients emitted as CP_POLYCOEFFS= must parse back to
        //     the same values supplied in CpPolynomialResult.Coefficients.
        double[] expectedCoeffs = { 2650.0, 0.08, -1e-5, 2e-9, 0.0 };
        var poly = new CpPolynomialResult(
            Coefficients:    expectedCoeffs,
            GammaEffective:  1.185,
            IsFlatCp:        false);

        string tmp = Path.GetTempFileName();
        try
        {
            var inputs = new Su2ConfigInputs(
                Gas:             TestGas,
                MeshFilePath:    "/tmp/nozzle.su2",
                OutputDirectory: "/tmp",
                Density:         Su2MeshDensity.Coarse,
                PolynomialCp:    poly);
            Su2ConfigWriter.Write(tmp, inputs);
            string content = File.ReadAllText(tmp);

            foreach (string line in content.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("CP_POLYCOEFFS=", StringComparison.Ordinal)) continue;

                // Parse "(b0, b1, b2, b3, b4)" — strip parens, split on comma
                string inner = trimmed["CP_POLYCOEFFS=".Length..].Trim().Trim('(', ')');
                string[] parts = inner.Split(',');
                Assert.Equal(5, parts.Length);
                for (int i = 0; i < 5; i++)
                {
                    double parsed = double.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
                    Assert.InRange(parsed,
                        expectedCoeffs[i] - Math.Abs(expectedCoeffs[i]) * 1e-7 - 1e-12,
                        expectedCoeffs[i] + Math.Abs(expectedCoeffs[i]) * 1e-7 + 1e-12);
                }
                return;
            }
            Assert.Fail("CP_POLYCOEFFS= not found in config.");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Write_WithFlatCp_FallsBackToIdealGas()
    {
        // (c) A flat (IsFlatCp=true) polynomial result must behave identically to null —
        //     GAMMA_VALUE stays at gas.GammaChamber and no CP_POLYCOEFFS line is emitted.
        var flatPoly = new CpPolynomialResult(
            Coefficients:    new double[] { TestGas.Cp_Jkg, 0, 0, 0, 0 },
            GammaEffective:  TestGas.GammaChamber,
            IsFlatCp:        true);

        string tmp = Path.GetTempFileName();
        try
        {
            var inputs = new Su2ConfigInputs(
                Gas:             TestGas,
                MeshFilePath:    "/tmp/nozzle.su2",
                OutputDirectory: "/tmp",
                Density:         Su2MeshDensity.Coarse,
                PolynomialCp:    flatPoly);
            Su2ConfigWriter.Write(tmp, inputs);
            string content = File.ReadAllText(tmp);

            Assert.Contains("FLUID_MODEL= IDEAL_GAS", content);
            Assert.DoesNotContain("CP_POLYCOEFFS=", content);

            foreach (string line in content.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("GAMMA_VALUE=", StringComparison.Ordinal)) continue;

                string val = trimmed["GAMMA_VALUE=".Length..].Trim();
                double actual = double.Parse(val, CultureInfo.InvariantCulture);
                Assert.InRange(actual, TestGas.GammaChamber * 0.999, TestGas.GammaChamber * 1.001);
                return;
            }
            Assert.Fail("GAMMA_VALUE= not found in config.");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Write_CpModelFrozenGamma_SuppressesPolynomialPath()
    {
        // (d) CpModel.FrozenGamma suppresses CP_POLYCOEFFS and forces GAMMA_VALUE =
        //     GammaChamber even when a non-flat CpPolynomialResult is supplied.
        var poly = new CpPolynomialResult(
            Coefficients:   new double[] { 2600.0, 0.05, 0.0, 0.0, 0.0 },
            GammaEffective: 1.19,
            IsFlatCp:       false);

        string tmp = Path.GetTempFileName();
        try
        {
            Su2ConfigWriter.Write(tmp, new Su2ConfigInputs(
                Gas:             TestGas,
                MeshFilePath:    "/tmp/nozzle.su2",
                OutputDirectory: "/tmp",
                Density:         Su2MeshDensity.Coarse,
                PolynomialCp:    poly,
                CpModel:         CpModel.FrozenGamma));
            string content = File.ReadAllText(tmp);

            Assert.DoesNotContain("CP_POLYCOEFFS=", content);
            foreach (string line in content.Split('\n'))
            {
                if (!line.TrimStart().StartsWith("GAMMA_VALUE=", StringComparison.Ordinal)) continue;
                double v = double.Parse(
                    line.TrimStart()["GAMMA_VALUE=".Length..].Trim(),
                    CultureInfo.InvariantCulture);
                Assert.InRange(v, TestGas.GammaChamber * 0.999, TestGas.GammaChamber * 1.001);
                return;
            }
            Assert.Fail("GAMMA_VALUE= not found in config.");
        }
        finally { File.Delete(tmp); }
    }

    // ── Sprint C.2 follow-on tests (issues #480, #485) ────────────────────────

    [Fact]
    public void Write_ReturnsBartzSlopeProvenance_WhenNoPairSupplied()
    {
        // No Pair → fallback path. Provenance must reflect the Sprint C.2 source labels.
        string tmp = Path.GetTempFileName();
        try
        {
            var inputs = new Su2ConfigInputs(
                Gas:             TestGas,
                MeshFilePath:    "/tmp/nozzle.su2",
                OutputDirectory: "/tmp",
                Density:         Su2MeshDensity.Coarse);
            var prov = Su2ConfigWriter.Write(tmp, inputs);

            Assert.Equal(SutherlandSource.BartzSlope, prov.SutherlandSource);
            Assert.Equal(string.Empty, prov.SutherlandPairLabel);
            Assert.Equal(MuRefSource.CeaTableFormula, prov.MuRefSource);
            Assert.Equal(string.Empty, prov.MuRefPairLabel);
            Assert.Equal(TestGas.ChamberTemp_K / 9.0, prov.SutherlandS_K, precision: 6);
            Assert.Equal(TestGas.Viscosity_PaS, prov.MuRef_PaS, precision: 12);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Write_ReturnsCeaProvenance_WhenLoxCh4PairSupplied()
    {
        // LOX/CH4 → both lookups hit the CEA per-pair path.
        string tmp = Path.GetTempFileName();
        try
        {
            var inputs = new Su2ConfigInputs(
                Gas:             TestGas,
                MeshFilePath:    "/tmp/nozzle.su2",
                OutputDirectory: "/tmp",
                Density:         Su2MeshDensity.Coarse,
                Pair:            PropellantPair.LOX_CH4);
            var prov = Su2ConfigWriter.Write(tmp, inputs);

            Assert.Equal(SutherlandSource.Cea, prov.SutherlandSource);
            Assert.Equal("LOX/CH4", prov.SutherlandPairLabel);
            Assert.Equal(MuRefSource.Cea, prov.MuRefSource);
            Assert.Equal("LOX/CH4", prov.MuRefPairLabel);
            // Sutherland S is independent of TestGas.ChamberTemp (per-pair lookup).
            Assert.NotEqual(TestGas.ChamberTemp_K / 9.0, prov.SutherlandS_K);
            // μ_ref differs from gas.Viscosity_PaS once the per-pair value applies.
            Assert.NotEqual(TestGas.Viscosity_PaS, prov.MuRef_PaS);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Write_WithLoxH2Pair_EmitsHydrogenRichSutherlandConstant()
    {
        // Confirm SUTHERLAND_CONSTANT = LOX/H2 lookup value (≈ 97 K), not T_c/9 (≈ 378 K).
        string tmp = Path.GetTempFileName();
        try
        {
            var inputs = new Su2ConfigInputs(
                Gas:             TestGas,
                MeshFilePath:    "/tmp/nozzle.su2",
                OutputDirectory: "/tmp",
                Density:         Su2MeshDensity.Coarse,
                Pair:            PropellantPair.LOX_H2);
            Su2ConfigWriter.Write(tmp, inputs);
            string content = File.ReadAllText(tmp);

            foreach (string line in content.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("SUTHERLAND_CONSTANT=", StringComparison.Ordinal))
                    continue;
                string val = trimmed.Substring("SUTHERLAND_CONSTANT=".Length).Trim();
                double actual = double.Parse(val, CultureInfo.InvariantCulture);
                // LOX/H2 placeholder is 97 K (±5 K mechanical-swap tolerance).
                Assert.InRange(actual, 92.0, 102.0);
                return;
            }
            Assert.Fail("SUTHERLAND_CONSTANT= not found.");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Write_WithUnimplementedPair_FallsBackToBartzSlope()
    {
        // N2O4/MMH is declared but not implemented in either lookup → fallback.
        string tmp = Path.GetTempFileName();
        try
        {
            var inputs = new Su2ConfigInputs(
                Gas:             TestGas,
                MeshFilePath:    "/tmp/nozzle.su2",
                OutputDirectory: "/tmp",
                Density:         Su2MeshDensity.Coarse,
                Pair:            PropellantPair.N2O4_MMH);
            var prov = Su2ConfigWriter.Write(tmp, inputs);

            Assert.Equal(SutherlandSource.BartzSlope, prov.SutherlandSource);
            Assert.Equal(MuRefSource.CeaTableFormula, prov.MuRefSource);
            // Identical numerics to the no-Pair path.
            Assert.Equal(TestGas.ChamberTemp_K / 9.0, prov.SutherlandS_K, precision: 6);
            Assert.Equal(TestGas.Viscosity_PaS, prov.MuRef_PaS, precision: 12);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CfdCalibrationInputs_CpModelFrozenGamma_RoundTrips()
    {
        // (e) CpModel.FrozenGamma can be set on CfdCalibrationInputs and read back.
        //     Only the CpModel property is exercised — Gas uses TestGas; class-typed
        //     params use null! (reference types, safe for a property-only read).
        var inputs = new CfdCalibrationInputs(
            Contour:            null!,
            Gas:                TestGas,
            SolverInputs:       null!,
            ChamberPressure_Pa: 5_000_000,
            CpModel:            CpModel.FrozenGamma);
        Assert.Equal(CpModel.FrozenGamma, inputs.CpModel);
    }

    [Theory]
    [InlineData("/tmp/nozzle.su2\nMARKER_HEATFLUX= ( wall, 999999 )")]
    [InlineData("/tmp/nozzle.su2\r\nITER= 1")]
    [InlineData("foo\nbar")]
    [InlineData("foo\rbar")]
    public void Write_RejectsNewlineInMeshFilePath(string injected)
    {
        // Audit 01-security L5: a newline-bearing MeshFilePath must not
        // smuggle additional SU2 directives into the .cfg via a literal
        // CR/LF in the path argument. The writer is internal-only today
        // (mesh paths come from a runner-computed temp dir), but this
        // guard is defence-in-depth for any future plumbing that lets
        // an end user route a mesh path through.
        var inputs = new Su2ConfigInputs(
            Gas:             TestGas,
            MeshFilePath:    injected,
            OutputDirectory: "/tmp",
            Density:         Su2MeshDensity.Coarse);

        string tmp = Path.GetTempFileName();
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => Su2ConfigWriter.Write(tmp, inputs));
            Assert.Contains("newline", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(tmp); }
    }
}
