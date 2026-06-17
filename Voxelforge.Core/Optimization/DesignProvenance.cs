// DesignProvenance.cs — Deterministic short hash over (cond, design).
//
// Dependent analyses (proof test, tolerance sweep) must not display
// values computed against a stale design.
// Attaching a provenance hash to every result lets the UI compare against
// the current design hash and flag "STALE — re-run" when they diverge.
//
// Hash construction:
//   1. Serialise (OperatingConditions, RegenChamberDesign) as a JSON
//      object — stable ordering because System.Text.Json writes
//      properties in declaration order for records.
//   2. Compute SHA-256 of UTF-8 bytes.
//   3. Keep the first 8 bytes → 16 hex chars. 2^64 distinct hashes is
//      ample for a human diagnostic; collisions have no physics impact.
//
// The hash is purely a session-local diagnostic. It is NOT a cryptographic
// authentication token and does not need to be stable across versions —
// serialiser changes may legitimately change the hash without breaking
// any design.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Voxelforge.Optimization;

public static class DesignProvenance
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Compute a 16-char hex hash over (conditions, design). Stable within
    /// a session: identical (cond, design) → identical hash; any init-only
    /// record field change → different hash.
    /// </summary>
    public static string Compute(OperatingConditions cond, RegenChamberDesign design)
    {
        // Two separate serialisations avoid a wrapper-record allocation.
        string condJson   = JsonSerializer.Serialize(cond,   JsonOpts);
        string designJson = JsonSerializer.Serialize(design, JsonOpts);
        byte[] buf = Encoding.UTF8.GetBytes(condJson + "\u0000" + designJson);
        byte[] hash = SHA256.HashData(buf);
        var sb = new StringBuilder(16);
        for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }
}
