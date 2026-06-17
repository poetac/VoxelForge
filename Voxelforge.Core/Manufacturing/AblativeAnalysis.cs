// AblativeAnalysis.cs — Char-front recession analysis for an
// ablative-cooled chamber variant.
//
// Answers the sounding-rocket / hybrid-engine question: "given the
// gas-side heat-flux profile this contour produces, how thick does
// an ablative wall liner need to be to survive a fixed burn?"
// Runs as an additive analysis — when the user sets
// `RegenChamberDesign.AblativeMaterial != None`, the regen solver
// still computes the heat-flux profile (Bartz across stations) and
// this module folds it into a recession + char-depth budget.
//
// The recession-rate model is the classical power-law form widely
// used for screening ablative liners (Sutton 9e §16.3,
// NASA SP-8124):
//
//     ṙ = A · (q / q_ref)^n              [mm/s]
//
// where q_ref = 1 MW/m² and (A, n) are material-specific constants
// fit to ground-test ablation data. The char layer trails the
// recession surface by a roughly constant ratio for char-forming
// composites (silica- / carbon-phenolic); pure graphite recedes
// without an appreciable char layer.
//
// Burnthrough check:
//   recession_t = ṙ · t_burn                       (mm)
//   char_t      = recession_t · CharRatio          (mm)
//   penetrated  = (recession_t + char_t) · SF      (mm)
//   isAcceptable = penetrated < initialThickness_mm
//
// MVP simplifications:
//   • Constant heat flux over the burn (steady-state Bartz q).
//     Real recession is monotonically slowing as the wall recedes
//     (boundary-layer thickening), so this is conservative.
//   • Single safety factor applied to (recession + char), not split
//     by phenomenology. Default 1.5 covers typical recession
//     correlation scatter.
//   • No re-radiation, no pyrolysis-gas blockage. Both effects
//     reduce predicted recession; ignoring them is conservative.
//
// References:
//   Sutton & Biblarz, "Rocket Propulsion Elements" 9e §16.3;
//   NASA SP-8124 "Liquid Rocket Engine Ablative Thermal Protection
//   Systems" §3-4;
//   Hartfield et al., "Thermal Modeling of Ablative TPS for Liquid
//   Rocket Engines", AIAA-2007-5354 (recession power-law fits).

using Voxelforge.HeatTransfer;

namespace Voxelforge.Manufacturing;

public enum AblativeMaterial
{
    /// <summary>No ablative analysis run; result not attached to RegenGenerationResult.</summary>
    None = 0,
    /// <summary>Silica-phenolic (MX-2625-class) — moderate-q workhorse.</summary>
    SilicaPhenolic,
    /// <summary>Carbon-phenolic (FM-5055-class) — better at high q, common on solid-rocket nozzles.</summary>
    CarbonPhenolic,
    /// <summary>Pyrolytic graphite — minimal char layer, low recession, high density.</summary>
    GraphitePyrolytic,
}

/// <summary>
/// Specification for one ablative material. <see cref="RecessionCoefficient_mmps"/>
/// and <see cref="RecessionExponent"/> are the (A, n) constants in
/// ṙ = A · (q / q_ref)^n with q_ref = 1 MW/m².  <see cref="CharThicknessRatio"/>
/// is a multiplier on recession to estimate char-layer depth (charForming
/// composites ≈ 2; pure graphite ≈ 0.5). <see cref="MaxBurnDuration_s"/> is
/// a soft service limit beyond which the constant-q assumption breaks down.
/// </summary>
public readonly record struct AblativeMaterialSpec(
    AblativeMaterial Id,
    string DisplayName,
    double Density_kgm3,
    double CharTemperature_K,
    double RecessionCoefficient_mmps,    // A in ṙ = A · (q / q_ref)^n
    double RecessionExponent,            // n
    double CharThicknessRatio,           // char_depth ≈ ratio × recession
    double MaxBurnDuration_s,
    string Notes);

public static class AblativeMaterials
{
    /// <summary>Reference heat flux (W/m²) for the recession-rate power law.</summary>
    public const double ReferenceHeatFlux_Wm2 = 1.0e6;

    public static readonly System.Collections.Generic.Dictionary<AblativeMaterial, AblativeMaterialSpec> All =
        new()
        {
            [AblativeMaterial.None] = new(
                AblativeMaterial.None, "(no ablative)",
                Density_kgm3: 0, CharTemperature_K: 0,
                RecessionCoefficient_mmps: 0, RecessionExponent: 0,
                CharThicknessRatio: 0, MaxBurnDuration_s: 0,
                Notes: "Ablative analysis disabled."),

            [AblativeMaterial.SilicaPhenolic] = new(
                AblativeMaterial.SilicaPhenolic, "Silica-phenolic (MX-2625-class)",
                Density_kgm3: 1750, CharTemperature_K: 1500,
                RecessionCoefficient_mmps: 0.025, RecessionExponent: 0.65,
                CharThicknessRatio: 2.0, MaxBurnDuration_s: 120,
                Notes: "Workhorse for sounding-rocket / sub-orbital pressure-fed engines. Easy to manufacture."),

            [AblativeMaterial.CarbonPhenolic] = new(
                AblativeMaterial.CarbonPhenolic, "Carbon-phenolic (FM-5055-class)",
                Density_kgm3: 1450, CharTemperature_K: 1900,
                RecessionCoefficient_mmps: 0.012, RecessionExponent: 0.55,
                CharThicknessRatio: 1.5, MaxBurnDuration_s: 200,
                Notes: "Higher q tolerance and lower recession than silica; widely used on solid-rocket nozzles."),

            [AblativeMaterial.GraphitePyrolytic] = new(
                AblativeMaterial.GraphitePyrolytic, "Pyrolytic graphite",
                Density_kgm3: 2200, CharTemperature_K: 2500,
                RecessionCoefficient_mmps: 0.005, RecessionExponent: 0.50,
                CharThicknessRatio: 0.5, MaxBurnDuration_s: 300,
                Notes: "Lowest recession of the three; expensive and heavy. Minimal char layer."),
        };

    public static AblativeMaterialSpec SpecFor(AblativeMaterial m) => All[m];

    /// <summary>Steady-state recession rate (mm/s) at the supplied heat flux.</summary>
    public static double RecessionRate_mmps(AblativeMaterial m, double heatFlux_Wm2)
    {
        var spec = All[m];
        if (spec.RecessionCoefficient_mmps <= 0 || heatFlux_Wm2 <= 0) return 0;
        double qFrac = heatFlux_Wm2 / ReferenceHeatFlux_Wm2;
        return spec.RecessionCoefficient_mmps * System.Math.Pow(qFrac, spec.RecessionExponent);
    }
}

/// <summary>
/// Per-station recession outcome at end-of-burn.
/// </summary>
public sealed record AblativeStationRecession(
    int Index,
    double X_mm,
    double HeatFlux_Wm2,
    double Recession_mm,
    double CharDepth_mm,
    double Penetrated_mm,           // (recession + char) × safety factor
    double Margin_mm);              // initial thickness − penetrated; negative ⇒ burnthrough

/// <summary>
/// Output of <see cref="AblativeAnalysis.Run"/>.
/// </summary>
public sealed record AblativeResult(
    AblativeMaterial Material,
    double BurnDuration_s,
    double InitialThickness_mm,
    double SafetyFactor,
    double MaxRecession_mm,
    double MaxCharDepth_mm,
    int MaxRecessionStationIndex,
    double EstimatedBurnthroughTime_s,
    bool   IsAcceptable,
    AblativeStationRecession[] Stations,
    string[] Warnings);

public static class AblativeAnalysis
{
    /// <summary>
    /// Default safety factor applied to (recession + char_depth) before
    /// comparing against initial liner thickness. 1.5 covers typical
    /// recession-correlation scatter (Sutton §16.3 footnote).
    /// </summary>
    public const double DefaultSafetyFactor = 1.5;

    /// <summary>
    /// Run the recession analysis across every station in the regen
    /// solver's heat-flux profile. Returns null when the material is
    /// <see cref="AblativeMaterial.None"/> — caller does not attach a
    /// result in that case.
    /// </summary>
    public static AblativeResult? Run(
        AblativeMaterial material,
        RegenSolverOutputs thermal,
        double initialThickness_mm,
        double burnDuration_s,
        double safetyFactor = DefaultSafetyFactor)
    {
        if (material == AblativeMaterial.None) return null;

        var spec = AblativeMaterials.SpecFor(material);
        var warnings = new System.Collections.Generic.List<string>();

        if (burnDuration_s <= 0)
            return new AblativeResult(
                Material: material, BurnDuration_s: 0,
                InitialThickness_mm: initialThickness_mm,
                SafetyFactor: safetyFactor,
                MaxRecession_mm: 0, MaxCharDepth_mm: 0,
                MaxRecessionStationIndex: -1,
                EstimatedBurnthroughTime_s: double.PositiveInfinity,
                IsAcceptable: true,
                Stations: System.Array.Empty<AblativeStationRecession>(),
                Warnings: new[] { "Burn duration ≤ 0 — recession not evaluated." });

        if (burnDuration_s > spec.MaxBurnDuration_s)
            warnings.Add($"Burn {burnDuration_s:F0} s > {spec.DisplayName} typical service limit "
                       + $"{spec.MaxBurnDuration_s:F0} s — constant-q assumption may be optimistic.");

        if (initialThickness_mm <= 0)
            warnings.Add("Initial thickness ≤ 0 — burnthrough is reported but margin is meaningless.");

        var stations = new AblativeStationRecession[thermal.Stations.Length];
        double maxRec = 0, maxChar = 0;
        int maxIdx = -1;

        for (int i = 0; i < thermal.Stations.Length; i++)
        {
            var s = thermal.Stations[i];
            double r_dot = AblativeMaterials.RecessionRate_mmps(material, s.HeatFlux_Wm2);
            double rec = r_dot * burnDuration_s;
            double charDepth = rec * spec.CharThicknessRatio;
            double penetrated = (rec + charDepth) * safetyFactor;
            double margin = initialThickness_mm - penetrated;

            stations[i] = new AblativeStationRecession(
                Index: s.Index,
                X_mm: s.X_mm,
                HeatFlux_Wm2: s.HeatFlux_Wm2,
                Recession_mm: rec,
                CharDepth_mm: charDepth,
                Penetrated_mm: penetrated,
                Margin_mm: margin);

            if (rec > maxRec) { maxRec = rec; maxChar = charDepth; maxIdx = i; }
        }

        // Burnthrough time at the worst station: solve t such that
        // (rec(t) + char(t)) · SF = thickness. Linear in t.
        double tBurnthrough;
        if (maxIdx < 0 || maxRec <= 0)
            tBurnthrough = double.PositiveInfinity;
        else
        {
            double rDotPeak = AblativeMaterials.RecessionRate_mmps(
                material, thermal.Stations[maxIdx].HeatFlux_Wm2);
            double effRate = rDotPeak * (1.0 + spec.CharThicknessRatio) * safetyFactor;
            tBurnthrough = effRate > 0 ? initialThickness_mm / effRate : double.PositiveInfinity;
        }

        bool acceptable = (maxRec + maxChar) * safetyFactor < initialThickness_mm;

        return new AblativeResult(
            Material: material,
            BurnDuration_s: burnDuration_s,
            InitialThickness_mm: initialThickness_mm,
            SafetyFactor: safetyFactor,
            MaxRecession_mm: maxRec,
            MaxCharDepth_mm: maxChar,
            MaxRecessionStationIndex: maxIdx,
            EstimatedBurnthroughTime_s: tBurnthrough,
            IsAcceptable: acceptable,
            Stations: stations,
            Warnings: warnings.ToArray());
    }
}
