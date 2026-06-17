// CfdCalibrationRunner.cs — Orchestrates the full CFD validation loop:
//   mesh writer → SU2 config → SU2_CFD subprocess → surface parser →
//   CalibrationPosterior.Calibrate() → MultiKnobCalibrationResult.
//
// Sprint C.2 (2026-05-06): direct T_aw vs T_aw comparison.
//   SU2 runs with an adiabatic wall BC (MARKER_HEATFLUX= wall, 0.0), so
//   SU2's surface "Temperature" at wall nodes = T_aw (adiabatic recovery
//   temperature, no heat loss). The runner callback now returns
//   RegenSolverOutputs.PeakAdiabaticWallTemp_K (peak T_aw across stations,
//   computed inside RegenCoolingSolver from the same Mach + γ + Pr basis
//   SU2 sees), so MAP calibration is comparing like for like.

using Voxelforge.Analysis;
using Voxelforge.Cfd.Config;
using Voxelforge.Cfd.Mesh;
using Voxelforge.Cfd.Parser;
using Voxelforge.Cfd.Runner;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Cfd;

/// <summary>Inputs for a single CFD calibration run.</summary>
public sealed record CfdCalibrationInputs(
    /// <summary>Nozzle axisymmetric contour — source of wall geometry for the mesh.</summary>
    ChamberContour Contour,
    /// <summary>Combustion gas state providing thermodynamic properties for the SU2 config.</summary>
    PropellantState Gas,
    /// <summary>
    /// Regen-cooling solver inputs. The runner callback will vary
    /// <see cref="RegenSolverInputs.BartzScalingFactor"/> during calibration.
    /// </summary>
    RegenSolverInputs SolverInputs,
    /// <summary>Chamber pressure (Pa) used for <see cref="MeasuredSummary"/>.</summary>
    double ChamberPressure_Pa,
    /// <summary>Mesh density preset (default Coarse for speed).</summary>
    Su2MeshDensity Density = Su2MeshDensity.Coarse,
    /// <summary>
    /// Optional working directory for SU2 files. When null a GUID-named temp directory is created.
    /// </summary>
    string? WorkDirectory = null,
    /// <summary>When true, CalibrationPosterior prints per-iteration progress to Console.Error.</summary>
    bool Verbose = false,
    /// <summary>
    /// Issue #454 (Sprint C.3 follow-on): when provided, <see cref="ThroatGammaComputer"/>
    /// re-queries <see cref="PropellantTables"/> at the isentropic throat pressure P* to
    /// derive a distinct <see cref="PropellantState.GammaThroat"/>, activating the
    /// <see cref="CpPolynomialFitter"/> polynomial-γ path. When null the frozen-γ fallback
    /// from Sprint C.2 is used.
    /// </summary>
    PropellantPair? Pair = null,
    /// <summary>
    /// Sprint C.3: selects the Cp(T) model forwarded to <see cref="Su2ConfigWriter"/>.
    /// Default <see cref="CpModel.PolynomialFit"/> activates polynomial γ_eff when
    /// <see cref="Pair"/> supplies a distinct GammaThroat.
    /// Set to <see cref="CpModel.FrozenGamma"/> to revert to the Sprint C.2 frozen-γ path
    /// without changing any other pipeline options.
    /// </summary>
    CpModel CpModel = CpModel.PolynomialFit);

/// <summary>Full result of a CFD-assisted BartzScalingFactor calibration.</summary>
public sealed record CfdCalibrationResult(
    /// <summary>SU2 surface temperature profile parsed from the surface output file.</summary>
    Su2WallProfile WallProfile,
    /// <summary>Multi-knob MAP calibration result (knob #2 = BartzScalingFactor).</summary>
    MultiKnobCalibrationResult CalibrationResult,
    /// <summary>Total wall-clock time for the entire pipeline (mesh+run+parse+calibrate).</summary>
    TimeSpan TotalWallTime,
    /// <summary>Non-fatal warnings from the pipeline (convergence, parse, calibration).</summary>
    string[] Warnings,
    /// <summary>
    /// Sprint C.2 follow-on (issues #480, #485): records which numerical values SU2
    /// actually saw as <c>SUTHERLAND_CONSTANT</c> + <c>MU_REF</c> and whether they
    /// came from the per-pair CEA lookup or the Sprint C.2 fallback path. Surfaced
    /// to <see cref="Voxelforge.Cfd.Report.CfdDriftReport"/> for transport-property
    /// provenance rendering. Default value is the Bartz-slope fallback for
    /// pre-existing call sites that don't supply a Pair.
    /// </summary>
    Su2ConfigProvenance ConfigProvenance = default);

/// <summary>
/// Orchestrates the SU2 CFD validation loop and wires the result into
/// <see cref="CalibrationPosterior"/> for BartzScalingFactor calibration.
/// </summary>
public static class CfdCalibrationRunner
{
    private static readonly TimeSpan[] DensityTimeouts =
    {
        TimeSpan.FromMinutes(10),  // Coarse
        TimeSpan.FromMinutes(30),  // Standard
        TimeSpan.FromMinutes(60),  // Fine
    };

    /// <summary>
    /// Runs the full mesh → SU2 → parse → calibration pipeline.
    /// </summary>
    /// <param name="inputs">Geometry, gas state, solver inputs, and pipeline options.</param>
    /// <param name="regenRunner">
    /// Delegate that re-solves <see cref="RegenCoolingSolver"/> for a given set of
    /// scaling-factor knob values; called inside the <see cref="CalibrationPosterior"/>
    /// golden-section loop (up to ~600 times for 4 outer iterations).
    /// </param>
    /// <returns>Wall profile, MAP calibration result, elapsed time, and warnings.</returns>
    public static CfdCalibrationResult RunCalibration(
        CfdCalibrationInputs inputs,
        Func<RegenSolverInputs, RegenSolverOutputs> regenRunner)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(regenRunner);

        var t0       = DateTimeOffset.UtcNow;
        var warnings = new List<string>();

        // Resolve work directory
        string workDir = inputs.WorkDirectory
            ?? Path.Combine(Path.GetTempPath(), $"vxf_cfd_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        string meshPath = Path.Combine(workDir, "nozzle.su2");
        string cfgPath  = Path.Combine(workDir, "nozzle.cfg");

        // ── Step 1: mesh ───────────────────────────────────────────────────────
        Su2MeshWriter.Write(meshPath, inputs.Contour, inputs.Density);

        // ── Step 2: config ─────────────────────────────────────────────────────
        // Issue #454: when PropellantPair is supplied, derive a distinct GammaThroat from a
        // PropellantTables lookup at the isentropic throat pressure. This gives
        // CpPolynomialFitter a non-trivial two-point Cp(T) spread, activating γ_eff.
        // Issue #463: gate on CpModel.PolynomialFit too — under FrozenGamma the polynomial
        // path is skipped and the modified GammaThroat is discarded, so the per-call
        // PropellantTables 2-D interpolation is wasted work.
        PropellantState gas = inputs.CpModel == CpModel.PolynomialFit && inputs.Pair.HasValue
            ? ThroatGammaComputer.WithThroatGamma(inputs.Gas, inputs.Pair.Value)
            : inputs.Gas;

        // Sprint C.3: fit polynomial Cp(T) from chamber+throat anchor points; use γ_eff
        // in place of frozen chamber γ for equilibrium-corrected gas states.
        // CpModel.FrozenGamma skips the fit entirely — regression-safety escape hatch.
        CpPolynomialResult? poly = inputs.CpModel == CpModel.PolynomialFit
            ? CpPolynomialFitter.Fit(gas)
            : null;
        var cfgInputs = new Su2ConfigInputs(
            Gas:             gas,
            MeshFilePath:    meshPath.Replace('\\', '/'),
            OutputDirectory: workDir.Replace('\\', '/'),
            Density:         inputs.Density,
            PolynomialCp:    poly is { IsFlatCp: false } ? poly : null,
            CpModel:         inputs.CpModel,
            // Sprint C.2 follow-on (issues #480, #485): forward Pair so SU2 sees
            // per-pair Sutherland-S + μ_ref when implemented; falls back to the
            // Sprint C.2 Bartz-slope path when null or pair not implemented.
            Pair:            inputs.Pair);
        Su2ConfigProvenance configProvenance = Su2ConfigWriter.Write(cfgPath, cfgInputs);

        // ── Step 3: SU2 subprocess ─────────────────────────────────────────────
        TimeSpan timeout = DensityTimeouts[(int)inputs.Density];
        Su2RunResult runResult = Su2CfdRunner.Run(cfgPath, workDir, timeout);

        if (!runResult.Converged)
            warnings.Add(
                $"SU2 did not converge (exit={runResult.ExitCode}, " +
                $"residual drop={runResult.ResidualDrop:F1} orders). " +
                "Calibration proceeds on unconverged T_aw field.");

        // ── Step 4: surface parser ─────────────────────────────────────────────
        Su2WallProfile wallProfile;
        try
        {
            wallProfile = Su2SurfaceParser.Parse(workDir, inputs.Contour, runResult.Converged);
        }
        catch (FileNotFoundException ex)
        {
            warnings.Add($"Surface CSV not found: {ex.Message}. Returning prior-only calibration.");
            wallProfile = new Su2WallProfile(
                Converged:                  false,
                PeakAdiabaticWallTemp_K:    double.NaN,
                AdiabaticWallTempByStation: new Dictionary<int, double>(),
                NodeCount:                  0,
                ParseWarnings:              ex.Message);
        }

        if (!string.IsNullOrEmpty(wallProfile.ParseWarnings))
            warnings.Add(wallProfile.ParseWarnings);

        // ── Step 5: CalibrationPosterior wiring ────────────────────────────────
        //
        // Sprint C.2: direct T_aw vs T_aw comparison.
        //   SU2 surface T (adiabatic BC) ⇄ RegenSolverOutputs.PeakAdiabaticWallTemp_K
        // Both are T_aw, computed from the same axisymmetric isentropic flow basis.
        var measured = new MeasuredSummary(
            SampleCount:         1,
            ChamberP_Pa:         inputs.ChamberPressure_Pa,
            CoolantDP_Pa:        double.NaN,
            CoolantDT_K:         double.NaN,
            CoolantT_In_K:       double.NaN,
            CoolantT_Out_K:      double.NaN,
            Thrust_N:            double.NaN,
            WallT_K:             wallProfile.PeakAdiabaticWallTemp_K,
            WallTByStation:      wallProfile.AdiabaticWallTempByStation.Count > 0
                                     ? wallProfile.AdiabaticWallTempByStation
                                     : null,
            TotalMassFlow_kgs:   double.NaN);

        // Runner callback: vary BartzScalingFactor (and the coolant-side knobs that
        // affect T_wg). Returns peak T_aw — a function of Mach + γ + Pr only, so
        // BartzScalingFactor itself doesn't move it; the MAP estimate now reflects
        // the systematic offset between SU2's RANS-resolved recovery factor
        // (r ≈ 0.89 at Pr_t=0.9) and the analytic Pr^(1/3) ≈ 0.91 in
        // PropellantTables.AdiabaticWallTemp.
        CalibrationObservables Runner(
            double cstar, double cf, double bartz, double htcSF, double frictionSF)
        {
            var modified = inputs.SolverInputs with
            {
                BartzScalingFactor         = bartz,
                CoolantHtcScalingFactor    = htcSF,
                CoolantFrictionScalingFactor = frictionSF,
            };
            var outputs = regenRunner(modified);
            return new CalibrationObservables(
                TotalMassFlow_kgs: double.NaN,
                PeakWallT_K:       outputs.PeakAdiabaticWallTemp_K,
                CoolantDT_K:       double.NaN,
                CoolantDP_Pa:      double.NaN);
        }

        var calResult = CalibrationPosterior.Calibrate(measured, Runner, 4, inputs.Verbose);

        TimeSpan elapsed = DateTimeOffset.UtcNow - t0;

        return new CfdCalibrationResult(
            WallProfile:       wallProfile,
            CalibrationResult: calResult,
            TotalWallTime:     elapsed,
            Warnings:          warnings.ToArray(),
            ConfigProvenance:  configProvenance);
    }
}
