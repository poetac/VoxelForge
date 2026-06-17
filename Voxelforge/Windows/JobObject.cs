// JobObject.cs — Minimal wrapper around the Win32 Job-Object API
// used to cap a child process's memory (and
// optionally CPU priority) without touching the child process itself.
//
// Use from the main app to wrap `Process.Start` of the StlExporter
// subprocess:
//
//   using var job = new JobObject(memoryLimitBytes: 8L * 1024 * 1024 * 1024);
//   var proc = Process.Start(psi);
//   job.AssignProcess(proc);
//   proc.WaitForExit();  // if the subprocess exceeds the memory cap,
//                        // Windows kills it with STATUS_QUOTA_EXCEEDED
//                        // and the main app survives.
//
// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE makes the subprocess die when
// the Job Object handle is disposed — so an orphaned subprocess can't
// outlive a main-app crash.
//
// All P/Invoke is kept internal. Non-Windows callers: the class throws
// PlatformNotSupportedException from the ctor. This is Windows-only
// code; the whole codebase targets net9.0-windows.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Voxelforge.Windows;

public sealed class JobObject : IDisposable
{
    private IntPtr _handle;
    private bool   _disposed;

    public JobObject(ulong memoryLimitBytes = 0, bool killOnClose = true)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("JobObject requires Windows.");

        _handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"CreateJobObject failed, GetLastError={Marshal.GetLastWin32Error()}.");

        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        var basicLimits = new NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION();
        uint flags = 0;
        if (memoryLimitBytes > 0)
        {
            flags |= NativeMethods.JOB_OBJECT_LIMIT_PROCESS_MEMORY;
            // CA2020: .NET 7+ relaxed (UIntPtr)UInt64 to silently truncate on
            // 32-bit overflow — wrap in `checked` to keep .NET 6 throw-on-
            // overflow behaviour. Practical only on 32-bit builds with
            // memoryLimitBytes > 4 GiB; harmless on 64-bit.
            info.ProcessMemoryLimit = checked((UIntPtr)memoryLimitBytes);
        }
        if (killOnClose)
            flags |= NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        basicLimits.LimitFlags = flags;
        info.BasicLimitInformation = basicLimits;

        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buf, false);
            if (!NativeMethods.SetInformationJobObject(
                    _handle,
                    NativeMethods.JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                    buf,
                    (uint)size))
            {
                int err = Marshal.GetLastWin32Error();
                NativeMethods.CloseHandle(_handle);
                _handle = IntPtr.Zero;
                throw new InvalidOperationException(
                    $"SetInformationJobObject failed, GetLastError={err}.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>
    /// Bind the given child <paramref name="process"/> to this Job
    /// Object. Once bound, the process inherits the job's memory cap
    /// and (if killOnClose=true) dies when the job handle is disposed.
    /// Returns true on success, false if the OS rejects the bind
    /// (e.g., the process is already in a conflicting job).
    /// </summary>
    public bool AssignProcess(Process process)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(JobObject));
        if (process == null || process.HasExited) return false;
        return NativeMethods.AssignProcessToJobObject(_handle, process.Handle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    ~JobObject() { Dispose(); }

    private static class NativeMethods
    {
        public const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        public enum JOBOBJECTINFOCLASS
        {
            JobObjectBasicLimitInformation = 2,
            JobObjectExtendedLimitInformation = 9,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long     PerProcessUserTimeLimit;
            public long     PerJobUserTimeLimit;
            public uint     LimitFlags;
            public UIntPtr  MinimumWorkingSetSize;
            public UIntPtr  MaximumWorkingSetSize;
            public uint     ActiveProcessLimit;
            public UIntPtr  Affinity;
            public uint     PriorityClass;
            public uint     SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS                        IoInfo;
            public UIntPtr                            ProcessMemoryLimit;
            public UIntPtr                            JobMemoryLimit;
            public UIntPtr                            PeakProcessMemoryUsed;
            public UIntPtr                            PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInformationClass,
            IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
