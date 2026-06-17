// Schema-pinning test for the committed benchmark JSONL baselines.
// ADR-013 defines schema v1 — every record under
// `Voxelforge.Benchmarks/baselines/*.jsonl` must:
//   1. Parse as strict JSON (one record per line, UTF-8).
//   2. Carry `schema_version == 1`.
//   3. Carry the 5 other provenance fields (`machine_id`, `git_sha`,
//      `bench_name`, `build_config`, `timestamp`) populated.
//
// This is the only `.Tests` interaction with the baselines (no PicoGK
// touch — pure string parsing — so xUnit-safe in-process per ADR-005).
//
// Phantom baselines that document a CLI flag never on `main` are
// listed in PhantomBaselines and skipped record-by-record.
//
// History note: `bench-cfd-export.jsonl` was the original phantom
// listed here. It was promoted to a real schema-v1 baseline by
// Sprint BB-3 (PR #206, 2026-04-29) and removed from the set.

using System.Text.Json;

namespace Voxelforge.Tests;

public class BenchmarkJsonSchemaTests
{
    // Empty by design today — no phantoms remaining post-BB-3. Kept
    // as a labelled hook for future regression of the same kind.
    private static readonly HashSet<string> PhantomBaselines = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] RequiredProvenanceFields =
    {
        "schema_version", "machine_id", "git_sha",
        "bench_name", "build_config", "timestamp",
    };

    public static IEnumerable<object[]> AllBaselines()
    {
        string dir = LocateBaselinesDirectory();
        // Per-pillar layout (PR #406): rocket/, airbreathing/, electric/, marine/, legacy/.
        foreach (var path in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories))
            yield return new object[] { Path.GetRelativePath(dir, path).Replace('\\', '/') };
    }

    [Theory]
    [MemberData(nameof(AllBaselines))]
    public void Baseline_ConformsToSchemaV1(string fileName)
    {
        if (PhantomBaselines.Contains(fileName))
            return; // Phantom — see per-pillar README under baselines/.

        string path = Path.Combine(LocateBaselinesDirectory(), fileName);
        Assert.True(File.Exists(path), $"Baseline {path} not found.");

        var lines = File.ReadAllLines(path);
        Assert.True(lines.Length > 0, $"{fileName} is empty — JSONL must have at least one record.");

        for (int lineNo = 0; lineNo < lines.Length; lineNo++)
        {
            var line = lines[lineNo];
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException ex)
            {
                Assert.Fail($"{fileName} line {lineNo + 1}: invalid JSON — {ex.Message}");
                return;
            }
            using (doc)
            {
                var root = doc.RootElement;

                // schema_version must be 1.
                Assert.True(root.TryGetProperty("schema_version", out var sv),
                    $"{fileName} line {lineNo + 1}: missing schema_version");
                Assert.Equal(1, sv.GetInt32());

                // Required provenance fields must be present and non-empty.
                foreach (var field in RequiredProvenanceFields)
                {
                    Assert.True(root.TryGetProperty(field, out var v),
                        $"{fileName} line {lineNo + 1}: missing required provenance field '{field}'");
                    if (v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString();
                        Assert.False(string.IsNullOrWhiteSpace(s),
                            $"{fileName} line {lineNo + 1}: provenance field '{field}' is empty");
                    }
                }

                // machine_id is a 16-char lowercase hex string.
                var mid = root.GetProperty("machine_id").GetString();
                Assert.NotNull(mid);
                Assert.Equal(16, mid!.Length);
                Assert.Matches("^[0-9a-f]{16}$", mid);

                // git_sha is a 40-char lowercase hex string OR the literal "unknown".
                var sha = root.GetProperty("git_sha").GetString();
                Assert.NotNull(sha);
                Assert.True(sha == "unknown" || (sha!.Length == 40 && System.Text.RegularExpressions.Regex.IsMatch(sha, "^[0-9a-f]{40}$")),
                    $"{fileName} line {lineNo + 1}: git_sha '{sha}' is neither a 40-hex SHA nor 'unknown'");

                // build_config is one of the two known values.
                var cfg = root.GetProperty("build_config").GetString();
                Assert.True(cfg is "Debug" or "Release",
                    $"{fileName} line {lineNo + 1}: build_config '{cfg}' is not 'Debug' or 'Release'");

                // timestamp parses as an ISO-8601 UTC instant.
                var ts = root.GetProperty("timestamp").GetString();
                Assert.True(DateTimeOffset.TryParse(ts, out _),
                    $"{fileName} line {lineNo + 1}: timestamp '{ts}' is not parseable as ISO-8601");
            }
        }
    }

    [Fact]
    public void PhantomBaselines_AreDocumentedInReadme()
    {
        // Per-pillar layout (PR #406): READMEs now live under each pillar
        // subdir (e.g. baselines/rocket/README.md, baselines/airbreathing/README.md).
        // Concatenate all README.md found anywhere in the baselines tree so a
        // phantom listed for any pillar is discoverable.
        string dir = LocateBaselinesDirectory();
        var readmes = Directory.EnumerateFiles(dir, "README.md", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.NotEmpty(readmes);
        string readme = string.Join("\n\n", readmes);
        foreach (var phantom in PhantomBaselines)
        {
            Assert.Contains(phantom, readme);
            Assert.Contains("phantom", readme, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Walk up from the test assembly's location until we find
    // `voxelforge.sln`, then resolve the baselines directory relative
    // to that. Robust to running from `bin/Release/...` (xUnit default)
    // or repo root (manual `dotnet test`).
    private static string LocateBaselinesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "voxelforge.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir); // walked off the filesystem root without finding the solution
        // Folder name kept as Voxelforge.Benchmarks (PR-2 plan defers
        // folder renames; only csproj/namespace identifiers changed).
        return Path.Combine(dir!.FullName, "Voxelforge.Benchmarks", "baselines");
    }
}
