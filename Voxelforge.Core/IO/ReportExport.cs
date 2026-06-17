// ReportExport.cs — Human-readable text report of a full design.

using System.Text;
using Voxelforge.Combustion.Stability;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.IO;

public static class ReportExport
{
    /// <summary>Software/reconstruction version stamp. Bump on any physics
    /// change that could invalidate previously-exported reports.</summary>
    public const string ReportSchemaVersion = "v4.2 (2026-04-18)";

    public static string Build(RegenGenerationResult r) => Build(r, bestSoFarIteration: 0);

    /// <summary>
    /// Build the report. When <paramref name="bestSoFarIteration"/> &gt; 0 the
    /// output is prefixed with a BEST-SO-FAR banner so an export during an
    /// active SA run is never mistaken for a converged result. When
    /// <paramref name="pareto"/> is non-null and non-empty a PARETO FRONT
    /// section lists the tracked (peak T, ΔP, mass) triples.
    /// </summary>
    public static string Build(
        RegenGenerationResult r,
        int bestSoFarIteration,
        IReadOnlyList<Voxelforge.Optimization.ParetoPoint>? pareto = null)
    {
        var sb = new StringBuilder();
        if (bestSoFarIteration > 0)
        {
            sb.AppendLine("################################################################");
            sb.AppendLine("#  BEST-SO-FAR  (optimization in progress, iteration "
                         + bestSoFarIteration + ")");
            sb.AppendLine("#  This is an intermediate candidate, not a converged result.");
            sb.AppendLine("#  Wait for the run to finish for final values.");
            sb.AppendLine("################################################################");
            sb.AppendLine();
        }
        sb.AppendLine("════════════════════════════════════════════════════════════════");
        sb.AppendLine("  REGENERATIVELY-COOLED THRUST CHAMBER DESIGN REPORT");
        sb.AppendLine($"  Generated           : {DateTime.Now:yyyy-MM-dd HH:mm} (local)");
        sb.AppendLine($"  Schema / solver ver : {ReportSchemaVersion}");
        sb.AppendLine($"  Model fidelity      : preliminary design (±25\u201350% wall T vs fire)");
        sb.AppendLine($"  Validated regime    : LOX/CH4 @ Pc 3\u201320 MPa, CuCrZr or GRCop-42.");
        sb.AppendLine($"                        Not qualified for flight / combustion stability.");
        if (!string.IsNullOrEmpty(r.DesignHash))
            sb.AppendLine($"  Design hash         : {r.DesignHash}");
        sb.AppendLine("════════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("OPERATING POINT");
        sb.AppendLine($"  Propellants     : {r.Gas.PropellantName}");
        sb.AppendLine($"  Thrust          : {r.Conditions.Thrust_N / 4.448:F0} lbf  ({r.Conditions.Thrust_N:F0} N)");
        sb.AppendLine($"  Chamber P       : {r.Conditions.ChamberPressure_Pa / 6894.76:F0} psia  ({r.Conditions.ChamberPressure_Pa / 1e6:F2} MPa)");
        sb.AppendLine($"  Mixture ratio   : {r.Conditions.MixtureRatio:F2} (O/F)");
        sb.AppendLine($"  Coolant inlet   : {r.Conditions.CoolantInletTemp_K:F0} K, {r.Conditions.CoolantInletPressure_Pa / 1e6:F2} MPa");
        sb.AppendLine($"  Wall material   : {WallMaterials.All[r.Conditions.WallMaterialIndex].Name}");
        sb.AppendLine();
        sb.AppendLine("COMBUSTION");
        sb.AppendLine($"  T_chamber       : {r.Gas.ChamberTemp_K:F0} K");
        sb.AppendLine($"  γ (gamma)       : {r.Gas.Gamma:F3}");
        sb.AppendLine($"  MW              : {r.Gas.MolecularWeight:F1} kg/kmol");
        sb.AppendLine($"  C* ideal        : {r.Gas.CStar_ms:F0} m/s");
        sb.AppendLine($"  C* actual       : {r.Derived.CStarActual_ms:F0} m/s ({r.Conditions.CStarEfficiency:P0} efficiency)");
        sb.AppendLine($"  ṁ total         : {r.Derived.TotalMassFlow_kgs:F3} kg/s");
        sb.AppendLine($"    fuel (CH4)    : {r.Derived.FuelMassFlow_kgs:F3} kg/s");
        sb.AppendLine($"    ox (LOX)      : {r.Derived.OxidizerMassFlow_kgs:F3} kg/s");
        sb.AppendLine($"  Isp vacuum      : {r.Derived.IdealIspVacuum_s:F0} s (ideal)");
        sb.AppendLine($"  Isp sea-level   : {r.Derived.IdealIspSeaLevel_s:F0} s (ideal)");
        sb.AppendLine($"  Cf              : {r.Derived.ThrustCoefficient:F3}");
        sb.AppendLine();
        sb.AppendLine("GEOMETRY");
        sb.AppendLine($"  D_throat        : {r.Derived.ThroatDiameter_mm:F2} mm");
        sb.AppendLine($"  D_exit          : {2 * r.Contour.ExitRadius_mm:F2} mm");
        sb.AppendLine($"  D_chamber       : {2 * r.Contour.ChamberRadius_mm:F2} mm");
        sb.AppendLine($"  Contraction ε_c : {r.Contour.ContractionRatio:F2}");
        sb.AppendLine($"  Expansion ε_e   : {r.Contour.ExpansionRatio:F2}");
        sb.AppendLine($"  L* (char. len)  : {r.Contour.CharacteristicLength_m * 1000:F0} mm = {r.Contour.CharacteristicLength_m * 39.37:F1} in");
        sb.AppendLine($"  L_chamber       : {r.Contour.ChamberLength_mm:F1} mm");
        sb.AppendLine($"  L_converging    : {r.Contour.ConvergingLength_mm:F1} mm");
        sb.AppendLine($"  L_bell          : {r.Contour.BellLength_mm:F1} mm");
        sb.AppendLine($"  L_total         : {r.Contour.TotalLength_mm:F1} mm");
        sb.AppendLine($"  OD              : {r.Geometry.BoundingDiameter_mm:F1} mm");
        sb.AppendLine();
        sb.AppendLine("COOLING JACKET");
        var g = r.Geometry;
        sb.AppendLine($"  Channels        : ~{r.Contour.Stations.Length} stations");
        sb.AppendLine($"  Inner wall      : {r.Conditions.ChamberPressure_Pa / 1e6:F2} MPa chamber pressure faces");
        sb.AppendLine($"  Solid volume    : {g.SolidVolume_mm3 / 1000:F1} cm³");
        sb.AppendLine($"  Mass            : {g.TotalMass_g:F0} g ({g.TotalMass_g / 454:F2} lb)");
        sb.AppendLine($"  Print cost est. : ${g.PrintedCost_USD:F0}");
        sb.AppendLine();
        sb.AppendLine("THERMAL PERFORMANCE");
        var t = r.Thermal;
        sb.AppendLine($"  Peak wall T (gas side) : {t.PeakGasSideWallT_K:F0} K  at station {t.PeakStationIndex}");
        sb.AppendLine($"  Peak wall T (cool side): {t.PeakCoolantSideWallT_K:F0} K");
        sb.AppendLine($"  Material limit         : {WallMaterials.All[r.Conditions.WallMaterialIndex].MaxServiceTemp_K:F0} K");
        sb.AppendLine($"  Wall T margin          : {t.WallMarginK:+0;-0;0} K");
        sb.AppendLine($"  Coolant T in → out     : {t.CoolantInletT_K:F0} K → {t.CoolantOutletT_K:F0} K  (ΔT = {t.CoolantOutletT_K - t.CoolantInletT_K:F0} K)");
        sb.AppendLine($"  Coolant P in → out     : {t.CoolantInletP_Pa / 1e6:F2} → {t.CoolantOutletP_Pa / 1e6:F2} MPa  (ΔP = {t.CoolantPressureDrop_Pa / 1e6:F2} MPa)");
        sb.AppendLine($"  ΔP / P_c               : {t.CoolantPressureDrop_Pa / r.Conditions.ChamberPressure_Pa:P1}");
        sb.AppendLine($"  Total heat load        : {t.TotalHeatLoad_W / 1000:F1} kW");
        sb.AppendLine($"  Throat heat flux       : {t.ThroatHeatFlux_Wm2 / 1e6:F1} MW/m²");

        var d = t.Diagnostics;
        sb.AppendLine();
        sb.AppendLine("SOLVER CONVERGENCE DIAGNOSTICS");
        sb.AppendLine($"  Clean convergence      : {(d.CleanConvergence ? "YES" : "NO")}");
        sb.AppendLine($"  Max-iter cap hits      : {d.MaxWallTempIterationsHit}  (of {t.Stations.Length} stations)");
        sb.AppendLine($"  Channel-width clamps   : {d.ChannelWidthClampedCount}");
        sb.AppendLine($"  Pressure floor clamps  : {d.PressureClampedCount}");
        sb.AppendLine($"  Pseudocritical zone    : {d.StationsInPseudocritical} stations");
        sb.AppendLine($"  Axial-conduction RMS   : {t.AxialConductionRMS_Wm2 / 1e6:F2} MW/m² ({100 * t.AxialConductionRMS_Wm2 / Math.Max(t.ThroatHeatFlux_Wm2, 1):F0}% of q_throat)");
        sb.AppendLine();
        sb.AppendLine("STRUCTURAL MARGINS");
        sb.AppendLine($"  Peak hoop stress       : {r.Stress.PeakHoop_MPa:F0} MPa");
        sb.AppendLine($"  Peak thermal stress    : {r.Stress.PeakThermal_MPa:F0} MPa");
        sb.AppendLine($"  Peak combined (VM)     : {r.Stress.PeakCombined_MPa:F0} MPa");
        sb.AppendLine($"  Min safety factor      : {r.Stress.MinSafetyFactor:F2}");
        if (r.Stress.YieldExceeded) sb.AppendLine("  *** YIELD EXCEEDED — DESIGN NOT SAFE AS-IS ***");
        sb.AppendLine();
        sb.AppendLine("MANUFACTURING (LPBF)");
        sb.AppendLine($"  Build height           : {r.Manufacturing.BuildHeight_mm:F0} mm");
        sb.AppendLine($"  Build diameter         : {r.Manufacturing.BuildDiameter_mm:F0} mm");
        sb.AppendLine($"  Layers (@ 30 μm)       : {r.Manufacturing.EstimatedLayers:N0}");
        sb.AppendLine($"  Est. build time        : {r.Manufacturing.EstimatedBuildHours:F1} h");
        sb.AppendLine($"  Est. total cost        : ${r.Manufacturing.EstimatedBuildCost_USD:F0}");
        sb.AppendLine($"  Min feature size       : {r.Manufacturing.MinFeatureSize_mm:F2} mm  [{(r.Manufacturing.FeatureSizeOK ? "OK" : "BELOW LPBF MIN")}]");
        sb.AppendLine($"  Orientation            : {r.Manufacturing.BuildOrientationRecommendation}");

        var oh = r.Manufacturing.Overhang;
        sb.AppendLine($"  Overhang (45\u00b0 rule)     : worst inner {oh.WorstOverhangAngle_deg_InnerWall:F0}\u00b0, outer {oh.WorstOverhangAngle_deg_OuterWall:F0}\u00b0");
        sb.AppendLine($"                           {oh.UnprintableStationCount} unprintable stations, {oh.MarginalStationCount} marginal");
        sb.AppendLine($"  Build orientation rec  : {oh.RecommendedBuildOrientation}");
        sb.AppendLine();
        sb.AppendLine("MATERIAL DATA PROVENANCE");
        sb.AppendLine($"  Material               : {r.Manufacturing.Material.Name}");
        sb.AppendLine($"  Source                 : {r.Manufacturing.Material.DataSource}");
        sb.AppendLine($"  LPBF process note      : {r.Manufacturing.Material.LPBFProcessNote}");
        sb.AppendLine($"  Cert status            : {r.Manufacturing.Material.CertificationStatus}");

        if (r.InjectorPattern != null)
        {
            sb.AppendLine();
            sb.AppendLine("INJECTOR ELEMENT PATTERN");
            sb.AppendLine($"  [preliminary-design — orifice sizing only, no breakup/combustion model]");
            var ip = r.InjectorPattern;
            sb.AppendLine($"  Element type        : {ip.ElementType}");
            sb.AppendLine($"  Element count       : {ip.ElementCount}");
            if (ip.PitchCircleRadius_mm > 0)
                sb.AppendLine($"  Pitch-circle radius : {ip.PitchCircleRadius_mm:F1} mm");
            sb.AppendLine($"  Film fraction       : {ip.OuterRowFilmFraction:P0} of fuel flow → wall film slot");
            sb.AppendLine($"  Cd (ox / fuel)      : {ip.CdOx:F2} / {ip.CdFuel:F2}");
            if (r.InjectorSizing != null)
            {
                var ps = r.InjectorSizing;
                sb.AppendLine($"  Per-element Ox area : {ps.PerElementResult.OxOrificeArea_mm2:F3} mm²  " +
                              $"(Ø{ps.PerElementResult.OxEquivDiameter_mm:F2} mm)");
                sb.AppendLine($"  Per-element Fu area : {ps.PerElementResult.FuelOrificeArea_mm2:F3} mm²  " +
                              $"(Ø{ps.PerElementResult.FuelEquivDiameter_mm:F2} mm)");
                sb.AppendLine($"  Velocity ratio (f/ox): {ps.PerElementResult.VelocityRatio:F2}");
                sb.AppendLine($"  Momentum ratio      : {ps.PerElementResult.MomentumRatio:F2}");
                sb.AppendLine($"  Total Ox area       : {ps.TotalOxArea_mm2:F2} mm²");
                sb.AppendLine($"  Total Fuel area     : {ps.TotalFuelArea_mm2:F2} mm²");
                sb.AppendLine($"  Flow split check    : {ps.FlowSplitCheck:F3}  (1.000 = exact)");
                if (ps.Warnings.Length > 0)
                    foreach (var w in ps.Warnings) sb.AppendLine($"    \u00b7 {w}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("COMBUSTION STABILITY SCREENING");
        sb.AppendLine($"  [preliminary-design — screening only, no coupled n-\u03c4]");
        var st = r.Stability;
        sb.AppendLine($"  Injector \u0394P_inj          : {st.Injector.DeltaPInj_Pa / 1e6:F2} MPa ({st.Injector.DeltaPInj_Pa / 6894.76:F0} psi)");
        sb.AppendLine($"  \u0394P/P_c (chug metric)     : {st.Chug.DeltaPRatio:P1}  [band {ChugAnalysis.LowerBand:P0}\u2013{ChugAnalysis.UpperBand:P0}]");
        sb.AppendLine($"  Chug rating               : {StabilityReport.RatingWord(st.Chug.Rating)} \u2014 {st.Chug.Reason}");
        sb.AppendLine($"  Sound speed (chamber)     : {st.Screech.SoundSpeed_ms:F0} m/s");
        sb.AppendLine($"  L1 (1st longitudinal)     : {st.Screech.L1_Hz:F0} Hz");
        sb.AppendLine($"  T1 (1st tangential)       : {st.Screech.T1_Hz:F0} Hz");
        sb.AppendLine($"  T2 (2nd tangential)       : {st.Screech.T2_Hz:F0} Hz");
        sb.AppendLine($"  Composite rating          : {StabilityReport.RatingWord(st.Composite)} \u2014 {st.CompositeReason}");
        foreach (var note in st.Notes) sb.AppendLine($"    \u00b7 {note}");

        if (r.Manufacturing.Warnings.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("WARNINGS");
            foreach (var w in r.Manufacturing.Warnings) sb.AppendLine($"  • {w}");
            foreach (var w in t.Warnings) sb.AppendLine($"  • {w}");
        }

        if (r.Manufacturing.Recommendations.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("RECOMMENDATIONS");
            foreach (var x in r.Manufacturing.Recommendations) sb.AppendLine($"  • {x}");
        }

        // PHASE 6: Pareto front section — only when the caller supplied one
        // (i.e. this report was written after an optimization run finished).
        if (pareto is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("PARETO FRONT  (non-dominated trade-off surface)");
            sb.AppendLine("  Axes: peak wall T, coolant ΔP, mass (all minimised).");
            sb.AppendLine($"  {pareto.Count} non-dominated candidates.");
            sb.AppendLine();
            sb.AppendLine("    iter   peak T [K]   ΔP [MPa]   mass [g]");
            // Sort by peak T so the listing runs from coldest to hottest.
            var ordered = new List<Voxelforge.Optimization.ParetoPoint>(pareto);
            ordered.Sort((a, b) => a.PeakWallT_K.CompareTo(b.PeakWallT_K));
            int maxRows = Math.Min(ordered.Count, 20);
            for (int i = 0; i < maxRows; i++)
            {
                var pt = ordered[i];
                sb.AppendLine($"    {pt.Iteration,5}   {pt.PeakWallT_K,10:F0}   {pt.CoolantDP_Pa / 1e6,8:F2}   {pt.Mass_g,8:F1}");
            }
            if (ordered.Count > maxRows)
                sb.AppendLine($"    … (+{ordered.Count - maxRows} more; truncated to 20 rows)");
        }

        sb.AppendLine();
        sb.AppendLine("────────────────────────────────────────────────────────────────");
        sb.AppendLine("  IMPORTANT: All predictions are scoping-grade (±25–50 % on wall T,");
        sb.AppendLine("  ±20 % on ΔP). Not suitable for qualification without CFD and");
        sb.AppendLine("  hot-fire validation.");
        sb.AppendLine("────────────────────────────────────────────────────────────────");

        return sb.ToString();
    }

    public static void SaveToFile(RegenGenerationResult r, string path)
    {
        File.WriteAllText(path, Build(r));
    }

    /// <summary>SPRINT 4: save variant that stamps a BEST-SO-FAR banner at the top.</summary>
    public static void SaveToFile(RegenGenerationResult r, string path, int bestSoFarIteration)
    {
        File.WriteAllText(path, Build(r, bestSoFarIteration));
    }

    /// <summary>PHASE 6: save variant that appends a Pareto-front section.</summary>
    public static void SaveToFile(
        RegenGenerationResult r, string path, int bestSoFarIteration,
        IReadOnlyList<Voxelforge.Optimization.ParetoPoint>? pareto)
    {
        File.WriteAllText(path, Build(r, bestSoFarIteration, pareto));
    }
}
