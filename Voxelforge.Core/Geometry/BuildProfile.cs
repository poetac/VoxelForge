// BuildProfile record — extracted to Core in A1. The companion BuildProfiler
// class stays in App/Geometry/BuildProfile.cs because it tallies ticks during
// PicoGK voxelization.

using System.IO;

namespace Voxelforge.Geometry;

public sealed record BuildProfile(
    double Shell_ms,
    double Channels_ms,
    double ChannelVoxelise_ms,
    double ChannelBoolSubtract_ms,
    int    ChannelCount,
    double Manifolds_ms,
    double RadialPorts_ms,
    double Smoothen_ms,
    double InjectorFlange_ms,
    double MountingFlange_ms,
    double InjectorBores_ms,
    double LateFeatures_ms,
    double FinalMeasurements_ms,
    double Total_ms,
    double VoxelSize_mm,
    float  BBoxLx_mm,
    float  BBoxLy_mm,
    float  BBoxLz_mm,
    long   DenseEquivalentVoxels)
{
    /// <summary>
    /// Emit every stage timing as structured "BENCH key=value" lines on the
    /// given sink. The StlExporter subprocess pipes these on stdout; the
    /// Benchmarks console app writes them to stdout + a JSONL history file.
    /// The prefix lets the caller separate "grid build" BENCH lines from
    /// downstream meshing / export BENCH lines sharing the same log stream.
    /// </summary>
    public void EmitBench(TextWriter sink, string prefix = "grid_build_")
    {
        // One key per line keeps the parser simple — Program.cs's
        // ParseBenchMs/ParseBenchLong scan for "BENCH <key>=…" and stop at
        // the first non-numeric character. Mixing two keys on one line
        // would make the second one invisible.
        sink.WriteLine($"BENCH {prefix}shell_ms={Shell_ms:F1}");
        sink.WriteLine($"BENCH {prefix}channels_ms={Channels_ms:F1}");
        sink.WriteLine($"BENCH {prefix}channel_count={ChannelCount}");
        sink.WriteLine($"BENCH {prefix}channel_voxelise_ms={ChannelVoxelise_ms:F1}");
        sink.WriteLine($"BENCH {prefix}channel_boolsub_ms={ChannelBoolSubtract_ms:F1}");
        sink.WriteLine($"BENCH {prefix}manifolds_ms={Manifolds_ms:F1}");
        sink.WriteLine($"BENCH {prefix}radial_ports_ms={RadialPorts_ms:F1}");
        sink.WriteLine($"BENCH {prefix}smoothen_ms={Smoothen_ms:F1}");
        sink.WriteLine($"BENCH {prefix}injflange_ms={InjectorFlange_ms:F1}");
        sink.WriteLine($"BENCH {prefix}mountflange_ms={MountingFlange_ms:F1}");
        sink.WriteLine($"BENCH {prefix}injbores_ms={InjectorBores_ms:F1}");
        sink.WriteLine($"BENCH {prefix}late_ms={LateFeatures_ms:F1}");
        sink.WriteLine($"BENCH {prefix}final_ms={FinalMeasurements_ms:F1}");
        sink.WriteLine($"BENCH {prefix}total_ms={Total_ms:F1}");
        sink.WriteLine($"BENCH voxel_size_mm={VoxelSize_mm:F3}");
        sink.WriteLine($"BENCH bbox_lx_mm={BBoxLx_mm:F1}");
        sink.WriteLine($"BENCH bbox_ly_mm={BBoxLy_mm:F1}");
        sink.WriteLine($"BENCH bbox_lz_mm={BBoxLz_mm:F1}");
        sink.WriteLine($"BENCH dense_voxels={DenseEquivalentVoxels}");
    }
}
