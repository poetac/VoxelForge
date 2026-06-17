using System;

namespace Voxelforge.Nuclear.IO;

internal static class NuclearSchemaVersion
{
    /// <summary>
    /// All accepted schema versions on read.
    ///   v1 — Wave-1 (NERVA-class lumped-reactor scaffold).
    ///   v2 — Wave-2 Sprint NU.W2 (per-pin heat-conduction model; identity
    ///        migration — 6 new init-only fuel-pin fields default to NaN/0
    ///        and round-trip Wave-1 designs unchanged).
    ///   v3 — Wave-3 Sprint NU.W3 (bimodal NTR + closed-cycle He Brayton
    ///        gas loop; identity migration — 1 new init-only BimodalMode
    ///        enum + 5 new init-only Brayton fields default to
    ///        Thrust / 0.0 / NaN and round-trip Wave-1/Wave-2 designs
    ///        unchanged).
    ///   v4 — Wave-3 Sprint NU.W4 (fuel material variants; identity
    ///        migration — 1 new init-only NuclearFuelMaterial enum
    ///        defaults to None and round-trips Wave-1/W2/W3 designs
    ///        unchanged because None resolves to UO₂-cermet anchors).
    ///   v5 — Wave-3 Sprint NU.W5 (uranium enrichment tiers; identity
    ///        migration — 1 new init-only UraniumEnrichment enum on
    ///        NuclearThermalDesign defaults to None and round-trips
    ///        Wave-1/W2/W3/W4 designs unchanged because None resolves
    ///        to HEU (4000 MW/m³ ceiling — matches the prior hard-coded
    ///        NERVA-baseline constant).
    /// </summary>
    internal const string Current = "v5";
    internal static readonly string[] Known = { "v1", "v2", "v3", "v4", "v5" };
    internal static bool IsSupported(string version) => Array.IndexOf(Known, version) >= 0;
}
