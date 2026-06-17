// SafetyReport.cs — Test-stand operator / safety-reviewer Markdown artifact
// for the cold hydrostatic proof test. Distinct from ReportExport (the full
// design report); this one is scoped to the single question "is it safe to
// pressurise this chamber to N× MEOP with water?"

using System.Globalization;
using System.Text;
using Voxelforge.Combustion;
using Voxelforge.HeatTransfer;
using Voxelforge.Structure;

namespace Voxelforge.IO;

public static class SafetyReport
{
    public const string ReportSchemaVersion = "v1.0 (2026-04-27)";

    // PH-33 (2026-04-27): aligned with ASME BPVC §VIII Div 1 ground-test
    // convention (was 2.0, raised to 2.5 to match ProofTestAnalysis).
    private const double ElasticBurstMarginThreshold = 2.5;

    public static string BuildMarkdown(
        ProofTestResult proof,
        WallMaterial wall,
        double meop_Pa,
        double gasSideWallThickness_mm,
        double outerJacketThickness_mm = 0.0,
        StructuralSummary? hotFire = null,
        Combustion.StartTransientResult? startTransient = null,
        Combustion.ShutdownBlowdownResult? shutdownBlowdown = null,
        ChilldownResult? chilldown = null)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        bool burstOk = proof.BurstMarginFactor >= ElasticBurstMarginThreshold;
        bool overall = proof.Passes && burstOk;
        string status = overall ? "PASS" : "FAIL";

        sb.AppendLine("# Hydrostatic Proof Test — Safety Report");
        sb.AppendLine();
        sb.AppendLine($"**Status:** {status}  ");
        sb.AppendLine($"**Generated:** {DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci)} (local)  ");
        sb.AppendLine($"**Schema:** {ReportSchemaVersion}");
        if (!string.IsNullOrEmpty(proof.DesignHash))
            sb.AppendLine($"  \n**Design hash:** `{proof.DesignHash}`");
        sb.AppendLine();
        sb.AppendLine("> **Preliminary design — not flight-qualified.** Predictions are scoping-grade " +
                      "(±25–50 % on wall T, ±20 % on ΔP). This artifact is a go/no-go screen for the " +
                      "proof-test plan, not a substitute for FEA, CFD, or qualification testing. " +
                      "Verify the **design hash** above matches the as-built hardware before pressurising.");
        sb.AppendLine();

        sb.AppendLine("## Test summary");
        sb.AppendLine();
        sb.AppendLine("| Quantity | Value |");
        sb.AppendLine("| --- | ---: |");
        sb.AppendLine($"| MEOP (Maximum Expected Operating Pressure) | {meop_Pa / 1e6:F2} MPa ({meop_Pa / 6894.76:F0} psia) |");
        sb.AppendLine($"| Proof factor | {proof.ProofFactor:F2}× |");
        sb.AppendLine($"| Proof pressure | {proof.ProofPressure_Pa / 1e6:F2} MPa ({proof.ProofPressure_Pa / 6894.76:F0} psia) |");
        sb.AppendLine($"| Test medium | water (cold hydrostatic) |");
        sb.AppendLine($"| Test temperature | 293 K (20 °C) |");
        sb.AppendLine();

        sb.AppendLine("## Pass / fail criteria");
        sb.AppendLine();
        sb.AppendLine("The chamber must:");
        sb.AppendLine();
        sb.AppendLine("1. Survive proof pressure with no permanent deformation — peak von Mises stress " +
                      "< σ\\_y at test temperature (min safety factor ≥ 1.0).");
        sb.AppendLine($"2. Show ≥ {ElasticBurstMarginThreshold:F1}× elastic-burst margin over MEOP.");
        sb.AppendLine();
        sb.AppendLine("| Criterion | Threshold | Observed | Result |");
        sb.AppendLine("| --- | ---: | ---: | :---: |");
        sb.AppendLine($"| Min safety factor at proof pressure | ≥ 1.00 | {proof.ColdStructure.MinSafetyFactor:F2} | {(proof.Passes ? "PASS" : "FAIL")} |");
        sb.AppendLine($"| Elastic-burst margin vs MEOP | ≥ {ElasticBurstMarginThreshold:F2}× | {proof.BurstMarginFactor:F2}× | {(burstOk ? "PASS" : "FAIL")} |");
        sb.AppendLine($"| Yield exceeded at proof pressure | NO | {(proof.ColdStructure.YieldExceeded ? "YES" : "NO")} | {(proof.ColdStructure.YieldExceeded ? "FAIL" : "PASS")} |");
        sb.AppendLine();

        sb.AppendLine("## Structural margins at proof pressure (cold)");
        sb.AppendLine();
        sb.AppendLine("Computed at room temperature (no thermal gradient — hydrostatic test only).");
        sb.AppendLine();
        var c = proof.ColdStructure;
        sb.AppendLine("| Quantity | Value |");
        sb.AppendLine("| --- | ---: |");
        sb.AppendLine($"| Peak hoop stress | {c.PeakHoop_MPa:F0} MPa |");
        sb.AppendLine($"| Peak thermal stress | {c.PeakThermal_MPa:F0} MPa (cold test) |");
        sb.AppendLine($"| Peak combined (von Mises) | {c.PeakCombined_MPa:F0} MPa |");
        sb.AppendLine($"| Peak station index | {c.PeakStationIndex} (of {c.Stations.Length}) |");
        sb.AppendLine($"| Min safety factor | {c.MinSafetyFactor:F2} |");
        sb.AppendLine($"| Yield exceeded | {(c.YieldExceeded ? "YES" : "NO")} |");
        sb.AppendLine();

        sb.AppendLine("## Burst margin");
        sb.AppendLine();
        sb.AppendLine("Elastic-burst pressure is the pressure at which the worst-case station first " +
                      "yields at room temperature. Real ductile-material burst is typically " +
                      "1.2–1.5× this estimate due to strain hardening, but this artifact stops at " +
                      "yield for safety.");
        sb.AppendLine();
        sb.AppendLine("| Quantity | Value |");
        sb.AppendLine("| --- | ---: |");
        sb.AppendLine($"| Predicted elastic-burst pressure | {proof.ElasticBurstPressure_Pa / 1e6:F2} MPa |");
        sb.AppendLine($"| Burst margin vs MEOP | {proof.BurstMarginFactor:F2}× |");
        sb.AppendLine($"| Burst margin vs proof pressure | {proof.ElasticBurstPressure_Pa / Math.Max(proof.ProofPressure_Pa, 1):F2}× |");
        sb.AppendLine();

        sb.AppendLine("## Wall + material data");
        sb.AppendLine();
        sb.AppendLine("| Quantity | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Wall material | {wall.Name} |");
        sb.AppendLine($"| Gas-side liner thickness | {gasSideWallThickness_mm:F2} mm |");
        sb.AppendLine($"| Outer jacket thickness | {outerJacketThickness_mm:F2} mm |");
        sb.AppendLine($"| Maximum service temperature | {wall.MaxServiceTemp_K:F0} K |");
        sb.AppendLine($"| Density | {wall.Density_kgm3:F0} kg/m³ |");
        sb.AppendLine($"| Data source | {wall.DataSource} |");
        sb.AppendLine($"| LPBF process note | {wall.LPBFProcessNote} |");
        sb.AppendLine($"| Certification status | {wall.CertificationStatus} |");
        sb.AppendLine();

        sb.AppendLine("### Yield strength vs temperature");
        sb.AppendLine();
        sb.AppendLine("Linear interpolation between cold (300 K) and hot (800 K) anchors. " +
                      "Above 800 K the model holds the hot value; below 300 K it holds the cold value.");
        sb.AppendLine();
        sb.AppendLine("| T [K] | σ\\_y [MPa] |");
        sb.AppendLine("| ---: | ---: |");
        foreach (double T_K in new[] { 293.0, 400.0, 500.0, 600.0, 700.0, 800.0, 900.0 })
            sb.AppendLine($"| {T_K:F0} | {wall.YieldStrengthAt_MPa(T_K):F0} |");
        sb.AppendLine();

        if (hotFire != null)
        {
            sb.AppendLine("## Hot-fire structural margins (context only)");
            sb.AppendLine();
            sb.AppendLine("Independent of the cold proof test above — these reflect the chamber " +
                          "under steady-state hot-fire loads. Listed here so the safety reviewer " +
                          "can see the in-service stress regime that survived this proof.");
            sb.AppendLine();
            sb.AppendLine("| Quantity | Value |");
            sb.AppendLine("| --- | ---: |");
            sb.AppendLine($"| Peak hoop stress (hot) | {hotFire.PeakHoop_MPa:F0} MPa |");
            sb.AppendLine($"| Peak thermal stress (hot) | {hotFire.PeakThermal_MPa:F0} MPa |");
            sb.AppendLine($"| Peak combined VM (hot) | {hotFire.PeakCombined_MPa:F0} MPa |");
            sb.AppendLine($"| Min safety factor (hot) | {hotFire.MinSafetyFactor:F2} |");
            sb.AppendLine($"| Yield exceeded (hot) | {(hotFire.YieldExceeded ? "YES" : "NO")} |");
            sb.AppendLine();
        }

        // Hot-fire Item 4 close-out (2026-04-28): startup / shutdown
        // sequence summary. Each sub-section appears only when the
        // corresponding transient analysis was run.
        if (chilldown != null || startTransient != null || shutdownBlowdown != null)
        {
            sb.AppendLine("## Startup / Shutdown sequence (context only)");
            sb.AppendLine();
            sb.AppendLine("Lumped 0-D transient predictions for the engine's pre-fire chilldown, " +
                          "main-stage start-up, and shutdown / blowdown phases. These are scoping " +
                          "estimates (steady-state c\\*, single τ, no MR scrambling at low ṁ) and " +
                          "are intended to flag operator-visible issues before hot fire — not to " +
                          "qualify the start sequence.");
            sb.AppendLine();

            if (chilldown != null)
            {
                sb.AppendLine("### Pre-fire chilldown");
                sb.AppendLine();
                sb.AppendLine("| Quantity | Value |");
                sb.AppendLine("| --- | ---: |");
                sb.AppendLine($"| Time to chill | {chilldown.TimeToChill_s.ToString("F1", ci)} s |");
                sb.AppendLine($"| Time constant τ | {chilldown.TimeConstant_s.ToString("F1", ci)} s |");
                sb.AppendLine($"| Boil-off mass | {chilldown.PropellantMassConsumed_kg.ToString("F2", ci)} kg |");
                sb.AppendLine($"| Peak thermal-shock stress | {chilldown.PeakThermalShockStress_MPa.ToString("F0", ci)} MPa |");
                sb.AppendLine($"| Regime | {chilldown.Regime} |");
                sb.AppendLine($"| Within budget | {(chilldown.IsAcceptable ? "YES" : "NO")} |");
                sb.AppendLine();
            }

            if (startTransient != null)
            {
                sb.AppendLine("### Main-stage start-up");
                sb.AppendLine();
                sb.AppendLine("| Quantity | Value |");
                sb.AppendLine("| --- | ---: |");
                sb.AppendLine($"| Time to 90 % Pc | {startTransient.TimeTo90Pc_s.ToString("F3", ci)} s |");
                sb.AppendLine($"| Ignition time | {startTransient.IgnitionTime_s.ToString("F3", ci)} s |");
                sb.AppendLine($"| Unburned propellant at ignition | {startTransient.UnburnedMassAtIgnition_kg.ToString("F3", ci)} kg |");
                sb.AppendLine($"| Peak chamber pressure | {(startTransient.PeakPressure_Pa / 1e6).ToString("F2", ci)} MPa |");
                sb.AppendLine($"| Peak overshoot vs target | {(startTransient.PeakPressureOvershoot * 100).ToString("F1", ci)} % |");
                sb.AppendLine($"| Hard-start risk flag | **{(startTransient.HardStartRisk ? "RISK" : "ok")}** |");
                sb.AppendLine();
            }

            if (shutdownBlowdown != null)
            {
                sb.AppendLine("### Shutdown / blowdown");
                sb.AppendLine();
                sb.AppendLine("| Quantity | Value |");
                sb.AppendLine("| --- | ---: |");
                sb.AppendLine($"| Time to 10 % Pc | {FormatNanAware(shutdownBlowdown.TimeTo10PctPc_s, "F3", "s", ci)} |");
                sb.AppendLine($"| Time to subcritical (1.1× ambient) | {FormatNanAware(shutdownBlowdown.TimeToSubcritical_s, "F3", "s", ci)} |");
                sb.AppendLine($"| Residual propellant burned | {shutdownBlowdown.ResidualPropellantBurned_kg.ToString("F3", ci)} kg |");
                sb.AppendLine($"| Residual propellant vented | {shutdownBlowdown.ResidualPropellantVented_kg.ToString("F3", ci)} kg |");
                sb.AppendLine($"| Total impulse loss vs ideal cutoff | {shutdownBlowdown.TotalImpulseLoss_Ns.ToString("F0", ci)} N·s |");
                sb.AppendLine();
            }
        }

        if (proof.Warnings.Length > 0)
        {
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            foreach (var w in proof.Warnings) sb.AppendLine($"- {w}");
            sb.AppendLine();
        }

        sb.AppendLine("## Operator notes");
        sb.AppendLine();
        sb.AppendLine("- Pressurise with water (or other inert incompressible fluid) at room temperature. " +
                      "Compressible-gas proof testing is **not** approved by this artifact — gas testing " +
                      "stores far more energy and demands separate hazard analysis.");
        sb.AppendLine("- Hold at proof pressure for the duration required by the standard you're " +
                      "certifying to (e.g. ASME BPVC VIII Div 1 UG-99: ≥ 30 minutes hold; NASA-STD-5012: " +
                      "duration per project test plan).");
        sb.AppendLine("- Inspect for visible deformation, leaks, and crack initiation around ports, " +
                      "flanges, and welds before declaring the test passed.");
        sb.AppendLine("- Helium leak-down at MEOP is a separate test, not covered by this artifact.");
        sb.AppendLine("- Re-run this report any time the wall thickness, material, or operating " +
                      "pressure changes — the design hash above is the integrity check.");
        sb.AppendLine();

        sb.AppendLine("## Limitations");
        sb.AppendLine();
        sb.AppendLine("This artifact is generated from a 1-D thermal solver + isotropic elastic shell model. " +
                      "It does **not** model:");
        sb.AppendLine();
        sb.AppendLine("- Localised stress concentrations around ports, flanges, or welds — local stress " +
                      "at those features may exceed the predicted peak by a stress-concentration factor " +
                      "of 2–4×.");
        sb.AppendLine("- Fatigue (LCF/HCF) — only static yield is checked. Cyclic proof testing or repeat " +
                      "use needs separate fatigue analysis.");
        sb.AppendLine("- Buckling under external over-pressure (unlikely for regen chambers but should " +
                      "be confirmed for evacuated jackets).");
        sb.AppendLine("- Fracture mechanics with measured flaw populations.");
        sb.AppendLine("- Material anisotropy from LPBF build direction — the yield-strength values above " +
                      "are isotropic averages.");
        sb.AppendLine();
        sb.AppendLine("For flight-qualification or above-MAWP service, follow this artifact with FEA at " +
                      "discontinuities, hot-fire validation, and a vendor-certified material card.");

        return sb.ToString();
    }

    public static void SaveMarkdown(
        string path,
        ProofTestResult proof,
        WallMaterial wall,
        double meop_Pa,
        double gasSideWallThickness_mm,
        double outerJacketThickness_mm = 0.0,
        StructuralSummary? hotFire = null,
        StartTransientResult? startTransient = null,
        ShutdownBlowdownResult? shutdownBlowdown = null,
        ChilldownResult? chilldown = null)
    {
        File.WriteAllText(path, BuildMarkdown(
            proof, wall, meop_Pa, gasSideWallThickness_mm, outerJacketThickness_mm,
            hotFire, startTransient, shutdownBlowdown, chilldown));
    }

    /// <summary>
    /// Format a double that might be NaN (the integrator returns NaN
    /// when a threshold wasn't crossed within the simulation window).
    /// Renders "—" for NaN so the operator can tell the predicted
    /// time wasn't reachable.
    /// </summary>
    private static string FormatNanAware(double value, string format, string unit, CultureInfo ci)
        => double.IsNaN(value) ? "—" : $"{value.ToString(format, ci)} {unit}";
}
