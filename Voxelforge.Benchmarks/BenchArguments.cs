using System.Diagnostics;
using System.Globalization;
using System.Text;
using PicoGK;
using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.IO;
using Voxelforge.Manufacturing;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class MonolithicArgs
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --monolithic "
        + "--propellant <LOX_CH4|LOX_H2|LOX_RP1> "
        + "--thrust <N> --pc <Pa> --eps <ratio> "
        + "[--cycle <PressureFed|GasGenerator|ElectricPump|OpenExpander|StagedCombustion|FullFlow>] "
        + "[--voxel <mm>] [--out <stl>] "
        + "[--fillet <mm>] [--no-flanges] [--no-preburner] "
        + "[--aerospike]";
}

internal static class AerospikeArgs
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --aerospike "
        + "--propellant <LOX_CH4|LOX_H2|LOX_RP1> "
        + "--thrust <N> --pc <Pa> --eps <ratio> "
        + "[--plug <0.15-1.00>] [--voxel <mm>] [--out <stl>] "
        + "[--channels] [--channel-count <N>] [--channel-width <mm>] "
        + "[--channel-depth <mm>] [--wall-material <0-3>] "
        + "[--pattern-elements <N>] [--out-vti <file.vti>]";
}

internal static class TurbopumpArgs
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --turbopump "
        + "--propellant <LOX_CH4|LOX_H2|LOX_RP1> "
        + "--thrust <N> --pc <Pa> "
        + "[--cycle <GasGenerator|ElectricPump|OpenExpander|StagedCombustion|FullFlow>] "
        + "[--side <fuel|ox>] [--voxel <mm>] [--out <stl>]";
}

internal sealed record AutonomousArgs(
    PropellantPair PropellantPair,
    double         Thrust_N,
    double         ChamberPressure_Pa,
    double         ExpansionRatio,
    double         VoxelSize_mm,
    string         OutStlPath,
    string?        AnalyticalPreviewPath,
    string?        OutVtiPath,
    string?        OutParamsPath,
    LpbfMachine    Machine,
    string?        InjectorType,
    InjectorFaceLayout? Layout,
    bool           PrintAdvisor,
    bool           PreviewOnly,
    bool           Strict,
    // Tri-state equilibrium override.
    //   null  → use AutoSeeder recommendation (auto-enable at Pc > 10 MPa)
    //   true  → force equilibrium correction on regardless of Pc
    //   false → force frozen tables on regardless of Pc
    bool?          EquilibriumOverride)
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --autonomous "
        + "--propellant <LOX_CH4|LOX_H2|LOX_RP1> "
        + "--thrust <N> --pc <Pa> --eps <ratio> "
        + "[--voxel <mm>] [--out <stl>] [--analytical-preview <stl>] "
        + "[--out-vti <file.vti>] [--out-params <file.json>] "
        + "[--machine <EosM290|SlmSolutions500|NikonSlmNxg600|RenishawRenAM500Q>] "
        + "[--injector-type <Coax|ImpingingDoublet|Pintle|Showerhead|Swirl>] "
        + "[--layout <Circular|Hexagonal|AnnularRows|Central>] "
        + "[--equilibrium | --frozen] "
        + "[--advisor] [--preview-only] [--strict]";

    public static AutonomousArgs Parse(string[] args)
    {
        PropellantPair? pair = null;
        double? thrust = null;
        double? pc     = null;
        double? eps    = null;
        double  voxel  = 0.6;  // default preview voxel
        string  outStl = "engine.stl";
        string? preview = null;
        string? outVti  = null;
        string? outParams = null;
        LpbfMachine machine = LpbfMachine.EosM290;
        string? injectorType = null;
        InjectorFaceLayout? layoutOverride = null;
        bool    advisor = false;
        bool    previewOnly = false;
        bool    strict = false;
        bool?   equilibriumOverride = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--propellant":
                    if (i + 1 >= args.Length) throw new ArgumentException("--propellant missing value");
                    if (!Enum.TryParse<PropellantPair>(args[++i], ignoreCase: true, out var p))
                        throw new ArgumentException(
                            $"--propellant must be one of: {string.Join(", ", Enum.GetNames<PropellantPair>())}");
                    pair = p;
                    break;
                case "--thrust":
                    if (i + 1 >= args.Length) throw new ArgumentException("--thrust missing value");
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var tn))
                        throw new ArgumentException($"--thrust must be a number, got '{args[i]}'");
                    thrust = tn;
                    break;
                case "--pc":
                    if (i + 1 >= args.Length) throw new ArgumentException("--pc missing value");
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var pcv))
                        throw new ArgumentException($"--pc must be a number in Pa, got '{args[i]}'");
                    pc = pcv;
                    break;
                case "--eps":
                    if (i + 1 >= args.Length) throw new ArgumentException("--eps missing value");
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var epsv))
                        throw new ArgumentException($"--eps must be a number, got '{args[i]}'");
                    eps = epsv;
                    break;
                case "--voxel":
                    if (i + 1 >= args.Length) throw new ArgumentException("--voxel missing value");
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        throw new ArgumentException($"--voxel must be a number in mm, got '{args[i]}'");
                    voxel = v;
                    break;
                case "--out":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out missing value");
                    outStl = args[++i];
                    break;
                case "--analytical-preview":
                    if (i + 1 >= args.Length) throw new ArgumentException("--analytical-preview missing value");
                    preview = args[++i];
                    break;
                case "--out-vti":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out-vti missing value");
                    outVti = args[++i];
                    break;
                case "--out-params":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out-params missing value");
                    outParams = args[++i];
                    break;
                case "--machine":
                    if (i + 1 >= args.Length) throw new ArgumentException("--machine missing value");
                    if (!Enum.TryParse<LpbfMachine>(args[++i], ignoreCase: true, out var m))
                        throw new ArgumentException(
                            $"--machine must be one of: {string.Join(", ", Enum.GetNames<LpbfMachine>())}");
                    machine = m;
                    break;
                case "--injector-type":
                    if (i + 1 >= args.Length) throw new ArgumentException("--injector-type missing value");
                    injectorType = args[++i];
                    if (Array.IndexOf(InjectorElementFactory.AllTypes, injectorType) < 0)
                        throw new ArgumentException(
                            $"--injector-type must be one of: {string.Join(", ", InjectorElementFactory.AllTypes)}");
                    break;
                case "--layout":
                    if (i + 1 >= args.Length) throw new ArgumentException("--layout missing value");
                    if (!Enum.TryParse<InjectorFaceLayout>(args[++i], ignoreCase: true, out var lay))
                        throw new ArgumentException(
                            $"--layout must be one of: {string.Join(", ", Enum.GetNames<InjectorFaceLayout>())}");
                    layoutOverride = lay;
                    break;
                case "--advisor":
                    advisor = true;
                    break;
                case "--preview-only":
                    previewOnly = true;
                    break;
                case "--strict":
                    strict = true;
                    break;
                case "--equilibrium":
                    if (equilibriumOverride == false)
                        throw new ArgumentException("--equilibrium and --frozen are mutually exclusive.");
                    equilibriumOverride = true;
                    break;
                case "--frozen":
                    if (equilibriumOverride == true)
                        throw new ArgumentException("--equilibrium and --frozen are mutually exclusive.");
                    equilibriumOverride = false;
                    break;
                case "-h":
                case "--help":
                    throw new ArgumentException(UsageLine);
                default:
                    throw new ArgumentException($"Unknown arg '{args[i]}'");
            }
        }

        if (pair   == null) throw new ArgumentException("Missing required --propellant.");
        if (thrust == null) throw new ArgumentException("Missing required --thrust.");
        if (pc     == null) throw new ArgumentException("Missing required --pc.");
        if (eps    == null) throw new ArgumentException("Missing required --eps.");
        if (voxel  < 0.05 || voxel > 2.0)
            throw new ArgumentOutOfRangeException(nameof(voxel),
                $"voxel size {voxel:F3} mm out of supported range 0.05–2.0 mm.");

        return new AutonomousArgs(
            pair.Value, thrust.Value, pc.Value, eps.Value,
            voxel, outStl, preview, outVti, outParams, machine,
            injectorType, layoutOverride,
            advisor, previewOnly, strict,
            equilibriumOverride);
    }
}

internal sealed record RunRecord(
    float         VoxelMM,
    BuildProfile? Profile,
    double        GenerateWithMs,
    double        MeshingMs,
    double        StlWriteMs,
    int           TriangleCount,
    long          StlBytes,
    string        Description)
{
    public void EmitBench(TextWriter sink)
    {
        sink.WriteLine($"BENCH run_description=\"{Description}\"");
        sink.WriteLine($"BENCH generatewith_total_ms={GenerateWithMs:F1}");
        Profile?.EmitBench(sink);
        if (TriangleCount > 0)
        {
            sink.WriteLine($"BENCH export_meshing_ms={MeshingMs:F1}");
            sink.WriteLine($"BENCH triangle_count={TriangleCount}");
            sink.WriteLine($"BENCH export_stl_write_ms={StlWriteMs:F1}");
            sink.WriteLine($"BENCH stl_bytes={StlBytes}");
        }
        sink.WriteLine($"BENCH total_ms={GenerateWithMs + MeshingMs + StlWriteMs:F1}");
    }

    public void AppendJsonl(string path)
    {
        // Schema v1 per ADR-013: provenance prefix from JsonlSchema,
        // payload fields below. Pre-format doubles with invariant
        // culture so non-US locales don't write "3,14".
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(1024);
        sb.Append('{');
        JsonlSchema.AppendProvenance(sb, JsonlSchema.BenchNames.Voxel);
        sb.Append($"\"voxel_mm\":{VoxelMM.ToString("F3", c)},");
        sb.Append($"\"generatewith_ms\":{GenerateWithMs.ToString("F1", c)},");
        if (Profile is { } p)
        {
            sb.Append($"\"shell_ms\":{p.Shell_ms.ToString("F1", c)},");
            sb.Append($"\"channels_ms\":{p.Channels_ms.ToString("F1", c)},");
            sb.Append($"\"channel_voxelise_ms\":{p.ChannelVoxelise_ms.ToString("F1", c)},");
            sb.Append($"\"channel_boolsub_ms\":{p.ChannelBoolSubtract_ms.ToString("F1", c)},");
            sb.Append($"\"channel_count\":{p.ChannelCount},");
            sb.Append($"\"manifolds_ms\":{p.Manifolds_ms.ToString("F1", c)},");
            sb.Append($"\"radial_ports_ms\":{p.RadialPorts_ms.ToString("F1", c)},");
            sb.Append($"\"smoothen_ms\":{p.Smoothen_ms.ToString("F1", c)},");
            sb.Append($"\"injflange_ms\":{p.InjectorFlange_ms.ToString("F1", c)},");
            sb.Append($"\"mountflange_ms\":{p.MountingFlange_ms.ToString("F1", c)},");
            sb.Append($"\"injbores_ms\":{p.InjectorBores_ms.ToString("F1", c)},");
            sb.Append($"\"late_ms\":{p.LateFeatures_ms.ToString("F1", c)},");
            sb.Append($"\"final_ms\":{p.FinalMeasurements_ms.ToString("F1", c)},");
            sb.Append($"\"grid_build_total_ms\":{p.Total_ms.ToString("F1", c)},");
            sb.Append($"\"dense_voxels\":{p.DenseEquivalentVoxels},");
            sb.Append($"\"bbox_lx_mm\":{p.BBoxLx_mm.ToString("F1", c)},");
            sb.Append($"\"bbox_ly_mm\":{p.BBoxLy_mm.ToString("F1", c)},");
            sb.Append($"\"bbox_lz_mm\":{p.BBoxLz_mm.ToString("F1", c)},");
        }
        sb.Append($"\"meshing_ms\":{MeshingMs.ToString("F1", c)},");
        sb.Append($"\"stl_write_ms\":{StlWriteMs.ToString("F1", c)},");
        sb.Append($"\"triangle_count\":{TriangleCount},");
        sb.Append($"\"stl_bytes\":{StlBytes},");
        JsonlSchema.AppendRecord(path, sb);
    }
}

internal sealed record BenchArgs(
    float   VoxelSizeMM,
    int     Repeat,
    string? JsonlOutPath,
    string? OutStlPath,
    bool    NoExport,
    int     Tiles)
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --voxel <mm> [--repeat <n>] [--tiles <N>] "
        + "[--out <jsonl>] [--out-stl <stl>] [--no-export]";

    public static BenchArgs Parse(string[] args)
    {
        float? voxel = null;
        int repeat = 3;
        string? jsonl = null;
        string? outStl = null;
        bool noExport = false;
        int tiles = 1;   // 1 = monolithic (default); ≥ 2 = axial-tiled build.

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--voxel":
                    if (i + 1 >= args.Length) throw new ArgumentException("--voxel missing value");
                    if (!float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        throw new ArgumentException($"--voxel must be a float, got '{args[i]}'");
                    voxel = v;
                    break;
                case "--repeat":
                    if (i + 1 >= args.Length) throw new ArgumentException("--repeat missing value");
                    if (!int.TryParse(args[++i], out repeat) || repeat < 1 || repeat > 50)
                        throw new ArgumentException($"--repeat must be 1..50, got '{args[i]}'");
                    break;
                case "--tiles":
                    if (i + 1 >= args.Length) throw new ArgumentException("--tiles missing value");
                    if (!int.TryParse(args[++i], out tiles) || tiles < 1 || tiles > 32)
                        throw new ArgumentException($"--tiles must be 1..32, got '{args[i]}'");
                    break;
                case "--out":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out missing value");
                    jsonl = args[++i];
                    break;
                case "--out-stl":
                    if (i + 1 >= args.Length) throw new ArgumentException("--out-stl missing value");
                    outStl = args[++i];
                    break;
                case "--no-export":
                    noExport = true;
                    break;
                case "-h":
                case "--help":
                    throw new ArgumentException(UsageLine);
                default:
                    throw new ArgumentException($"Unknown arg '{args[i]}'");
            }
        }
        if (voxel == null) throw new ArgumentException("Missing required --voxel.");
        if (voxel < 0.05f || voxel > 2f)
            throw new ArgumentOutOfRangeException(nameof(voxel),
                $"voxel size {voxel.Value:F3} mm out of supported range 0.05–2.0 mm");
        return new BenchArgs(voxel.Value, repeat, jsonl, outStl, noExport, tiles);
    }
}
