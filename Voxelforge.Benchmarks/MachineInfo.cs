// Provenance source for the schema-v1 JSONL emitter (ADR-013).
//
// Captures a deterministic fingerprint of the machine + runtime so the
// benchmark JSONL records carry a `machine_id` (16-hex SHA-256 prefix)
// and a `git_sha` alongside every measurement. Without this, future
// bench-diffs cannot tell "Sprint 32 changed Bartz" from "the dev box
// was upgraded" from "ran on a different git SHA than the baseline."

using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Voxelforge.Benchmarks;

internal sealed record MachineInfo(
    string CpuModel,
    int    LogicalCores,
    int    PhysicalCores,
    int    RamGb,
    string OsVersion,
    string DotnetVersion,
    string PicoGkVersion,
    string BuildConfig)
{
    // 16-hex prefix of SHA-256 over the field tuple. Same machine + same
    // build config → same id; cross-machine runs flag automatically.
    public string MachineId
    {
        get
        {
            var tuple = string.Join("|",
                CpuModel, LogicalCores, PhysicalCores, RamGb,
                OsVersion, DotnetVersion, PicoGkVersion, BuildConfig);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(tuple));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }

    private static MachineInfo? _cached;
    public static MachineInfo Capture()
    {
        if (_cached != null) return _cached;
        _cached = new MachineInfo(
            CpuModel:      ReadCpuModel(),
            LogicalCores:  Environment.ProcessorCount,
            PhysicalCores: ReadPhysicalCoreCount(),
            RamGb:         ReadTotalRamGb(),
            OsVersion:     RuntimeInformation.OSDescription,
            DotnetVersion: RuntimeInformation.FrameworkDescription,
            PicoGkVersion: ReadPicoGkVersion(),
            BuildConfig:
#if DEBUG
                "Debug"
#else
                "Release"
#endif
        );
        return _cached;
    }

    // git rev-parse HEAD, cached for the process. Returns "unknown" if
    // not in a git checkout or the call fails — never crashes the bench
    // over a missing .git directory.
    //
    // Audit 01-security L6/L7: this resolves `git` via the OS PATH
    // rather than an absolute exe path. The attack surface is "an
    // attacker who can plant `git.exe` earlier on the user's PATH gets
    // arbitrary code execution under the bench harness." In voxelforge's
    // threat model (a single-developer Windows workstation that also
    // hosts the self-hosted CI runner under NETWORK SERVICE), planting
    // an earlier-PATH `git.exe` already requires write access to a
    // PATH-listed directory — at which point the attacker has full
    // local code execution against the user account anyway. The
    // exposure here is not new ground; resolving via Get-Command at
    // startup wouldn't materially change the threat surface.
    private static string? _gitShaCached;
    public static string GitSha()
    {
        if (_gitShaCached != null) return _gitShaCached;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
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

    private static string ReadCpuModel()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "unknown";
        }
        catch { return "unknown"; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint  dwLength;
        public uint  dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static int ReadTotalRamGb()
    {
        try
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref m)
                ? (int)Math.Round(m.ullTotalPhys / 1024.0 / 1024.0 / 1024.0)
                : 0;
        }
        catch { return 0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION
    {
        public UIntPtr ProcessorMask;
        public int     Relationship;            // 0 = ProcessorCore
        public ulong   Reserved0;
        public ulong   Reserved1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformation(
        IntPtr buffer, ref uint returnLength);

    private static int ReadPhysicalCoreCount()
    {
        try
        {
            uint len = 0;
            GetLogicalProcessorInformation(IntPtr.Zero, ref len);
            if (len == 0) return Environment.ProcessorCount;

            IntPtr buf = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!GetLogicalProcessorInformation(buf, ref len))
                    return Environment.ProcessorCount;

                int size = Marshal.SizeOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION>();
                int count = (int)len / size;
                int physical = 0;
                for (int i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION>(
                        IntPtr.Add(buf, i * size));
                    if (info.Relationship == 0) physical++;
                }
                return physical > 0 ? physical : Environment.ProcessorCount;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return Environment.ProcessorCount; }
    }

    private static string ReadPicoGkVersion()
    {
        try
        {
            var asm = typeof(PicoGK.Library).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (info != null) return info.InformationalVersion;
            var fileVer = asm.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return fileVer?.Version ?? asm.GetName().Version?.ToString() ?? "unknown";
        }
        catch { return "unknown"; }
    }

    // Diagnostic header line for each .stdout.log capture. Schema-v1
    // JSONL records hash the same data into machine_id; this header is
    // human-readable and not parsed.
    public string ToHeaderLine() =>
        $"# machine_id={MachineId} cpu=\"{CpuModel}\" cores={LogicalCores}L/{PhysicalCores}P "
      + $"ram={RamGb}GB os=\"{OsVersion}\" dotnet=\"{DotnetVersion}\" "
      + $"picogk=\"{PicoGkVersion}\" build={BuildConfig}";
}
