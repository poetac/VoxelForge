// PortStandards.cs — Standard fluid-fitting thread presets for manifold and
// sensor ports. Ported from HeatExchangerDesigner/Geometry/PortStandards.cs
// and kept in sync: extending the spec list here is additive only.
//
// Covers four fitting families commonly used on LPBF rocket hardware:
//   • Plain        — drilled bore, no thread (weld / braze / interference)
//   • G-series     — ISO 228 BSPP parallel pipe thread (dominant in Europe,
//                    compatible with bonded-seal washers)
//   • NPT          — ANSI B1.20.1 tapered pipe thread (US standard; 1:16 taper,
//                    thread sealant required)
//   • SAE ORB      — SAE J1926 straight thread O-ring boss (preferred for
//                    cryogenic fluid lines, reusable, requires flat seal face)
//
// Dimensions are nominal thread major diameter, pitch (mm per turn), and
// taper-per-side (1/32 for NPT, 0 for parallel). The printable boss diameter
// is MajorDia + 2·Pitch — wide enough that the V-thread cutter (~0.5·Pitch
// radius) leaves enough crest material for a clean print.

namespace Voxelforge.Geometry;

public enum PortStandard
{
    Plain = 0,
    // Small metric straight threads (coarse DIN 13; sized for bench/test hardware)
    M5_0p8, M6_1p0, M8_1p0,
    // Miniature pipe threads (sub-1/8" — ideal for small test articles)
    G_1_16, NPT_1_16,
    // Standard plumbing sizes (real-world fittings; oversized on a
    // sub-kN thrust chamber — check the proportionality warning).
    G_1_8, G_1_4, G_3_8, G_1_2, G_3_4,
    NPT_1_8, NPT_1_4, NPT_3_8, NPT_1_2,
    SAE_4, SAE_6, SAE_8,
}

public sealed record PortSpec(
    PortStandard Standard,
    string Name,
    string Description,
    float MajorDiaMM,
    float PitchMM,
    float TaperPerSide,
    float NominalBoreMM,
    float ThreadLengthMM,
    bool RequiresSealFace)
{
    /// <summary>V-thread depth approximation: 0.6 × pitch.</summary>
    public float ThreadDepthMM => 0.6f * PitchMM;

    /// <summary>Minor diameter = major − 2·depth (root of thread).</summary>
    public float MinorDiaMM => MajorDiaMM - 2f * ThreadDepthMM;

    /// <summary>Outer boss diameter (shoulder collar surrounding the threads).</summary>
    public float BossDiaMM => MajorDiaMM + 2f * PitchMM;

    public bool IsThreaded => Standard != PortStandard.Plain;
}

public static class PortStandards
{
    private static readonly PortSpec[] _specs = new[]
    {
        new PortSpec(PortStandard.Plain, "Plain bore",
            "Un-threaded drilled passage (weld/braze/interference fit)",
            MajorDiaMM: 10f, PitchMM: 0f, TaperPerSide: 0f,
            NominalBoreMM: 10f, ThreadLengthMM: 0f, RequiresSealFace: false),

        // ── Metric coarse (DIN 13) — ideal for small chambers (< 1 kN) ──
        new PortSpec(PortStandard.M5_0p8, "M5 x 0.8",
            "ISO 724 metric coarse (bench/test-cell instrumentation)",
            MajorDiaMM: 5.00f, PitchMM: 0.80f, TaperPerSide: 0f,
            NominalBoreMM: 3.0f, ThreadLengthMM: 5.0f, RequiresSealFace: false),
        new PortSpec(PortStandard.M6_1p0, "M6 x 1.0",
            "ISO 724 metric coarse (small manifold ports)",
            MajorDiaMM: 6.00f, PitchMM: 1.00f, TaperPerSide: 0f,
            NominalBoreMM: 4.0f, ThreadLengthMM: 6.0f, RequiresSealFace: false),
        new PortSpec(PortStandard.M8_1p0, "M8 x 1.0 (fine)",
            "ISO 724 metric fine; common pressure-transducer thread",
            MajorDiaMM: 8.00f, PitchMM: 1.00f, TaperPerSide: 0f,
            NominalBoreMM: 5.0f, ThreadLengthMM: 8.0f, RequiresSealFace: false),

        // ── Miniature pipe threads (sub-1/8") — sized for sub-kN hardware ──
        new PortSpec(PortStandard.G_1_16, "G 1/16",
            "ISO 228 BSPP parallel miniature (bonded seal)",
            MajorDiaMM: 7.723f, PitchMM: 0.907f, TaperPerSide: 0f,
            NominalBoreMM: 4.0f, ThreadLengthMM: 5.0f, RequiresSealFace: false),
        new PortSpec(PortStandard.NPT_1_16, "1/16 NPT",
            "ANSI B1.20.1 miniature tapered (PTFE sealant)",
            MajorDiaMM: 8.26f, PitchMM: 0.941f, TaperPerSide: 1f / 32f,
            NominalBoreMM: 4.0f, ThreadLengthMM: 5.5f, RequiresSealFace: false),

        // ── ISO 228 BSPP parallel pipe thread (G-series) ──────────
        new PortSpec(PortStandard.G_1_8, "G 1/8",
            "ISO 228 BSPP parallel (bonded seal)",
            MajorDiaMM: 9.728f, PitchMM: 0.907f, TaperPerSide: 0f,
            NominalBoreMM: 6.5f, ThreadLengthMM: 7.0f, RequiresSealFace: false),
        new PortSpec(PortStandard.G_1_4, "G 1/4",
            "ISO 228 BSPP parallel (bonded seal)",
            MajorDiaMM: 13.157f, PitchMM: 1.337f, TaperPerSide: 0f,
            NominalBoreMM: 9.0f, ThreadLengthMM: 9.5f, RequiresSealFace: false),
        new PortSpec(PortStandard.G_3_8, "G 3/8",
            "ISO 228 BSPP parallel (bonded seal)",
            MajorDiaMM: 16.662f, PitchMM: 1.337f, TaperPerSide: 0f,
            NominalBoreMM: 12.0f, ThreadLengthMM: 10.5f, RequiresSealFace: false),
        new PortSpec(PortStandard.G_1_2, "G 1/2",
            "ISO 228 BSPP parallel (bonded seal)",
            MajorDiaMM: 20.955f, PitchMM: 1.814f, TaperPerSide: 0f,
            NominalBoreMM: 15.0f, ThreadLengthMM: 13.2f, RequiresSealFace: false),
        new PortSpec(PortStandard.G_3_4, "G 3/4",
            "ISO 228 BSPP parallel (bonded seal)",
            MajorDiaMM: 26.441f, PitchMM: 1.814f, TaperPerSide: 0f,
            NominalBoreMM: 20.0f, ThreadLengthMM: 14.5f, RequiresSealFace: false),

        // ── NPT tapered (ANSI B1.20.1, 1:16 = 1/32 per side) ──────
        new PortSpec(PortStandard.NPT_1_8, "1/8 NPT",
            "ANSI B1.20.1 tapered (PTFE sealant)",
            MajorDiaMM: 10.242f, PitchMM: 0.941f, TaperPerSide: 1f / 32f,
            NominalBoreMM: 6.5f, ThreadLengthMM: 6.7f, RequiresSealFace: false),
        new PortSpec(PortStandard.NPT_1_4, "1/4 NPT",
            "ANSI B1.20.1 tapered (PTFE sealant)",
            MajorDiaMM: 13.616f, PitchMM: 1.411f, TaperPerSide: 1f / 32f,
            NominalBoreMM: 9.0f, ThreadLengthMM: 10.2f, RequiresSealFace: false),
        new PortSpec(PortStandard.NPT_3_8, "3/8 NPT",
            "ANSI B1.20.1 tapered (PTFE sealant)",
            MajorDiaMM: 17.055f, PitchMM: 1.411f, TaperPerSide: 1f / 32f,
            NominalBoreMM: 12.0f, ThreadLengthMM: 10.4f, RequiresSealFace: false),
        new PortSpec(PortStandard.NPT_1_2, "1/2 NPT",
            "ANSI B1.20.1 tapered (PTFE sealant)",
            MajorDiaMM: 21.223f, PitchMM: 1.814f, TaperPerSide: 1f / 32f,
            NominalBoreMM: 15.0f, ThreadLengthMM: 13.6f, RequiresSealFace: false),

        // ── SAE J1926-1 ORB (UNF straight + O-ring face seal) ──────
        new PortSpec(PortStandard.SAE_4, "SAE-4 ORB",
            "SAE J1926 7/16-20 UNF O-ring boss (cryo-rated)",
            MajorDiaMM: 11.113f, PitchMM: 1.270f, TaperPerSide: 0f,
            NominalBoreMM: 6.5f, ThreadLengthMM: 11.5f, RequiresSealFace: true),
        new PortSpec(PortStandard.SAE_6, "SAE-6 ORB",
            "SAE J1926 9/16-18 UNF O-ring boss (cryo-rated)",
            MajorDiaMM: 14.288f, PitchMM: 1.411f, TaperPerSide: 0f,
            NominalBoreMM: 9.0f, ThreadLengthMM: 12.7f, RequiresSealFace: true),
        new PortSpec(PortStandard.SAE_8, "SAE-8 ORB",
            "SAE J1926 3/4-16 UNF O-ring boss (cryo-rated)",
            MajorDiaMM: 19.050f, PitchMM: 1.587f, TaperPerSide: 0f,
            NominalBoreMM: 12.5f, ThreadLengthMM: 14.3f, RequiresSealFace: true),
    };

    public static PortSpec Get(PortStandard s) => _specs[(int)s];

    public static string[] Names => _specs.Select(s => s.Name).ToArray();

    public static PortSpec[] All => _specs;
}
