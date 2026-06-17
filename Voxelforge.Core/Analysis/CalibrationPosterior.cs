// CalibrationPosterior.cs — Multi-knob MAP calibration for
// {CStarEfficiency, NozzleCfEfficiency, BartzScalingFactor,
//  CoolantHtcScalingFactor, CoolantFrictionScalingFactor}.
//
// Extends the single-knob Bartz grid search in MeasuredDataOverlay with a
// five-dimensional coordinate-descent MAP estimator backed by Gaussian priors
// and golden-section line searches on each axis. The axes are nearly decoupled:
//
//   • CStarEfficiency × NozzleCfEfficiency — constrained by total mass-flow
//     when the CSV carries a `total_mass_flow_kgs` column. (Sizing holds spec
//     thrust constant, so lower efficiency ⇒ higher mass flow to compensate.)
//
//   • BartzScalingFactor — constrained by peak wall T, coolant ΔT, and
//     coolant ΔP. Decoupled from efficiency to first order.
//
//   • CoolantHtcScalingFactor — constrained primarily by coolant ΔT.
//     Moves only when a CoolantDT_K observable is present in the CSV.
//
//   • CoolantFrictionScalingFactor — constrained by coolant ΔP.
//     Moves only when a CoolantDP_Pa observable is present in the CSV.
//
// When mass-flow data is absent, CStarEfficiency and NozzleCfEfficiency are
// held at their prior means and only the thermal / pressure-drop axes run.
//
// Uncertainty:
//   A Laplace approximation gives the diagonal posterior variance: the
//   curvature of the objective at the MAP is estimated by a three-point
//   finite difference. A high SsrCurvature on a knob means the data
//   constrains it tightly; low curvature means the prior dominates.
//
// Performance:
//   Each outer iteration calls the runner at most 5 × 30 = 150 times (one
//   golden section per axis). With maxOuterIterations = 4 the ceiling is
//   ~600 runner calls + 10 curvature probes. A headless GenerateWith call
//   typically takes 20-80 ms → calibration completes in < 60 s for all
//   presets. Progress is logged to Console.Error if verbose = true.

namespace Voxelforge.Analysis;

/// <summary>
/// Observables returned by the multi-knob calibration runner for one
/// (cStarEff, cfEff, bartzSF) triple.  NaN values are silently excluded
/// from the SSR so partial test data (e.g. no wall thermocouple) is fine.
/// </summary>
public sealed record CalibrationObservables(
    /// <summary>Total propellant mass flow predicted by the physics model (kg/s).</summary>
    double TotalMassFlow_kgs,
    /// <summary>Predicted peak gas-side wall temperature (K).</summary>
    double PeakWallT_K,
    /// <summary>Predicted coolant temperature rise T_out − T_in (K).</summary>
    double CoolantDT_K,
    /// <summary>Predicted coolant-circuit pressure drop (Pa).</summary>
    double CoolantDP_Pa);

/// <summary>
/// MAP estimate and Laplace-approximation uncertainty for a single knob.
/// </summary>
public sealed record KnobEstimate(
    /// <summary>Human-readable identifier, e.g. "CStarEfficiency".</summary>
    string Name,
    /// <summary>MAP estimate (coordinate-descent converged value).</summary>
    double MapValue,
    /// <summary>Prior mean used during calibration.</summary>
    double PriorMean,
    /// <summary>Prior standard deviation used during calibration.</summary>
    double PriorSigma,
    /// <summary>
    /// ∂²objective/∂θ² at the MAP, estimated by finite difference.
    /// Higher curvature = measurement data constrains this knob more tightly.
    /// Curvature &lt; 0.5 usually means the prior dominates.
    /// </summary>
    double SsrCurvature,
    /// <summary>Plain-English summary of the calibration outcome for this knob.</summary>
    string Interpretation);

/// <summary>
/// Full result of a five-knob MAP calibration.
/// </summary>
public sealed record MultiKnobCalibrationResult(
    KnobEstimate CStarEfficiency,
    KnobEstimate NozzleCfEfficiency,
    KnobEstimate BartzScalingFactor,
    KnobEstimate CoolantHtcScalingFactor,
    KnobEstimate CoolantFrictionScalingFactor,
    /// <summary>Objective value (likelihood SSR + prior penalty) at the prior means.</summary>
    double SsrAtPrior,
    /// <summary>Objective value at the MAP estimate.</summary>
    double SsrAtMap,
    /// <summary>Number of coordinate-descent outer iterations used before convergence.</summary>
    int    IterationsUsed,
    /// <summary>Diagnostic messages about data quality and calibration outcome.</summary>
    string[] Notes);

/// <summary>
/// Multi-knob MAP calibration extending <see cref="MeasuredDataOverlay"/>.
/// </summary>
public static class CalibrationPosterior
{
    // ── Prior parameters ─────────────────────────────────────────────────────
    // Literature anchors:
    //   CStarEff:    90-98 % for well-injected stoichiometric chambers [Sutton §3]
    //   CfEff:       92-99 % for well-designed nozzles with small divergence loss
    //   BartzSF:     empirically 0.7-1.3 × textbook for additive-manufactured channels
    //   CoolantHTC:  Dittus-Boelter/Sieder-Tate off by ±20-30 % for AM channels
    //   CoolantFric: Haaland off by ±30-50 % for rough AM channels at low Re
    private const double CStarPriorMean     = 0.95,  CStarPriorSigma     = 0.04;
    private const double CfPriorMean        = 0.94,  CfPriorSigma        = 0.03;
    private const double BartzPriorMean     = 1.00,  BartzPriorSigma     = 0.20;
    private const double HtcPriorMean       = 1.00,  HtcPriorSigma       = 0.15;
    private const double FrictionPriorMean  = 1.00,  FrictionPriorSigma  = 0.25;

    // ── Bounds ────────────────────────────────────────────────────────────────
    private const double CStarLo    = 0.84, CStarHi    = 1.00;
    private const double CfLo       = 0.87, CfHi       = 1.00;
    private const double BartzLo    = 0.60, BartzHi    = 1.40;
    private const double HtcLo      = 0.70, HtcHi      = 1.30;
    private const double FrictionLo = 0.50, FrictionHi = 1.50;

    // Weight of the prior penalty term relative to the likelihood SSR.
    // 0.001 keeps the prior as a soft regularizer: with one 10%-mismatch
    // channel the likelihood term (0.01) outweighs the prior at 1 σ (0.001)
    // by 10×, so data drives the calibration while the prior prevents
    // degeneracy when observables are absent or noisy.
    private const double PriorWeight = 0.001;

    // Convergence: stop when the maximum per-axis change across one full
    // round of golden sections is below this fraction of the axis range.
    private const double ConvergenceTol = 1e-4;

    /// <summary>
    /// Run the multi-knob MAP calibration.
    /// </summary>
    /// <param name="measured">
    ///   Steady-state summary from <see cref="MeasuredDataOverlay.Summarise"/>.
    ///   <c>TotalMassFlow_kgs</c> enables efficiency calibration; all other
    ///   thermal channels (WallT_K, CoolantDT_K, CoolantDP_Pa) enable Bartz
    ///   calibration. NaN / zero values are silently excluded.
    /// </param>
    /// <param name="runner">
    ///   Callback that re-evaluates the design at a given knob triple and
    ///   returns predicted observables. Must be deterministic and cheap
    ///   (headless <c>GenerateWith</c>, no voxel build). Called up to ~400
    ///   times during calibration.
    /// </param>
    /// <param name="maxOuterIterations">
    ///   Maximum coordinate-descent outer iterations. Typically converges in
    ///   2-3 when the two axes are weakly coupled. Default 4 is conservative.
    /// </param>
    /// <param name="verbose">
    ///   When true, logs per-iteration progress to <see cref="Console.Error"/>.
    /// </param>
    public static MultiKnobCalibrationResult Calibrate(
        MeasuredSummary                                                    measured,
        Func<double, double, double, double, double, CalibrationObservables> runner,
        int  maxOuterIterations = 4,
        bool verbose = false)
    {
        // ── Determine which observables are live ─────────────────────────────
        bool hasMassFlow = !double.IsNaN(measured.TotalMassFlow_kgs)
                           && measured.TotalMassFlow_kgs > 0;
        bool hasWallT    = !double.IsNaN(measured.WallT_K) && measured.WallT_K > 0;
        bool hasDT       = measured.CoolantDT_K  > 0;
        bool hasDP       = measured.CoolantDP_Pa > 0;
        bool hasThermal  = hasWallT || hasDT || hasDP;

        // ── Objective function: −log p(θ|data) ─────────────────────────────
        double Objective(double cstar, double cf, double bartz, double htcSF, double frictionSF)
        {
            // Prior penalty (Gaussian).
            double prior = PriorWeight * (
                Sq((cstar     - CStarPriorMean)    / CStarPriorSigma) +
                Sq((cf        - CfPriorMean)       / CfPriorSigma) +
                Sq((bartz     - BartzPriorMean)    / BartzPriorSigma) +
                Sq((htcSF     - HtcPriorMean)      / HtcPriorSigma) +
                Sq((frictionSF - FrictionPriorMean) / FrictionPriorSigma));

            var obs = runner(cstar, cf, bartz, htcSF, frictionSF);
            double lik = 0;

            if (hasMassFlow && !double.IsNaN(obs.TotalMassFlow_kgs))
                lik += Sq((obs.TotalMassFlow_kgs - measured.TotalMassFlow_kgs)
                          / measured.TotalMassFlow_kgs);
            if (hasWallT && !double.IsNaN(obs.PeakWallT_K))
                lik += Sq((obs.PeakWallT_K - measured.WallT_K) / measured.WallT_K);
            if (hasDT && !double.IsNaN(obs.CoolantDT_K))
                lik += Sq((obs.CoolantDT_K - measured.CoolantDT_K) / measured.CoolantDT_K);
            if (hasDP && !double.IsNaN(obs.CoolantDP_Pa))
                lik += Sq((obs.CoolantDP_Pa - measured.CoolantDP_Pa) / measured.CoolantDP_Pa);

            return lik + prior;
        }

        // ── Starting point: prior means ──────────────────────────────────────
        double cstarMap      = CStarPriorMean;
        double cfMap         = CfPriorMean;
        double bartzMap      = BartzPriorMean;
        double htcMap        = HtcPriorMean;
        double frictionMap   = FrictionPriorMean;

        double ssrAtPrior = Objective(CStarPriorMean, CfPriorMean, BartzPriorMean,
                                      HtcPriorMean, FrictionPriorMean);

        // ── Coordinate descent ───────────────────────────────────────────────
        int itersUsed = 0;
        for (int iter = 0; iter < maxOuterIterations; iter++)
        {
            double prevCstar    = cstarMap,  prevCf      = cfMap,
                   prevBartz    = bartzMap,  prevHtc     = htcMap,
                   prevFriction = frictionMap;

            // Axis 0 — CStarEfficiency (only useful when mass-flow constrains it)
            if (hasMassFlow)
            {
                double b = bartzMap, h = htcMap, fr = frictionMap;
                cstarMap = GoldenSection(x => Objective(x, cfMap, b, h, fr), CStarLo, CStarHi);
            }

            // Axis 1 — NozzleCfEfficiency (same observable as CStarEff)
            if (hasMassFlow)
            {
                double c = cstarMap, b = bartzMap, h = htcMap, fr = frictionMap;
                cfMap = GoldenSection(x => Objective(c, x, b, h, fr), CfLo, CfHi);
            }

            // Axis 2 — BartzScalingFactor (wall T + DT + DP)
            if (hasThermal)
            {
                double c = cstarMap, cf2 = cfMap, h = htcMap, fr = frictionMap;
                bartzMap = GoldenSection(x => Objective(c, cf2, x, h, fr), BartzLo, BartzHi);
            }

            // Axis 3 — CoolantHtcScalingFactor (primary: CoolantDT_K)
            if (hasDT)
            {
                double c = cstarMap, cf2 = cfMap, b = bartzMap, fr = frictionMap;
                htcMap = GoldenSection(x => Objective(c, cf2, b, x, fr), HtcLo, HtcHi);
            }

            // Axis 4 — CoolantFrictionScalingFactor (primary: CoolantDP_Pa)
            if (hasDP)
            {
                double c = cstarMap, cf2 = cfMap, b = bartzMap, h = htcMap;
                frictionMap = GoldenSection(x => Objective(c, cf2, b, h, x), FrictionLo, FrictionHi);
            }

            itersUsed = iter + 1;
            double maxChange = Math.Max(Math.Abs(cstarMap    - prevCstar),
                               Math.Max(Math.Abs(cfMap       - prevCf),
                               Math.Max(Math.Abs(bartzMap    - prevBartz),
                               Math.Max(Math.Abs(htcMap      - prevHtc),
                                        Math.Abs(frictionMap - prevFriction)))));

            if (verbose)
                Console.Error.WriteLine(
                    $"[CalibPosterior] iter {iter + 1}: "
                    + $"cstar={cstarMap:F4} cf={cfMap:F4} bartz={bartzMap:F4} "
                    + $"htc={htcMap:F4} fric={frictionMap:F4} Δ={maxChange:G3}");

            if (maxChange < ConvergenceTol) break;
        }

        double ssrAtMap = Objective(cstarMap, cfMap, bartzMap, htcMap, frictionMap);

        // ── Laplace curvature at MAP (finite difference, 3-point) ────────────
        const double hCstar = 0.005, hCf = 0.004, hBartz = 0.02, hHtc = 0.02, hFriction = 0.03;
        double curvCstar = FiniteDiffCurvature(
            x => Objective(x, cfMap, bartzMap, htcMap, frictionMap),
            cstarMap, hCstar, CStarLo, CStarHi);
        double curvCf = FiniteDiffCurvature(
            x => Objective(cstarMap, x, bartzMap, htcMap, frictionMap),
            cfMap, hCf, CfLo, CfHi);
        double curvBartz = FiniteDiffCurvature(
            x => Objective(cstarMap, cfMap, x, htcMap, frictionMap),
            bartzMap, hBartz, BartzLo, BartzHi);
        double curvHtc = FiniteDiffCurvature(
            x => Objective(cstarMap, cfMap, bartzMap, x, frictionMap),
            htcMap, hHtc, HtcLo, HtcHi);
        double curvFriction = FiniteDiffCurvature(
            x => Objective(cstarMap, cfMap, bartzMap, htcMap, x),
            frictionMap, hFriction, FrictionLo, FrictionHi);

        // ── Assemble result ───────────────────────────────────────────────────
        double ssrImprovement = ssrAtPrior > 0
            ? (ssrAtPrior - ssrAtMap) / ssrAtPrior * 100.0
            : 0.0;

        var notes = BuildNotes(
            hasMassFlow, hasThermal, hasWallT, hasDT, hasDP,
            ssrImprovement, ssrAtPrior, itersUsed, maxOuterIterations,
            cstarMap, cfMap, bartzMap, htcMap, frictionMap);

        return new MultiKnobCalibrationResult(
            CStarEfficiency:             BuildKnob("CStarEfficiency",             cstarMap,    hasMassFlow,
                                                    CStarPriorMean,    CStarPriorSigma,    curvCstar),
            NozzleCfEfficiency:          BuildKnob("NozzleCfEfficiency",          cfMap,       hasMassFlow,
                                                    CfPriorMean,       CfPriorSigma,       curvCf),
            BartzScalingFactor:          BuildKnob("BartzScalingFactor",          bartzMap,    hasThermal,
                                                    BartzPriorMean,    BartzPriorSigma,    curvBartz),
            CoolantHtcScalingFactor:     BuildKnob("CoolantHtcScalingFactor",     htcMap,      hasDT,
                                                    HtcPriorMean,      HtcPriorSigma,      curvHtc),
            CoolantFrictionScalingFactor:BuildKnob("CoolantFrictionScalingFactor",frictionMap, hasDP,
                                                    FrictionPriorMean, FrictionPriorSigma, curvFriction),
            SsrAtPrior:     ssrAtPrior,
            SsrAtMap:       ssrAtMap,
            IterationsUsed: itersUsed,
            Notes:          notes.ToArray());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Golden-section minimisation of f(x) over [lo, hi]. Classical
    /// algorithm; ~30 evaluations give tolerance &lt; 1e-5 × (hi−lo).
    /// </summary>
    public static double GoldenSection(
        Func<double, double> f, double lo, double hi, int maxEvals = 30)
    {
        const double phi    = 1.6180339887498949;
        const double resphi = 2.0 - phi;
        double x1 = lo + resphi * (hi - lo);
        double x2 = hi - resphi * (hi - lo);
        double f1 = f(x1), f2 = f(x2);
        for (int i = 0; i < maxEvals; i++)
        {
            double range = hi - lo;
            double mid   = 0.5 * (lo + hi);
            if (range < 1e-9 * (Math.Abs(mid) + 1e-15)) break;
            if (f1 < f2)
            {
                hi = x2; x2 = x1; f2 = f1;
                x1 = lo + resphi * (hi - lo); f1 = f(x1);
            }
            else
            {
                lo = x1; x1 = x2; f1 = f2;
                x2 = hi - resphi * (hi - lo); f2 = f(x2);
            }
        }
        return 0.5 * (lo + hi);
    }

    private static double FiniteDiffCurvature(
        Func<double, double> f1d, double xMap, double h, double lo, double hi)
    {
        // Clamp probe points to valid range; use one-sided when at boundary.
        double xP = Math.Min(xMap + h, hi);
        double xM = Math.Max(xMap - h, lo);
        double hP = xP - xMap;
        double hM = xMap - xM;
        // Three-point formula adapted for possibly unequal steps.
        if (hP <= 0 || hM <= 0) return 0;
        double fP = f1d(xP), f0 = f1d(xMap), fM = f1d(xM);
        // Generalised second-order finite difference: d²f/dx² ≈ 2(fP/hP - f0*(1/hP+1/hM) + fM/hM) / (hP+hM)
        double curv = 2.0 * (fP / hP - f0 * (1.0 / hP + 1.0 / hM) + fM / hM) / (hP + hM);
        return Math.Max(0, curv); // clamp negative numerical-noise artefacts
    }

    private static KnobEstimate BuildKnob(
        string name, double mapValue, bool wasConstrained,
        double priorMean, double priorSigma, double curvature)
    {
        string interp;
        if (!wasConstrained)
        {
            interp = "Not constrained by available data — held near prior mean.";
        }
        else if (curvature < 0.5)
        {
            interp = $"Weakly constrained (curvature {curvature:F2}); prior dominates. "
                   + $"Add more observables to narrow the posterior.";
        }
        else if (curvature < 3.0)
        {
            interp = $"Moderately constrained (curvature {curvature:F2}). "
                   + $"Posterior σ ≈ {1.0 / Math.Sqrt(curvature):F3}.";
        }
        else
        {
            interp = $"Well-constrained by data (curvature {curvature:F2}). "
                   + $"Posterior σ ≈ {1.0 / Math.Sqrt(curvature):F3}.";
        }
        return new KnobEstimate(name, mapValue, priorMean, priorSigma, curvature, interp);
    }

    private static List<string> BuildNotes(
        bool hasMassFlow, bool hasThermal, bool hasWallT, bool hasDT, bool hasDP,
        double ssrImprovementPct, double ssrAtPrior,
        int itersUsed, int maxIters,
        double cstarMap, double cfMap, double bartzMap, double htcMap, double frictionMap)
    {
        var notes = new List<string>();

        if (!hasMassFlow)
            notes.Add("No total_mass_flow_kgs channel — CStarEfficiency and "
                    + "NozzleCfEfficiency are held at prior means (0.95, 0.94). "
                    + "Add a DAQ mass-flow channel to calibrate combustion efficiency.");

        if (!hasThermal)
            notes.Add("No thermal channel (wall_t_k / coolant ΔT / ΔP) — "
                    + "BartzScalingFactor held at prior mean (1.00).");
        else if (!hasWallT)
            notes.Add("No wall_t_k channel — BartzScalingFactor calibrated from "
                    + "coolant ΔT/ΔP only; add an embedded thermocouple for a "
                    + "more direct Bartz calibration.");

        if (!hasDT)
            notes.Add("No coolant_dt_k channel — CoolantHtcScalingFactor held at "
                    + "prior mean (1.00). Add a coolant inlet/outlet thermocouple "
                    + "pair to calibrate the coolant-side HTC.");

        if (!hasDP)
            notes.Add("No coolant_dp_pa channel — CoolantFrictionScalingFactor held "
                    + "at prior mean (1.00). Add a differential pressure transducer "
                    + "across the cooling jacket to calibrate friction losses.");

        if (ssrAtPrior < 1e-10)
            notes.Add("No active observables — calibration returned prior means unchanged.");
        else if (ssrImprovementPct < 5.0)
            notes.Add($"SSR improved by only {ssrImprovementPct:F1} % — prior values "
                    + $"(η_c*={CStarPriorMean:F3}, η_Cf={CfPriorMean:F3}, "
                    + $"α_Bartz={BartzPriorMean:F3}, α_HTC={HtcPriorMean:F3}, "
                    + $"α_Friction={FrictionPriorMean:F3}) are already near-optimal.");
        else
            notes.Add($"SSR reduced by {ssrImprovementPct:F1} % "
                    + $"(prior → MAP). Calibrated: "
                    + $"CStarEff={cstarMap:F4}, CfEff={cfMap:F4}, "
                    + $"BartzSF={bartzMap:F4}, HtcSF={htcMap:F4}, "
                    + $"FrictionSF={frictionMap:F4}.");

        if (itersUsed < maxIters)
            notes.Add($"Converged in {itersUsed} iteration(s).");
        else
            notes.Add($"Reached max iterations ({maxIters}) without full convergence — "
                    + $"result is near-optimal but not tight. "
                    + $"Increase maxOuterIterations or check for a strongly coupled design.");

        return notes;
    }

    private static double Sq(double x) => x * x;
}
