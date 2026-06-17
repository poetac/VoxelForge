// RegenChamberForm.ResultsDisplay.cs — UpdateResults decomposition.
//
// UpdateResults previously lived in RegenChamberForm.cs at ~340 LOC
// of sequential label-population code — the single biggest remaining
// sprawl after the partial-class split. Each time a new physics path
// landed (aerospike, preburner thermal, turbopump, chilldown, ...)
// UpdateResults grew another 30–80 LOC block. The method had become a
// drag to read and a merge-conflict hotspot for anyone touching
// multiple physics paths in one sprint.
//
// The decomposition splits UpdateResults into 13 cohesive section
// helpers, one per result family. The public UpdateResults in
// RegenChamberForm.cs is now a ~15-LOC orchestrator that calls each
// helper in the same order they populated before — zero behaviour
// change, same labels, same colours, same ordering. The extracted
// helpers live here so the main file stays focused on construction,
// event wiring, and public UI surface.
//
// Why section helpers, not per-field helpers?
// ─────────────────────────────────────────────
// Tried per-field first (40+ one-liners) — churn-dense but each helper
// did too little to justify the name lookup. 13 section helpers hit the
// sweet spot: each is ~10-40 LOC, each owns one physics story (thermal,
// stress, manufacturing, chilldown, ...), and the orchestrator lists
// them in readout-order for anyone skimming the file.
//
// All helpers are private (sibling to UpdateResults), take a
// RegenGenerationResult (+ optional RegenScoreResult for the warnings
// panel), and mutate only the labels / panels they own. No helper
// touches another helper's labels. No helper allocates; no helper
// calls back into the domain layer.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Voxelforge.Combustion.Stability;
using Voxelforge.HeatTransfer;
using Voxelforge.IO;
using Voxelforge.Optimization;

namespace Voxelforge.UI;

public partial class RegenChamberForm
{
    // ── Engine summary: D_t, D_e, mass-flow, Isp ─────────────────────
    private void PopulateEngineSummary(RegenGenerationResult r)
    {
        var d = r.Derived;
        lblThroatD.Text  = $"D_throat: {d.ThroatDiameter_mm:F2} mm";
        lblExitD.Text    = $"D_exit: {2 * r.Contour.ExitRadius_mm:F2} mm";
        lblMassFlow.Text = $"ṁ total: {d.TotalMassFlow_kgs:F3} kg/s  (fuel: {d.FuelMassFlow_kgs:F3}, ox: {d.OxidizerMassFlow_kgs:F3})";
        lblIsp.Text      = $"Isp (vac/sl ideal): {d.IdealIspVacuum_s:F0} / {d.IdealIspSeaLevel_s:F0} s  |  C_F = {d.ThrustCoefficient:F3}";
    }

    // ── Dimensions + mass + cost ─────────────────────────────────────
    private void PopulateDimensionsAndMass(RegenGenerationResult r)
    {
        var g = r.Geometry;
        lblChamberL.Text = $"L_chamber: {r.Contour.ChamberLength_mm:F0} mm  (total {r.Contour.TotalLength_mm:F0} mm)";
        lblTotalL.Text   = $"L_total: {r.Contour.TotalLength_mm:F0} mm";
        lblOD.Text       = $"OD: {g.BoundingDiameter_mm:F0} mm";
        lblMass.Text     = $"Mass: {g.TotalMass_g:F0} g ({g.TotalMass_g / 454:F2} lb)";
        lblCost.Text     = $"Print cost est.: ${g.PrintedCost_USD:F0}";
    }

    // ── Thermal readouts (wall T, coolant out, ΔP, heat load, throat-q)
    private void PopulateThermalReadouts(RegenGenerationResult r)
    {
        var t   = r.Thermal;
        var mat = WallMaterials.All[r.Conditions.WallMaterialIndex];
        var tColor = t.WallTempExceedsLimit ? Color.Red : Color.Black;
        lblPeakT.Text       = $"Peak wall T: {t.PeakGasSideWallT_K:F0} K (limit {mat.MaxServiceTemp_K:F0} K)";
        lblPeakT.ForeColor  = tColor;
        lblMargin.Text      = $"Wall T margin: {t.WallMarginK:+0;-0;0} K";
        lblCoolantOut.Text  = $"Coolant T out: {t.CoolantOutletT_K:F0} K (ΔT={t.CoolantOutletT_K - t.CoolantInletT_K:F0} K)";
        lblDP.Text          = $"Coolant ΔP: {t.CoolantPressureDrop_Pa / 1e6:F2} MPa ({t.CoolantPressureDrop_Pa / r.Conditions.ChamberPressure_Pa:P1} of P_c)";
        lblHeatLoad.Text    = $"Total heat load: {t.TotalHeatLoad_W / 1000:F1} kW";
        lblThroatQ.Text     = $"Throat heat flux: {t.ThroatHeatFlux_Wm2 / 1e6:F1} MW/m²";
    }

    // ── Charts + compare panel (A-side sync) ─────────────────────────
    private void PopulateChartsAndCompare(RegenGenerationResult r)
    {
        // Feed the per-station data into the axial chart. The chart
        // auto-scales each axis independently and handles the
        // zero-station / null-Stations case internally.
        axialProfilePanel.SetOutputs(r.Thermal);

        // Keep "A" on the compare panel synced to the current
        // generation result so deltas vs B reflect the latest design.
        comparePanel.SetA(r);
    }

    // ── Structural SF + stability pills (chug / screech / composite)
    private void PopulateStructuralAndStability(RegenGenerationResult r)
    {
        var s = r.Stress;

        // Status foregrounds go through ThemeManager so High-Contrast
        // + Dark mode see theme-appropriate colours.
        var sfSev = s.MinSafetyFactor < 1.0 ? PillSeverity.Fail
                  : s.MinSafetyFactor < 1.5 ? PillSeverity.Marginal
                  : PillSeverity.Pass;
        lblSF.Text = $"Min safety factor: {s.MinSafetyFactor:F2}" + (s.YieldExceeded ? "  (yield exceeded)" : "");
        lblSF.ForeColor = ThemeManager.StatusForeground(sfSev);
        lblStress.Text = $"Peak hoop/thermal/VM: {s.PeakHoop_MPa:F0} / {s.PeakThermal_MPa:F0} / {s.PeakCombined_MPa:F0} MPa";

        // Structural-confidence pill. High/Medium/Low tracks whether
        // threaded ports + flanges are introducing stress-concentration
        // risks the analytical VM check does not model.
        string confTag = r.StructuralConfidence switch
        {
            StructuralConfidence.High   => "HIGH",
            StructuralConfidence.Medium => "MEDIUM",
            _                           => "LOW",
        };
        lblStructConfidence.Text = $"Confidence: {confTag} — {r.StructuralConfidenceReason}";
        lblStructConfidence.ForeColor = ThemeManager.StatusForeground(r.StructuralConfidence switch
        {
            StructuralConfidence.High   => PillSeverity.Pass,
            StructuralConfidence.Medium => PillSeverity.Marginal,
            _                           => PillSeverity.Fail,
        });

        // Combustion stability pills (chug / screech / composite).
        var st = r.Stability;
        ApplyStabilityPill(lblStabilityChug, "Chug", st.Chug.Rating);
        // Screech sub-rating is implicit in the composite — surface
        // whichever individual condition demoted the composite (if any).
        bool t1InBand = st.Screech.T1_Hz >= StabilityScreening.ScreechRiskBand_LowerHz
                     && st.Screech.T1_Hz <= StabilityScreening.ScreechRiskBand_UpperHz;
        bool modeOverlap = Math.Abs(st.Screech.L1_Hz - st.Screech.T1_Hz)
                         / Math.Max(st.Screech.T1_Hz, 1.0)
                         < StabilityScreening.ModeOverlapTolerance;
        var screechRating = modeOverlap
            ? StabilityRating.Fail
            : t1InBand
                ? StabilityRating.Marginal
                : StabilityRating.Pass;
        ApplyStabilityPill(lblStabilityScreech,   "Screech",   screechRating);
        ApplyStabilityPill(lblStabilityComposite, "Composite", st.Composite);
        lblStabilityFreqs.Text =
            $"L1 {st.Screech.L1_Hz,7:F0} Hz  |  T1 {st.Screech.T1_Hz,7:F0} Hz  |  " +
            $"T2 {st.Screech.T2_Hz,7:F0} Hz  |  c {st.Screech.SoundSpeed_ms,5:F0} m/s";
    }

    // ── Manufacturing: min feature, build time, overhang, material src
    private void PopulateManufacturingReadouts(RegenGenerationResult r)
    {
        var m = r.Manufacturing;
        var g = r.Geometry;
        lblMinFeature.Text = $"Min feature size: {m.MinFeatureSize_mm:F2} mm" + (m.FeatureSizeOK ? "" : "  (BELOW LPBF MIN)");
        lblBuildTime.Text  = $"Est. build time: {m.EstimatedBuildHours:F1} h  |  cost: ${m.EstimatedBuildCost_USD:F0}";
        var o = m.Overhang;
        string overhangTxt = o.AllSelfSupporting
            ? $"Overhang: self-supporting (worst {Math.Min(o.WorstOverhangAngle_deg_InnerWall, o.WorstOverhangAngle_deg_OuterWall):F0}\u00b0)"
            : $"Overhang: {o.UnprintableStationCount} unprintable, {o.MarginalStationCount} marginal; worst {Math.Min(o.WorstOverhangAngle_deg_InnerWall, o.WorstOverhangAngle_deg_OuterWall):F0}\u00b0";
        lblOverhangSummary.Text = overhangTxt;
        lblOverhangSummary.ForeColor = o.UnprintableStationCount > 0 ? Color.Red
                                     : o.MarginalStationCount > 0   ? Color.DarkOrange
                                                                    : Color.DarkGreen;
        // Sprint 14 / Track I / P16: only allocate the substring when we
        // actually need to truncate. The vast majority of material
        // DataSource strings are <= 78 chars; the unconditional Substring
        // call was allocating per UI refresh for no reason.
        string src = m.Material.DataSource;
        lblMaterialSource.Text = $"Material src: {(src.Length <= 78 ? src : src.Substring(0, 78))}";
        lblSTLMessage.Text     = string.IsNullOrEmpty(g.InjectorSTLMessage) ? "STL: disabled" : $"STL: {g.InjectorSTLMessage}";
    }

    // ── Diagnostics: film, Isp penalty, axial coupling, convergence ──
    private void PopulateDiagnosticsReadouts(RegenGenerationResult r)
    {
        var t = r.Thermal;
        var d = r.Derived;
        if (t.FilmMassFlow_kgs > 0)
            lblFilmStatus.Text = $"Film: {t.FilmMassFlow_kgs * 1000:F1} g/s ({100 * t.FilmMassFlow_kgs / Math.Max(d.FuelMassFlow_kgs, 1e-9):F0}% of fuel)";
        else
            lblFilmStatus.Text = "Film: disabled";
        lblIspPenalty.Text   = $"Isp penalty: {t.IspPenaltyFraction * 100:F2} %";
        lblAxialCoupling.Text = $"Axial conduction RMS: {t.AxialConductionRMS_Wm2 / 1e6:F2} MW/m² ({100 * t.AxialConductionRMS_Wm2 / Math.Max(t.ThroatHeatFlux_Wm2, 1):F0}% of q_throat)";

        var tDiag = t.Diagnostics;
        if (tDiag.CleanConvergence)
        {
            lblConvergence.Text = "Convergence: clean (no clamps, no max-iter hits)";
            lblConvergence.ForeColor = Color.DarkGreen;
        }
        else
        {
            lblConvergence.Text = $"Convergence: {tDiag.MaxWallTempIterationsHit} iter caps, {tDiag.ChannelWidthClampedCount} ch-w clamps, {tDiag.PressureClampedCount} P clamps, {tDiag.StationsInPseudocritical} pseudocrit";
            lblConvergence.ForeColor = tDiag.MaxWallTempIterationsHit > 0 ? Color.Red : Color.DarkOrange;
        }
    }

    // ── Warnings panel (structured severity + flat fallback) ─────────
    private void PopulateWarningsPanel(RegenGenerationResult r, RegenScoreResult? score)
    {
        var t = r.Thermal;
        var m = r.Manufacturing;

        // TIER A.3: structured warnings get a severity-prefixed layout.
        // The panel still renders as plain text (no table widget — keeps
        // layout simple and copy-pasteable) but Critical and Warn lines
        // sort first and get visible tags.
        var lines = new List<string>();
        if (score != null)
        {
            lines.Add($"Score: {score.TotalScore:F2}  ({RegenChamberOptimization.Profiles[cboProfile.SelectedIndex].Name})");
            lines.Add("");
            var structured = score.StructuredWarnings;
            if (structured is { Length: > 0 })
            {
                // Sort Critical → Warn → Info so the most important ones are visible first.
                var ordered = new List<SolverWarning>(structured);
                ordered.Sort((a, b) => ((int)b.Severity).CompareTo((int)a.Severity));
                foreach (var w in ordered)
                {
                    string tag = w.Severity switch
                    {
                        WarningSeverity.Critical => "[CRIT]",
                        WarningSeverity.Warn     => "[WARN]",
                        _                        => "[INFO]",
                    };
                    lines.Add($"{tag} {w.Code}: {w.Message}");
                }
            }
            else
            {
                // Fallback to legacy flat strings when StructuredWarnings wasn't populated.
                lines.AddRange(t.Warnings);
                lines.AddRange(m.Warnings);
            }
        }
        else
        {
            lines.AddRange(t.Warnings);
            lines.AddRange(m.Warnings);
        }
        // OOB-13 (issue #202): append causal gate explainer when violations exist.
        // The TextBox renders plain text, so the Markdown is legible as-is
        // (## headers, bullet points, etc.) without any conversion.
        if (score?.FeasibilityViolations is { Length: > 0 } violations)
        {
            var gateResult = new FeasibilityGateResult(
                IsFeasible: false,
                Violations: violations);
            if (lines.Count > 0)
                lines.Add("");
            lines.Add(GateExplainer.BuildMarkdown(gateResult, designHash: r.DesignHash));
        }

        txtWarnings.Text = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "";
        // Colour the whole box by the highest severity present so a glance
        // gives the overall feel (red = any Critical, amber = any Warn).
        // Sprint 14 / Track I / P17: single-pass scan with two bool
        // accumulators — no per-call lambda allocations from the previous
        // pair of `Array.Exists(ww, w => …)` calls.
        Color foreColour = Color.Black;
        if (score?.StructuredWarnings is { Length: > 0 } ww)
        {
            bool anyCritical = false, anyWarn = false;
            for (int i = 0; i < ww.Length; i++)
            {
                if (ww[i].Severity == WarningSeverity.Critical) { anyCritical = true; break; }
                if (ww[i].Severity == WarningSeverity.Warn)     anyWarn = true;
            }
            foreColour = anyCritical ? Color.Firebrick
                       : anyWarn     ? Color.DarkOrange
                                     : Color.Black;
        }
        txtWarnings.ForeColor = foreColour;
    }

    // ── Chilldown transient ──────────────────────────────────────────
    private void PopulateChilldownReadouts(RegenGenerationResult r)
    {
        if (r.Chilldown is { } chill)
        {
            string okTag = chill.IsAcceptable ? "" : "  ⚠ over budget";
            lblChilldownTime.Text      = $"Chilldown time: {chill.TimeToChill_s:F1} s (τ {chill.TimeConstant_s:F1} s){okTag}";
            lblChilldownTime.ForeColor = chill.IsAcceptable ? Color.DarkGreen : Color.Firebrick;
            lblChilldownProp.Text      = $"Propellant boiled off: {chill.PropellantMassConsumed_kg * 1000:F0} g";
            lblChilldownShock.Text     = $"Thermal-shock σ peak: {chill.PeakThermalShockStress_MPa:F0} MPa";
            lblChilldownRegime.Text    = $"Regime: {chill.Regime}";
        }
        else
        {
            lblChilldownTime.Text      = "Chilldown time: — (not run; opt in + cryogenic pair)";
            lblChilldownTime.ForeColor = Color.DimGray;
            lblChilldownProp.Text      = "Propellant boiled off: —";
            lblChilldownShock.Text     = "Thermal-shock σ peak: —";
            lblChilldownRegime.Text    = "Regime: —";
        }
    }

    // ── Start transient (time-to-90 %, overshoot, hard-start) ────────
    private void PopulateStartTransientReadouts(RegenGenerationResult r)
    {
        if (r.StartTransient is { } start)
        {
            string t90 = double.IsFinite(start.TimeTo90Pc_s)
                ? $"{start.TimeTo90Pc_s * 1000:F1} ms"
                : "did not reach 90 % within sim";
            lblStartTimeTo90.Text          = $"Time to 90 % Pc: {t90}";
            lblStartPeakOvershoot.Text     = $"Peak Pc overshoot: {start.PeakPressureOvershoot * 100:F0} %"
                                           + (start.HardStartRisk ? "  ⚠ HARD START" : "");
            lblStartPeakOvershoot.ForeColor = start.HardStartRisk ? Color.Firebrick : Color.DarkGreen;
            lblStartUnburned.Text          = $"Unburned at ignition: {start.UnburnedMassAtIgnition_kg * 1000:F1} g";
            pcChartPanel.SetSamples(start.Samples, r.Conditions.ChamberPressure_Pa);
        }
        else
        {
            lblStartTimeTo90.Text           = "Time to 90 % Pc: — (not run; opt in to enable)";
            lblStartTimeTo90.ForeColor      = Color.DimGray;
            lblStartPeakOvershoot.Text      = "Peak Pc overshoot: —";
            lblStartPeakOvershoot.ForeColor = Color.DimGray;
            lblStartUnburned.Text           = "Unburned at ignition: —";
            pcChartPanel.SetSamples(null, r.Conditions.ChamberPressure_Pa);
        }
    }

    // ── Turbopump (NPSH + shaft power + dry mass) ────────────────────
    private void PopulateTurbopumpReadouts(RegenGenerationResult r)
    {
        if (r.Turbopump is { } pump && pump.FuelPump is { } fp && pump.OxPump is { } op)
        {
            string ok = pump.NPSHFeasible ? "" : "  ⚠ NPSH";
            // NPSH readouts go through ThemeManager so High-Contrast
            // + Dark users see the traffic-light semantics with an
            // appropriate palette.
            lblPumpFuel.Text  = $"Fuel: {fp.HeadRise_m:F0} m head, {fp.ShaftPower_W / 1000:F1} kW, {fp.Rpm:F0} rpm "
                              + $"(NPSHA {fp.NPSHA_m:F1} / R {fp.NPSHR_m:F1} m){ok}";
            lblPumpFuel.ForeColor = ThemeManager.StatusForeground(
                fp.NPSHAcceptable ? PillSeverity.Pass : PillSeverity.Fail);
            lblPumpOx.Text    = $"Ox  : {op.HeadRise_m:F0} m head, {op.ShaftPower_W / 1000:F1} kW, {op.Rpm:F0} rpm "
                              + $"(NPSHA {op.NPSHA_m:F1} / R {op.NPSHR_m:F1} m){ok}";
            lblPumpOx.ForeColor = ThemeManager.StatusForeground(
                op.NPSHAcceptable ? PillSeverity.Pass : PillSeverity.Fail);
            string massDetail = pump.BatteryMass_kg > 0
                ? $"converter+battery: {pump.EstimatedDryMass_kg:F1} kg (incl. {pump.BatteryMass_kg:F1} kg battery)"
                : $"converter mass est.: {pump.EstimatedDryMass_kg:F1} kg";
            lblPumpTotal.Text = $"Total shaft: {pump.TotalShaftPower_W / 1000:F1} kW  |  "
                              + $"{massDetail} ({pump.Cycle})";
        }
        else
        {
            lblPumpFuel.Text      = "Fuel pump: — (PressureFed or not run)";
            lblPumpFuel.ForeColor = ThemeManager.StatusForeground(PillSeverity.Neutral);
            lblPumpOx.Text        = "Ox pump: —";
            lblPumpOx.ForeColor   = ThemeManager.StatusForeground(PillSeverity.Neutral);
            lblPumpTotal.Text     = "Total shaft / dry mass: —";
        }
    }

    // ── Aerospike sidecar readouts ───────────────────────────────────
    // Populated whenever GenerateWith ran an aerospike physics-only
    // build (r.Aerospike != null). The bell-chamber r.Thermal /
    // r.Contour readouts still reflect the fallback regen path so the
    // existing pills don't go blank on aerospike runs.
    private void PopulateAerospikeReadouts(RegenGenerationResult r)
    {
        if (r.Aerospike is { } aero)
        {
            var at = aero.Thermal;
            if (at is not null)
            {
                lblAerospikePlug.Text =
                    $"Aerospike plug: peak T {at.PeakGasSideWallT_K:F0} K @ x={at.PeakStation_X_mm:F1} mm,"
                    + $" coolant out {at.CoolantOutletT_K:F0} K, Q {at.TotalHeatLoad_W / 1000:F1} kW";
            }
            else
            {
                lblAerospikePlug.Text =
                    $"Aerospike plug: {aero.ThroatOuterRadius_mm * 2:F1} mm D_t,"
                    + $" {aero.TotalLength_mm:F1} mm total, mass {aero.EstimatedMass_g:F0} g";
            }

            if (aero.InjectorSizing is { } isz)
            {
                string okTag = isz.ClearanceOk ? "" : "  ⚠ clearance";
                lblAerospikeInjector.Text =
                    $"Aerospike injector: {isz.PatternSizing.ElementCount} elems on R_pc {isz.PitchCircleRadius_mm:F1} mm,"
                    + $" OD {isz.ElementOuterDiameter_mm:F2} mm, clr {isz.MinClearance_mm:F2} mm{okTag}";
                lblAerospikeInjector.ForeColor = isz.ClearanceOk ? Color.DarkGreen : Color.Firebrick;
            }
            else
            {
                lblAerospikeInjector.Text      = "Aerospike injector: — (no pattern set)";
                lblAerospikeInjector.ForeColor = Color.DimGray;
            }

            if (aero.InjectorFace is { } af)
            {
                lblAerospikeFace.Text =
                    $"Aerospike face T: {af.TFace_K:F0} K"
                    + $" (q {af.HeatFlux_Wm2 / 1e6:F1} MW/m², bore {af.BoreAreaFraction * 100:F1} %)";
            }
            else
            {
                lblAerospikeFace.Text      = "Aerospike face T: — (needs injector pattern)";
                lblAerospikeFace.ForeColor = Color.DimGray;
            }
        }
        else
        {
            lblAerospikePlug.Text           = "Aerospike plug: — (ChannelTopology != Aerospike)";
            lblAerospikePlug.ForeColor      = Color.DimGray;
            lblAerospikeInjector.Text       = "Aerospike injector: —";
            lblAerospikeInjector.ForeColor  = Color.DimGray;
            lblAerospikeFace.Text           = "Aerospike face T: —";
            lblAerospikeFace.ForeColor      = Color.DimGray;
        }
    }

    // ── Sprint 10 Track A: preburner regen-cooling readouts ──────────
    // Populated when IncludePreburnerRegenCooling is true AND the cycle
    // has a preburner (staged / gas-generator / FFSC). On FFSC we
    // surface the worst-case of the two preburners so the operator
    // sees the thermal-limiting side.
    private void PopulatePreburnerThermalReadouts(RegenGenerationResult r)
    {
        var mat = WallMaterials.All[r.Conditions.WallMaterialIndex];
        var preThermal   = r.Preburner?.Thermal;
        var oxPreThermal = r.OxidizerPreburner?.Thermal;

        PreburnerThermalResult? worstPre = null;
        string worstSide = "";
        if (preThermal is not null && oxPreThermal is not null)
        {
            if (preThermal.PeakWallT_K >= oxPreThermal.PeakWallT_K)
            { worstPre = preThermal;   worstSide = "fuel-rich"; }
            else
            { worstPre = oxPreThermal; worstSide = "ox-rich"; }
        }
        else if (preThermal   is not null) { worstPre = preThermal;   worstSide = "fuel-rich"; }
        else if (oxPreThermal is not null) { worstPre = oxPreThermal; worstSide = "ox-rich"; }

        if (worstPre is { } pt)
        {
            // Gate 18 (PREBURNER_WALL_TEMP) pulls the wall-material limit
            // from the chamber-wall material, matching the solver default.
            double matLimit = mat.MaxServiceTemp_K;
            bool overLimit = pt.PeakWallT_K > matLimit;
            lblPreburnerWallT.Text =
                $"Preburner peak wall T ({worstSide}): {pt.PeakWallT_K:F0} K (limit {matLimit:F0} K)";
            lblPreburnerWallT.ForeColor = overLimit ? Color.Firebrick : Color.DarkGreen;
            lblPreburnerCoolantOut.Text = $"Preburner coolant out: {pt.CoolantOutletT_K:F0} K";
            lblPreburnerHeatLoad.Text   = $"Preburner heat load: {pt.TotalHeatLoad_W / 1000:F1} kW";
        }
        else
        {
            lblPreburnerWallT.Text      = "Preburner peak wall T: — (opt in + preburner cycle)";
            lblPreburnerWallT.ForeColor = Color.DimGray;
            lblPreburnerCoolantOut.Text = "Preburner coolant out: —";
            lblPreburnerHeatLoad.Text   = "Preburner heat load: —";
        }
    }

    /// <summary>
    /// Sprint 27 (2026-04-23): populate the one-line LPBF printability
    /// summary label. Shows the advisor's recommended build axis, the
    /// worst overhang angle, and any gate-tripping violation count. Dim
    /// when the opt-in is off.
    /// </summary>
    private void PopulateLpbfPrintabilityReadout(RegenGenerationResult r)
    {
        if (r.Printability is not { } pr)
        {
            lblLpbfPrintability.Text = "Printability: — (opt-in off)";
            lblLpbfPrintability.ForeColor = Color.DimGray;
            return;
        }

        int violations = pr.Overhang.ViolationCount
                       + (pr.TrappedPowder?.PocketCount ?? 0)
                       + pr.DrainPath.ViolationCount;
        string axis = pr.Orientation?.RecommendedAxis ?? "—";
        double worst = pr.Overhang.WorstOverhangAngle_deg;
        string worstStr = double.IsNaN(worst) ? "—" : $"{worst:F0}°";

        lblLpbfPrintability.Text = violations == 0
            ? $"Printability ({pr.Material.DisplayName}): OK · worst β {worstStr} · "
            + $"recommend build along {axis}"
            : $"Printability ({pr.Material.DisplayName}): {violations} violation(s) · "
            + $"worst β {worstStr} · recommend build along {axis}";
        lblLpbfPrintability.ForeColor = violations == 0 ? Color.DarkGreen : Color.Firebrick;
    }
}
