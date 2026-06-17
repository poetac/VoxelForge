namespace Voxelforge.Cfd.Su2;

/// <summary>
/// Locates the SU2_CFD executable via SU2_RUN env var → PATH fallback.
/// Returns null when not found — callers must handle gracefully (CI-safe per ADR-026 §3).
/// </summary>
public static class Su2Locator
{
    public static string? FindSu2Cfd()
    {
        string exeName = OperatingSystem.IsWindows() ? "SU2_CFD.exe" : "SU2_CFD";

        string? su2Run = Environment.GetEnvironmentVariable("SU2_RUN");
        if (!string.IsNullOrWhiteSpace(su2Run))
        {
            string candidate = Path.Combine(su2Run, exeName);
            if (File.Exists(candidate))
                return candidate;
        }

        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar is not null)
        {
            foreach (string dir in pathVar.Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(dir, exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    public static bool IsAvailable() => FindSu2Cfd() is not null;
}
