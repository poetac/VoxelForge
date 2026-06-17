// ExportMetadata.cs — OOB-15 (2026-04-29):
//
// Provenance helpers shared by the export side (3MF / STEP / future
// formats). Bundles three pieces of traceability metadata so a fired
// part is forever attributable to the design + commit + gate-pass
// state that produced it:
//
//   1. GitSha       — `git rev-parse HEAD` of the repo at export
//                     time. "unknown" if not in a git checkout or the
//                     subprocess fails (never throws).
//   2. SchemaVersion — pinned to `DesignPersistence.CurrentSchemaVersion`
//                      so the embedded design schema is self-describing.
//   3. GatePassManifest — compact string-form report of the
//                         feasibility-gate march outcome ("PASS" if
//                         all 47 gates passed; otherwise the
//                         comma-joined ConstraintId list of fired
//                         violations).
//
// SA-vector hash is intentionally NOT a separate helper: the existing
// `RegenGenerationResult.DesignHash` (computed via
// `DesignProvenance.Compute`) already provides the design-state
// fingerprint OOB-15 calls for. ThreeMFExport already embeds it under
// the `DesignHash` metadata key.
//
// All three are pure-string outputs to keep the consumer side
// language-agnostic — XML in 3MF, STEP property strings, or a JSON
// blob in some future format.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Voxelforge.Optimization;

namespace Voxelforge.IO;

public static class ExportMetadata
{
    /// <summary>
    /// Schema version of the design serialiser at the time of export.
    /// Mirrors <see cref="DesignPersistence.CurrentSchemaVersion"/>.
    /// </summary>
    public static string SchemaVersion => DesignPersistence.CurrentSchemaVersion;

    /// <summary>
    /// Compact gate-pass manifest. Returns "PASS" when
    /// <paramref name="gates"/> reports all gates clean; otherwise
    /// "FAIL: &lt;comma-joined ConstraintId list&gt;". Stable across
    /// equivalent (cond, design) inputs because
    /// <see cref="FeasibilityGate.Evaluate"/> returns violations in
    /// insertion order.
    /// </summary>
    public static string GatePassManifest(FeasibilityGateResult gates)
    {
        if (gates.IsFeasible) return "PASS";
        var ids = new string[gates.Violations.Length];
        for (int i = 0; i < gates.Violations.Length; i++) ids[i] = gates.Violations[i].ConstraintId;
        return "FAIL: " + string.Join(",", ids);
    }

    // Binary STL header: bytes 0-79 (80 bytes total).
    // ASCII STLs start with "solid " — binary STLs MUST NOT, so we use the
    // "vxf:" magic prefix which is unambiguously binary and voxelforge-stamped.
    private const string StlMagic = "vxf:";

    /// <summary>
    /// Overwrites the 80-byte binary-STL header with a voxelforge provenance
    /// stamp so every exported STL is forever attributable to the design +
    /// commit + schema version that produced it.
    ///
    /// Format (all ASCII, NUL-padded to exactly 80 bytes):
    /// <c>vxf:&lt;sha40&gt;|v&lt;schema&gt;[|&lt;hash16&gt;]</c>
    ///
    /// The <c>vxf:</c> magic avoids the ASCII-STL "solid " conflict.
    /// No-ops silently when the file does not exist, is shorter than 84 bytes
    /// (80-byte header + 4-byte triangle-count minimum), or cannot be written.
    /// </summary>
    /// <param name="stlPath">Path to a binary STL file produced by PicoGK / StlExporter.</param>
    /// <param name="designHash">
    /// Optional 16-hex design hash from
    /// <see cref="RegenGenerationResult.DesignHash"/>. Omit when not available.
    /// </param>
    public static void StampStlHeader(string stlPath, string? designHash = null)
    {
        if (!File.Exists(stlPath)) return;
        try
        {
            if (new FileInfo(stlPath).Length < 84) return; // header(80) + tri-count(4)

            string stamp = string.IsNullOrEmpty(designHash)
                ? $"{StlMagic}{GitSha()}|{SchemaVersion}"
                : $"{StlMagic}{GitSha()}|{SchemaVersion}|{designHash}";

            byte[] header = new byte[80]; // zero-initialised — pads any unused bytes
            byte[] stampBytes = Encoding.ASCII.GetBytes(stamp);
            Array.Copy(stampBytes, header, Math.Min(stampBytes.Length, 80));

            using var fs = new FileStream(stlPath, FileMode.Open, FileAccess.Write, FileShare.None);
            fs.Write(header, 0, 80);
        }
        catch
        {
            // Non-fatal: a stamp failure must never block the export workflow.
        }
    }

    /// <summary>
    /// Reads back the provenance stamp previously written by
    /// <see cref="StampStlHeader"/>. Returns <c>null</c> when the file does
    /// not exist, is too short, or does not carry the <c>vxf:</c> magic.
    /// </summary>
    public static string? ReadStlHeaderStamp(string stlPath)
    {
        if (!File.Exists(stlPath)) return null;
        try
        {
            if (new FileInfo(stlPath).Length < 84) return null;
            byte[] header = new byte[80];
            using var fs = new FileStream(stlPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int read = fs.Read(header, 0, 80);
            if (read < 4) return null;
            string raw = Encoding.ASCII.GetString(header).TrimEnd('\0');
            return raw.StartsWith(StlMagic, StringComparison.Ordinal) ? raw : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// `git rev-parse HEAD` for the repo at export time, or
    /// <c>"unknown"</c> if not in a git checkout or the subprocess
    /// fails. Cached per-process; never throws.
    /// </summary>
    /// <remarks>
    /// Audit 01-security L6/L7: <c>FileName = "git"</c> resolves via
    /// the OS PATH. See <c>MachineInfo.GitSha()</c> for the shared
    /// threat-model write-up explaining why this does not materially
    /// widen the workstation attack surface.
    /// </remarks>
    private static string? _gitShaCached;
    public static string GitSha()
    {
        if (_gitShaCached != null) return _gitShaCached;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) { _gitShaCached = "unknown"; return _gitShaCached; }
            string sha = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            _gitShaCached = (p.ExitCode == 0 && sha.Length == 40) ? sha : "unknown";
        }
        catch
        {
            _gitShaCached = "unknown";
        }
        return _gitShaCached;
    }
}
