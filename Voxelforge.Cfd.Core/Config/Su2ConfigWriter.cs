// Su2ConfigWriter.cs — SU2 v8 configuration file writer for nozzle RANS validation.
//
// Emits a .cfg file for:
//   • 2-D axisymmetric SST RANS (SOLVER= RANS, KIND_TURB_MODEL= SST)
//   • Adiabatic wall BC (MARKER_HEATFLUX= wall, 0.0)
//   • Supersonic exit (MARKER_SUPERSONIC_OUTLET)
//   • Ideal-gas thermodynamics with γ and R_gas from PropellantState
//
// Sprint C.2 (2026-05-06): Sutherland S derived from the Bartz μ∝T^0.6
// exponent (see SutherlandConstantFromBartzSlope) instead of the
// pre-C.2 hot-gas approximation S = 0.5 · T_chamber.
//
// Sprint C.3 (2026-05-07): when Su2ConfigInputs.PolynomialCp is non-null and
// IsFlatCp=false, the GAMMA_VALUE is replaced with the temperature-averaged
// γ_eff derived by CpPolynomialFitter — a better approximation for equilibrium
// flows where γ varies from chamber to throat.  CP_POLYCOEFFS is also emitted
// (SU2 IDEAL_GAS ignores it; the key documents the polynomial for tooling).
//
// Sprint C.2 follow-on (issues #480, #485): when Su2ConfigInputs.Pair is
// supplied, SUTHERLAND_CONSTANT (S) and MU_REF are sourced per-pair from
// SutherlandFromCea / MuRefFromCea instead of the temperature-only Bartz-slope
// approximation / CeaTable2DBase formula. Pair = null preserves Sprint C.2
// behaviour exactly.

using System.Globalization;
using Voxelforge.Cfd.Mesh;
using Voxelforge.Combustion;

namespace Voxelforge.Cfd.Config;

/// <summary>Inputs required to generate a SU2 configuration file for nozzle RANS.</summary>
public sealed record Su2ConfigInputs(
    /// <summary>Gas thermodynamic state providing γ, R_gas, T_chamber, P_chamber, viscosity.</summary>
    PropellantState Gas,
    /// <summary>
    /// Path to the .su2 mesh file as seen by SU2 (forward slashes required on Windows).
    /// </summary>
    string MeshFilePath,
    /// <summary>Working directory where SU2 will write output files.</summary>
    string OutputDirectory,
    /// <summary>Mesh density — controls iteration count (Coarse=500, Standard=1500, Fine=3000).</summary>
    Su2MeshDensity Density,
    /// <summary>
    /// Sprint C.3: optional polynomial Cp(T) fit from <see cref="CpPolynomialFitter.Fit"/>.
    /// When non-null and <see cref="CpPolynomialResult.IsFlatCp"/> is false, GAMMA_VALUE is
    /// set to <see cref="CpPolynomialResult.GammaEffective"/> and CP_POLYCOEFFS is emitted.
    /// Null (or IsFlatCp=true) falls back to <see cref="PropellantState.GammaChamber"/>.
    /// </summary>
    CpPolynomialResult? PolynomialCp = null,
    /// <summary>
    /// Sprint C.3: toggle between polynomial Cp(T) γ_eff and frozen-γ fallback.
    /// Default <see cref="CpModel.PolynomialFit"/> activates the polynomial path when
    /// <see cref="PolynomialCp"/> is non-null and non-flat.
    /// Set to <see cref="CpModel.FrozenGamma"/> to suppress CP_POLYCOEFFS and revert to
    /// GAMMA_VALUE = GammaChamber regardless of <see cref="PolynomialCp"/>.
    /// </summary>
    CpModel CpModel = CpModel.PolynomialFit,
    /// <summary>
    /// Sprint C.2 follow-on (issues #480, #485): when supplied, SUTHERLAND_CONSTANT
    /// (S) and MU_REF are resolved per-pair via <see cref="SutherlandFromCea"/>
    /// and <see cref="MuRefFromCea"/>. When null, Sprint C.2 fallback values are
    /// used (S = T_chamber / 9 from Bartz slope; MU_REF = gas.Viscosity_PaS from
    /// the CeaTable2DBase per-temperature formula).
    /// </summary>
    PropellantPair? Pair = null);

/// <summary>
/// Provenance returned by <see cref="Su2ConfigWriter.Write"/>. Records which
/// numerical values SU2 actually saw as <c>SUTHERLAND_CONSTANT</c> + <c>MU_REF</c>
/// and whether they came from the per-pair CEA lookup or the Sprint C.2 fallback
/// path. Forwarded onto <see cref="Voxelforge.Cfd.CfdCalibrationResult"/> so the
/// drift report can render a transport-property provenance row.
/// </summary>
/// <param name="SutherlandS_K">Resolved Sutherland constant S [K].</param>
/// <param name="SutherlandSource">CEA lookup vs Bartz-slope fallback.</param>
/// <param name="SutherlandPairLabel">"LOX/CH4" / "LOX/H2" / "LOX/RP-1" or empty.</param>
/// <param name="MuRef_PaS">Resolved μ_ref [Pa·s].</param>
/// <param name="MuRefSource">CEA lookup vs CeaTable2DBase formula fallback.</param>
/// <param name="MuRefPairLabel">"LOX/CH4" / "LOX/H2" / "LOX/RP-1" or empty.</param>
public readonly record struct Su2ConfigProvenance(
    double SutherlandS_K,
    SutherlandSource SutherlandSource,
    string SutherlandPairLabel,
    double MuRef_PaS,
    MuRefSource MuRefSource,
    string MuRefPairLabel);

/// <summary>
/// Generates SU2 v8 configuration files for 2-D axisymmetric SST RANS nozzle validation.
/// </summary>
public static class Su2ConfigWriter
{
    private const double R_Universal = 8314.462618; // J / (kmol·K)

    /// <summary>
    /// Writes a SU2 .cfg file to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="outputPath">Destination .cfg file path.</param>
    /// <param name="inputs">Gas state, mesh path, output directory, and density preset.</param>
    /// <returns>
    /// Provenance record describing which Sutherland-S and μ_ref sources were used
    /// (per-pair CEA lookup vs Bartz-slope / CeaTable2DBase formula fallback). The
    /// runner forwards this onto <see cref="Voxelforge.Cfd.CfdCalibrationResult"/>
    /// so the drift report can render a "Sutherland source" / "Viscosity reference"
    /// row in the Gas Model section.
    /// </returns>
    public static Su2ConfigProvenance Write(string outputPath, Su2ConfigInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(inputs);

        var gas  = inputs.Gas;
        var ci   = CultureInfo.InvariantCulture;

        double rGas     = R_Universal / gas.MolecularWeight;          // J / (kg·K)
        var    sLookup  = SutherlandFromCea.Lookup(inputs.Pair, gas.ChamberTemp_K);
        var    muLookup = MuRefFromCea.Lookup(inputs.Pair, gas.Viscosity_PaS);
        double sutherS  = sLookup.SutherlandS_K;
        double muRef    = muLookup.MuRef_PaS;
        int    iters    = inputs.Density switch
        {
            Su2MeshDensity.Coarse   => 500,
            Su2MeshDensity.Standard => 1500,
            _                       => 3000,
        };

        // SU2 on Windows requires forward slashes in file paths inside the cfg.
        string meshPath = inputs.MeshFilePath.Replace('\\', '/');

        // Audit 01-security L5: reject newline-bearing paths so a
        // (hypothetical) user-controlled MeshFilePath can't smuggle
        // extra SU2 directives into the .cfg via a literal '\n'. Today
        // CfdCalibrationRunner constructs meshPath from an internally-
        // generated workDir, so the value isn't user-controlled — this
        // guard is defence-in-depth for any future plumbing that lets
        // an end user pass a mesh path through to the writer.
        if (meshPath.AsSpan().IndexOfAny('\n', '\r') >= 0)
            throw new ArgumentException(
                "MeshFilePath must not contain newline characters; SU2 .cfg" +
                " files use newline as a directive separator, so an embedded" +
                " CR/LF would silently inject additional SU2 directives.",
                nameof(inputs));

        using var sw = new StreamWriter(outputPath, append: false);

        void L(string line) => sw.WriteLine(line);
        void V(string key, double val) => sw.WriteLine($"{key}= {val.ToString("G10", ci)}");
        void S(string key, string val) => sw.WriteLine($"{key}= {val}");

        L("%");
        L("% Voxelforge Sprint C.3 — auto-generated SU2 v8 RANS config (axisymmetric nozzle)");
        L("%");
        L("% Physics");
        S("SOLVER",           "RANS");
        S("KIND_TURB_MODEL",  "SST");
        S("MATH_PROBLEM",     "DIRECT");
        S("RESTART_SOL",      "NO");
        S("AXISYMMETRIC",     "YES");
        L("%");
        // Sprint C.3: use temperature-averaged γ_eff when polynomial path is requested and a
        // non-flat fit is available. CpModel.FrozenGamma suppresses the polynomial path entirely.
        bool usePolyGamma = inputs.CpModel == CpModel.PolynomialFit
                         && inputs.PolynomialCp is { IsFlatCp: false };
        double gammaValue = usePolyGamma ? inputs.PolynomialCp!.GammaEffective : gas.GammaChamber;
        L("% Gas model — Sprint C.3: γ_eff averaged from polynomial Cp(T) fit when available");
        S("FLUID_MODEL",  "IDEAL_GAS");
        V("GAMMA_VALUE",  gammaValue);
        V("GAS_CONSTANT", rGas);
        if (usePolyGamma)
        {
            double[] b = inputs.PolynomialCp!.Coefficients;
            // CP_POLYCOEFFS documents the polynomial used to derive γ_eff.
            // SU2 IDEAL_GAS ignores this key; it is present for tooling round-trip.
            sw.WriteLine(
                $"CP_POLYCOEFFS= ( {b[0].ToString("G10", ci)}, {b[1].ToString("G10", ci)}, " +
                $"{b[2].ToString("G10", ci)}, {b[3].ToString("G10", ci)}, {b[4].ToString("G10", ci)} )");
        }
        L("%");
        // Sprint C.2 follow-on (issues #480, #485): per-pair S + μ_ref lookups
        // when Inputs.Pair is supplied; Sprint C.2 Bartz-slope fallback otherwise.
        string sLabel  = sLookup.Source == SutherlandSource.Cea
            ? $"CEA blend ({sLookup.PairLabel})"
            : "Bartz slope (S = T_c / 9)";
        string muLabel = muLookup.Source == MuRefSource.Cea
            ? $"CEA blend ({muLookup.PairLabel})"
            : "CeaTable2DBase formula";
        L($"% Viscosity (Sutherland) — S source: {sLabel}; μ_ref source: {muLabel}");
        S("VISCOSITY_MODEL",  "SUTHERLAND");
        V("MU_REF",           muRef);
        V("MU_T_REF",         gas.ChamberTemp_K);
        V("SUTHERLAND_CONSTANT", sutherS);
        L("%");
        L("% Free-stream (chamber barrel conditions, M≈0.01)");
        V("FREESTREAM_TEMPERATURE", gas.ChamberTemp_K);
        V("FREESTREAM_PRESSURE",    gas.ChamberPressure_Pa);
        S("FREESTREAM_MACH",  "0.01");
        L("%");
        L("% Boundary conditions");
        sw.WriteLine($"MARKER_INLET= ( inlet, {gas.ChamberTemp_K.ToString("G10", ci)}, {gas.ChamberPressure_Pa.ToString("G10", ci)} )");
        S("MARKER_SUPERSONIC_OUTLET", "( outlet )");
        S("MARKER_HEATFLUX",  "( wall, 0.0 )");
        S("MARKER_SYM",       "( axis )");
        S("MARKER_PLOTTING",  "( wall )");
        S("MARKER_MONITORING","( wall )");
        L("%");
        L("% Surface output (per-node wall temperatures for Su2SurfaceParser)");
        S("SURFACE_OUTPUT",   "PRESSURE, TEMPERATURE, HEAT_FLUX, SKIN_FRICTION, MACH_NUMBER");
        S("SURFACE_FILENAME", "surface_flow");
        L("%");
        L("% Mesh");
        S("MESH_FILENAME",    meshPath);
        S("MESH_FORMAT",      "SU2");
        L("%");
        L("% Volume output");
        S("OUTPUT_FORMAT",    "PARAVIEW");
        L("%");
        L("% Convergence");
        S("CONV_FIELD",              "RMS_DENSITY");
        S("CONV_RESIDUAL_MINVAL",    "-6");
        S("ITER",                    iters.ToString(ci));
        L("%");
        L("% Linear solver");
        S("LINEAR_SOLVER",         "FGMRES");
        S("LINEAR_SOLVER_PREC",    "ILU");
        S("LINEAR_SOLVER_ERROR",   "1E-6");
        S("LINEAR_SOLVER_ITER",    "5");
        L("%");
        L("% CFL + adaptive stepping");
        S("CFL_NUMBER",         "5.0");
        S("CFL_ADAPT",          "YES");
        S("CFL_ADAPT_PARAM",    "( 0.1, 2.0, 0.5, 100.0 )");
        L("%");
        L("% Numerical scheme (flow + turbulence)");
        S("CONV_NUM_METHOD_FLOW",   "ROE");
        S("MUSCL_FLOW",             "YES");
        S("SLOPE_LIMITER_FLOW",     "VENKATAKRISHNAN");
        S("VENKAT_LIMITER_COEFF",   "0.05");
        S("TIME_DISCRE_FLOW",       "EULER_IMPLICIT");
        S("CONV_NUM_METHOD_TURB",   "SCALAR_UPWIND");
        S("TIME_DISCRE_TURB",       "EULER_IMPLICIT");

        sw.Flush();

        return new Su2ConfigProvenance(
            SutherlandS_K:    sutherS,
            SutherlandSource: sLookup.Source,
            SutherlandPairLabel: sLookup.PairLabel,
            MuRef_PaS:        muRef,
            MuRefSource:      muLookup.Source,
            MuRefPairLabel:   muLookup.PairLabel);
    }

    /// <summary>
    /// Sprint C.2 (2026-05-06): Sutherland constant S calibrated so the
    /// resulting μ(T) slope at T_ref = T_chamber matches the Bartz
    /// μ ∝ T^0.6 hot-gas exponent.
    ///
    /// Derivation. Differentiating Sutherland's law
    ///   μ(T) = μ_ref · (T/T_ref)^1.5 · (T_ref + S)/(T + S)
    /// gives
    ///   d(ln μ) / d(ln T) = 1.5 − T/(T + S).
    /// Anchoring this slope at T = T_ref to the Bartz value 0.6 yields
    ///   T_ref / (T_ref + S) = 0.9   ⇒   S = T_ref · (1/0.9 − 1) = T_ref / 9.
    /// Compared to the pre-C.2 S = 0.5 · T_c (which gives a slope of 0.83 —
    /// far steeper than 0.6), this matches measured viscosity-temperature
    /// behaviour for combustion gases over the 1500-4000 K rocket range.
    ///
    /// Falls back to the air-air baseline S = 110.4 K if T_chamber is
    /// non-positive or non-finite.
    /// </summary>
    public static double SutherlandConstantFromBartzSlope(double chamberTemp_K)
    {
        if (!double.IsFinite(chamberTemp_K) || chamberTemp_K <= 0.0)
            return 110.4; // air-air baseline (Sutherland 1893)
        return chamberTemp_K / 9.0;
    }
}
