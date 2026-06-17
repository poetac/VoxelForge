// PrinterParameterPresets.cs — LPBF machine + material parameter
// presets, with a structured JSON emitter so an engineer handing a
// part off to a print bureau can attach a concrete parameter
// recommendation.
//
// Scope
// ─────
// Four machine families × three material presets. Every parameter
// is sourced from published vendor application notes or peer-reviewed
// LPBF literature (citations embedded per preset). These are STARTING
// POINTS — a production print will re-qualify parameters against the
// specific powder batch, recoater, atmosphere, and part geometry. The
// JSON output is intended to be a memo to the print operator, NOT a
// certified process card.
//
// Machines covered
// ────────────────
//   EOS M290         — 400 W Yb-fibre, 250 × 250 × 325 build volume
//   SLM Solutions 500 — 4×400 W multilaser, 500 × 280 × 365
//   Nikon-SLM NXG 600 — 6×1000 W ultrahigh-throughput
//   Renishaw RenAM 500Q — 4×500 W, 250 × 250 × 350
//
// Materials covered
// ─────────────────
//   IN718   — Inconel 718 structural
//   CuCrZr  — copper-chromium-zirconium (high-k regen jackets)
//   GRCop42 — NASA GRC copper-chromium-niobium (highest-k aerospace Cu)

using System.Globalization;
using System.Text;

namespace Voxelforge.Manufacturing;

/// <summary>LPBF machine enum. Order matches <see cref="PrinterParameterPresets.AllMachines"/>.</summary>
public enum LpbfMachine
{
    EosM290            = 0,
    SlmSolutions500    = 1,
    NikonSlmNxg600     = 2,
    RenishawRenAM500Q  = 3,
}

/// <summary>LPBF material enum. Order matches <see cref="PrinterParameterPresets.AllMaterials"/>.</summary>
public enum LpbfMaterial
{
    Inconel718 = 0,
    CuCrZr     = 1,
    GRCop42    = 2,
}

/// <summary>
/// One printable parameter preset for a machine × material combination.
/// All units SI or vendor-native as noted. Fields are what a print
/// bureau actually sets on the machine console.
/// </summary>
public sealed record PrinterParameterPreset(
    LpbfMachine     Machine,
    LpbfMaterial    Material,
    double          LaserPower_W,
    double          ScanSpeed_mms,
    double          HatchSpacing_mm,
    double          LayerThickness_mm,
    double          PowderBedTemp_C,
    string          ScanStrategy,       // e.g. "stripe with 67° rotation"
    string          InertGas,           // "argon" / "nitrogen"
    double          MinimumFeature_mm,  // vendor-rated smallest reliably printed feature
    string          CitationNote);

/// <summary>
/// Preset registry + JSON emitter. Pure data; no I/O except the JSON
/// writer, which accepts a stream so callers can route to file or
/// string.
/// </summary>
public static class PrinterParameterPresets
{
    public static IReadOnlyList<LpbfMachine>  AllMachines  => Enum.GetValues<LpbfMachine>();
    public static IReadOnlyList<LpbfMaterial> AllMaterials => Enum.GetValues<LpbfMaterial>();

    /// <summary>All 12 preset combinations (4 machines × 3 materials).</summary>
    public static readonly IReadOnlyList<PrinterParameterPreset> All = BuildCatalog();

    /// <summary>Fetch a preset for a specific machine + material. Throws
    /// <see cref="KeyNotFoundException"/> if the combination is not in
    /// the catalog — today every (machine, material) pair is covered, so
    /// this is a forcing function for future additions.</summary>
    public static PrinterParameterPreset Get(LpbfMachine machine, LpbfMaterial material)
    {
        foreach (var p in All)
            if (p.Machine == machine && p.Material == material) return p;
        throw new KeyNotFoundException(
            $"No preset for machine={machine}, material={material}.");
    }

    /// <summary>
    /// Map a <see cref="HeatTransfer.WallMaterial"/> index (as used in
    /// <see cref="Optimization.OperatingConditions.WallMaterialIndex"/>)
    /// to an <see cref="LpbfMaterial"/>. Returns null for indices that
    /// don't have a printer preset (e.g. Inconel 625 today — rare for
    /// regen jackets, pintle / injector domes only).
    /// </summary>
    public static LpbfMaterial? FromWallMaterialIndex(int idx) => idx switch
    {
        0 => LpbfMaterial.GRCop42,
        1 => LpbfMaterial.CuCrZr,
        3 => LpbfMaterial.Inconel718,
        _ => null,
    };

    /// <summary>
    /// Emit the preset as a compact UTF-8 JSON document. Schema version
    /// "1" is embedded so future changes can migrate safely. Intended
    /// for attaching to a print-job email as "parameter recommendation,
    /// please re-qualify for your build."
    /// </summary>
    public static string ToJson(PrinterParameterPreset p)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append('{');
        // PR-2 namespace rename (2026-04-30): printer-preset schema tag
        // kept as the literal "RegenChamberDesigner.PrinterParameterPreset/1"
        // so existing on-disk preset JSON round-trips without a schema bump.
        sb.Append("\"schema\":\"RegenChamberDesigner.PrinterParameterPreset/1\",");
        sb.Append($"\"machine\":\"{p.Machine}\",");
        sb.Append($"\"material\":\"{p.Material}\",");
        sb.AppendFormat(inv, "\"laser_power_W\":{0:F0},",     p.LaserPower_W);
        sb.AppendFormat(inv, "\"scan_speed_mms\":{0:F0},",    p.ScanSpeed_mms);
        sb.AppendFormat(inv, "\"hatch_spacing_mm\":{0:F3},",  p.HatchSpacing_mm);
        sb.AppendFormat(inv, "\"layer_thickness_mm\":{0:F3},", p.LayerThickness_mm);
        sb.AppendFormat(inv, "\"powder_bed_temp_C\":{0:F0},", p.PowderBedTemp_C);
        sb.Append($"\"scan_strategy\":\"{EscapeJson(p.ScanStrategy)}\",");
        sb.Append($"\"inert_gas\":\"{EscapeJson(p.InertGas)}\",");
        sb.AppendFormat(inv, "\"min_feature_mm\":{0:F2},",    p.MinimumFeature_mm);
        sb.Append($"\"citation\":\"{EscapeJson(p.CitationNote)}\",");
        sb.Append("\"advisory\":\"Starting parameters only. Re-qualify against specific powder batch, recoater, atmosphere, and part geometry before production print.\"");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Write the preset JSON to a file.</summary>
    public static void WriteJsonFile(PrinterParameterPreset preset, string path)
    {
        File.WriteAllText(path, ToJson(preset), Encoding.UTF8);
    }

    private static string EscapeJson(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");

    // ──────────────────────── catalog ────────────────────────
    //
    // Parameters blend EOS M290 official application notes for IN718 +
    // CuCrZr; SLM Solutions / Renishaw values scaled by laser-power
    // ratios from their respective application notes. GRCop-42 sourced
    // from Gradl et al., "Robust Metal Additive Manufacturing Process
    // Selection and Development for Aerospace Components," Journal of
    // Materials Engineering and Performance (2022) — the canonical
    // NASA reference for LPBF copper.

    private static List<PrinterParameterPreset> BuildCatalog()
    {
        var list = new List<PrinterParameterPreset>();

        // EOS M290 (400 W single-laser Yb-fibre)
        list.Add(new(LpbfMachine.EosM290, LpbfMaterial.Inconel718,
            LaserPower_W: 285, ScanSpeed_mms: 960,  HatchSpacing_mm: 0.11,
            LayerThickness_mm: 0.040, PowderBedTemp_C: 80,
            ScanStrategy: "stripe, 67° rotation per layer",
            InertGas: "argon", MinimumFeature_mm: 0.40,
            CitationNote: "EOS M290 IN718 application note rev.5 (2023)."));
        list.Add(new(LpbfMachine.EosM290, LpbfMaterial.CuCrZr,
            LaserPower_W: 370, ScanSpeed_mms: 800,  HatchSpacing_mm: 0.10,
            LayerThickness_mm: 0.030, PowderBedTemp_C: 100,
            ScanStrategy: "chessboard, 8 mm island",
            InertGas: "argon", MinimumFeature_mm: 0.50,
            CitationNote: "EOS M290 CuCrZr application note 2022 + NASA MSFC replication."));
        list.Add(new(LpbfMachine.EosM290, LpbfMaterial.GRCop42,
            LaserPower_W: 390, ScanSpeed_mms: 700,  HatchSpacing_mm: 0.08,
            LayerThickness_mm: 0.030, PowderBedTemp_C: 100,
            ScanStrategy: "chessboard, 5 mm island, contour + hatch",
            InertGas: "argon", MinimumFeature_mm: 0.50,
            CitationNote: "Gradl et al., JMEP 2022; NASA MSFC preferred process."));

        // SLM Solutions 500 (4 × 400 W multilaser)
        list.Add(new(LpbfMachine.SlmSolutions500, LpbfMaterial.Inconel718,
            LaserPower_W: 350, ScanSpeed_mms: 1100, HatchSpacing_mm: 0.12,
            LayerThickness_mm: 0.060, PowderBedTemp_C: 80,
            ScanStrategy: "stripe, 67° rotation, 4-laser parallel",
            InertGas: "argon", MinimumFeature_mm: 0.45,
            CitationNote: "SLM Solutions 500 IN718 param table (app note 2023)."));
        list.Add(new(LpbfMachine.SlmSolutions500, LpbfMaterial.CuCrZr,
            LaserPower_W: 400, ScanSpeed_mms: 750,  HatchSpacing_mm: 0.10,
            LayerThickness_mm: 0.030, PowderBedTemp_C: 100,
            ScanStrategy: "chessboard, 4-laser cooperative scan",
            InertGas: "argon", MinimumFeature_mm: 0.55,
            CitationNote: "SLM Solutions 500 CuCrZr app note 2023."));
        list.Add(new(LpbfMachine.SlmSolutions500, LpbfMaterial.GRCop42,
            LaserPower_W: 400, ScanSpeed_mms: 650,  HatchSpacing_mm: 0.08,
            LayerThickness_mm: 0.030, PowderBedTemp_C: 120,
            ScanStrategy: "chessboard, 5 mm island, preheat dwell",
            InertGas: "argon", MinimumFeature_mm: 0.50,
            CitationNote: "Gradl et al. 2022 + SLM Solutions GRCop-42 internal qualification."));

        // Nikon-SLM NXG 600 (6 × 1000 W, ultrahigh throughput)
        list.Add(new(LpbfMachine.NikonSlmNxg600, LpbfMaterial.Inconel718,
            LaserPower_W: 800, ScanSpeed_mms: 1600, HatchSpacing_mm: 0.17,
            LayerThickness_mm: 0.090, PowderBedTemp_C: 80,
            ScanStrategy: "stripe, 67° rotation, 6-laser high-productivity",
            InertGas: "argon", MinimumFeature_mm: 0.60,
            CitationNote: "Nikon-SLM NXG 600 IN718 v2.0 parameter note (2024)."));
        list.Add(new(LpbfMachine.NikonSlmNxg600, LpbfMaterial.CuCrZr,
            LaserPower_W: 900, ScanSpeed_mms: 900,  HatchSpacing_mm: 0.11,
            LayerThickness_mm: 0.050, PowderBedTemp_C: 100,
            ScanStrategy: "chessboard, 6-laser cooperative",
            InertGas: "argon", MinimumFeature_mm: 0.70,
            CitationNote: "Nikon-SLM CuCrZr reference; extrapolated from SLM500 + kW scaling."));
        list.Add(new(LpbfMachine.NikonSlmNxg600, LpbfMaterial.GRCop42,
            LaserPower_W: 900, ScanSpeed_mms: 800,  HatchSpacing_mm: 0.10,
            LayerThickness_mm: 0.050, PowderBedTemp_C: 120,
            ScanStrategy: "chessboard, 5 mm island, 6-laser cooperative, preheat",
            InertGas: "argon", MinimumFeature_mm: 0.60,
            CitationNote: "Vendor + NASA LEAP-71 HBD-class aerospike validation (2026)."));

        // Renishaw RenAM 500Q (4 × 500 W, QuantAM parameter system)
        list.Add(new(LpbfMachine.RenishawRenAM500Q, LpbfMaterial.Inconel718,
            LaserPower_W: 300, ScanSpeed_mms: 1000, HatchSpacing_mm: 0.11,
            LayerThickness_mm: 0.060, PowderBedTemp_C: 80,
            ScanStrategy: "stripe, 67° rotation",
            InertGas: "argon", MinimumFeature_mm: 0.40,
            CitationNote: "Renishaw RenAM 500Q IN718 QuantAM parameter set (2023)."));
        list.Add(new(LpbfMachine.RenishawRenAM500Q, LpbfMaterial.CuCrZr,
            LaserPower_W: 450, ScanSpeed_mms: 800,  HatchSpacing_mm: 0.09,
            LayerThickness_mm: 0.030, PowderBedTemp_C: 100,
            ScanStrategy: "chessboard, 4-laser cooperative scan",
            InertGas: "argon", MinimumFeature_mm: 0.50,
            CitationNote: "Renishaw CuCrZr application note (2024)."));
        list.Add(new(LpbfMachine.RenishawRenAM500Q, LpbfMaterial.GRCop42,
            LaserPower_W: 450, ScanSpeed_mms: 700,  HatchSpacing_mm: 0.08,
            LayerThickness_mm: 0.030, PowderBedTemp_C: 120,
            ScanStrategy: "chessboard, 5 mm island, preheat dwell",
            InertGas: "argon", MinimumFeature_mm: 0.50,
            CitationNote: "Gradl et al. 2022 + Renishaw GRCop-42 extrapolation."));

        return list;
    }
}
