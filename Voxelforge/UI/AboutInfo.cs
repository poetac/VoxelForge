// AboutInfo.cs — version + build-date metadata surfaced by the About dialog.
//
// Reads from assembly metadata — no manual stamp needed. The file
// Last-Write-Time is used as a pragmatic stand-in for build date;
// a future sprint could swap in `SourceRevisionId` or a proper
// embedded build timestamp via `GenerateAssemblyInfo=true`.
//
// Exposed as a standalone static class (rather than inline in
// `AboutDialog`) so the fallback behaviour is unit-testable.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Voxelforge.UI;

public static class AboutInfo
{
    public const string ProductName  = "Voxelforge";

    // Versioning scheme:
    //   • `Version` follows semver from the v1.0.0 baseline. Tag the
    //     corresponding commit on `main` with `git tag v1.0.0 && git push
    //     origin v1.0.0` after each release-ready merge so the tag matches
    //     the constant.
    //   • A forcing-function test (`AboutInfo_Version_Snapshot` in
    //     `Phase7FollowOnTests`) trips when this constant changes — bump
    //     the assertion in the same commit.
    public const string Version      = "v1.0.0";

    // 2026-04-22 ADR cleanup: HANDOFF.md was retired in favour of the
    // CLAUDE.md + ADR-folder + git-log structure (see ADR/README.md
    // "Removed ADRs"); the About dialog now offers README.md as the
    // user-facing entry point instead.
    public const string ReadmeFile  = "README.md";
    public const string DemoFile    = "DEMO_SCRIPT.md";

    /// <summary>
    /// Snapshot test count as of the current <see cref="Version"/>,
    /// surfaced in the About dialog so demo audiences can see the
    /// suite size without re-running <c>dotnet test</c>. Bumped
    /// deliberately on every sprint that adds tests; a forcing-
    /// function test (`AboutInfo_TestCount_Matches_ProjectStatus`)
    /// trips when the two fall out of sync.
    /// </summary>
    public const int TestCount = 1292;

    /// <summary>
    /// Assembly-reported informational version (e.g. "1.0.0.0"). Falls
    /// back to "unknown" when the assembly has no version metadata.
    /// </summary>
    public static string AssemblyVersion
    {
        get
        {
            var v = typeof(AboutInfo).Assembly.GetName().Version;
            return v?.ToString() ?? "unknown";
        }
    }

    /// <summary>
    /// Approximate build date, taken from the assembly file's last-write
    /// time. Returns <see cref="DateTime.MinValue"/> when the assembly
    /// path cannot be determined (e.g. single-file publish).
    /// </summary>
    public static DateTime BuildDate
    {
        get
        {
            try
            {
                string? path = typeof(AboutInfo).Assembly.Location;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return File.GetLastWriteTime(path);
            }
            catch { /* fall through */ }
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// One-line "AboutInfo-ready" summary usable as a dialog body line.
    /// </summary>
    public static string FormatSummary()
    {
        string date = BuildDate == DateTime.MinValue
            ? "unknown"
            : BuildDate.ToString("yyyy-MM-dd HH:mm");
        return $"{ProductName} {Version}  ·  assembly {AssemblyVersion}  ·  built {date}  ·  {TestCount} tests";
    }

    /// <summary>
    /// Multi-line diagnostic block suitable for pasting into a bug report:
    /// product / version / tests + OS + .NET framework + processor count +
    /// assembly location + the documented keyboard shortcuts. The About
    /// dialog's "Copy diagnostic info" button copies this verbatim to the
    /// clipboard so a user filing a report has a complete environment
    /// snapshot without having to dig through Settings.
    /// </summary>
    public static string FormatDiagnosticInfo()
    {
        var sb = new StringBuilder();
        sb.Append(FormatSummary());
        sb.Append('\n');
        sb.Append("OS: ");
        sb.Append(RuntimeInformation.OSDescription);
        sb.Append('\n');
        sb.Append("Runtime: ");
        sb.Append(RuntimeInformation.FrameworkDescription);
        sb.Append(" / ");
        sb.Append(RuntimeInformation.ProcessArchitecture);
        sb.Append('\n');
        sb.Append("Processors: ");
        sb.Append(Environment.ProcessorCount);
        sb.Append('\n');

        string? path = null;
        try { path = typeof(AboutInfo).Assembly.Location; }
        catch { /* single-file publish → no location */ }
        if (!string.IsNullOrEmpty(path))
        {
            sb.Append("Assembly path: ");
            sb.Append(path);
            sb.Append('\n');
        }

        sb.Append("\nKeyboard shortcuts\n");
        sb.Append(ShortcutRouter.FormatShortcutsList());
        return sb.ToString();
    }
}
