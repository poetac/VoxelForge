namespace Voxelforge.Marine.IO;

internal static class MarineSchemaVersion
{
    /// <summary>
    /// All accepted schema versions on read.
    ///   v1 — Wave-1 (AuvMidBody, Myring fairing only).
    ///   v2 — Wave-2 (HullFamily enum + CylindricalHemi addition).
    ///   v3 — Wave-3 Sprint M.W3 (SurfaceHull / Planing addition; identity
    ///        migration — 5 new init-only planing fields default to NaN and
    ///        round-trip AUV designs unchanged).
    ///   v4 — Wave-3 Sprint M.W4 (DisplacementSurface / Holtrop-Mennen
    ///        addition; identity migration — 4 new init-only displacement
    ///        fields (BeamWaterline_m, DraftDesign_m, BlockCoefficient,
    ///        DisplacementMass_kg) default to NaN and round-trip AUV /
    ///        Planing designs unchanged).
    ///   v5 — Wave-3 Sprint M.W5 (semi-displacement transition extension;
    ///        identity migration — 1 new init-only
    ///        EnableSemiDisplacementCorrection bool defaults to false.
    ///        Wave-1/W2/W3/W4 designs deserialise into v5 unchanged; the
    ///        SD correction is dormant until the flag is explicitly set.).
    /// </summary>
    internal const string Current = "v5";
    internal static readonly string[] Known = { "v1", "v2", "v3", "v4", "v5" };
    internal static bool IsSupported(string version) => Array.IndexOf(Known, version) >= 0;
}
