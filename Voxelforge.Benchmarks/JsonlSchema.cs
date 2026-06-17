// Schema-v1 JSONL emitter. ADR-013 is the spec; this file is the
// implementation. Every JSONL record under `baselines/` flows through
// AppendRecord / AppendProvenance so the 6-field provenance prefix is
// always present and always in the same order.
//
// Callers MUST format numerics with CultureInfo.InvariantCulture —
// otherwise a non-US locale writes "3,14" which downstream JSON
// parsers reject.

using System.Globalization;
using System.Text;

namespace Voxelforge.Benchmarks;

internal static class JsonlSchema
{
    public const int SchemaVersion = 1;

    // Canonical bench_name values. Adding a new value here does NOT
    // bump SchemaVersion (per ADR-013 versioning rules); only payload-
    // field changes do. New names should be lowercase, hyphenated.
    public static class BenchNames
    {
        public const string Voxel        = "voxel";
        public const string Autonomous   = "autonomous";
        public const string MegaSweep    = "mega-sweep";
        public const string Aerospike    = "aerospike";
        public const string Turbopump    = "turbopump";
        public const string Monolithic   = "monolithic";
        public const string BenchSa              = "bench-sa";
        public const string BenchSaAirbreathing  = "bench-sa-airbreathing";
        public const string BenchPareto          = "bench-pareto";
        public const string BenchStlValidation   = "bench-stl-validation";
    }

    // Writes the schema-v1 provenance prefix into `sb` with a trailing
    // comma so callers can immediately append their own payload fields.
    // Field order is fixed and load-bearing for ADR-013 compliance.
    public static void AppendProvenance(StringBuilder sb, string benchName)
    {
        var m = MachineInfo.Capture();
        var c = CultureInfo.InvariantCulture;
        sb.Append("\"schema_version\":").Append(SchemaVersion).Append(',');
        sb.Append("\"machine_id\":\"").Append(m.MachineId).Append("\",");
        sb.Append("\"git_sha\":\"").Append(MachineInfo.GitSha()).Append("\",");
        sb.Append("\"bench_name\":\"").Append(benchName).Append("\",");
        sb.Append("\"build_config\":\"").Append(m.BuildConfig).Append("\",");
        sb.Append("\"timestamp\":\"").Append(DateTime.UtcNow.ToString("O", c)).Append("\",");
    }

    // Appends a fully-formed record to `path` (one line, JSON object,
    // UTF-8 + LF). Strips the trailing comma left by callers, closes
    // the object, and appends the line terminator.
    public static void AppendRecord(string path, StringBuilder sb)
    {
        if (sb.Length > 0 && sb[^1] == ',') sb.Length--;
        sb.Append('}');
        sb.Append(Environment.NewLine);
        File.AppendAllText(path, sb.ToString());
    }

    // Convenience: build + append in one call. Caller's `writePayload`
    // emits its own `"field":value,` entries; the trailing comma is
    // handled here.
    public static void AppendRecord(string path, string benchName, Action<StringBuilder> writePayload)
    {
        var sb = new StringBuilder(512);
        sb.Append('{');
        AppendProvenance(sb, benchName);
        writePayload(sb);
        AppendRecord(path, sb);
    }

    // String escaping for free-text JSON values. Keeps the emitter
    // strict-JSON-compliant without pulling System.Text.Json into
    // every call site.
    public static string EscapeJson(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\b': sb.Append("\\b");  break;
                case '\f': sb.Append("\\f");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (ch < 0x20) sb.Append($"\\u{(int)ch:X4}");
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }
}
