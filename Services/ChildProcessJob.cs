using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PcStatsMonitor.Services;

/// <summary>
/// A Windows Job Object that terminates every process assigned to it as soon as this process
/// exits — including on a crash, a force-kill, or a logoff, where no managed shutdown code runs.
///
/// The bundled OpenRGB server is assigned to this job. Previously it was started detached, so a
/// kill or crash left it running for the rest of the machine's uptime, continuously sweeping the
/// SMBus; the client saw that as heavy system-wide load "even when the app is not running"
/// (client round 20, item 8). Managed exit handlers alone cannot cover that case — only the
/// kernel can, which is what this does.
/// </summary>
internal sealed class ChildProcessJob : IDisposable
{
    private IntPtr _handle;

    /// <summary>Creates the job. Throws nothing: on any failure the job is simply inactive and
    /// the caller falls back to best-effort termination on exit.</summary>
    public ChildProcessJob()
    {
        try
        {
            _handle = CreateJobObject(IntPtr.Zero, null);
            if (_handle != IntPtr.Zero) ConfigureKillOnClose();
        }
        catch
        {
            _handle = IntPtr.Zero;
        }
    }

    /// <summary>True when the kernel will clean up assigned children automatically.</summary>
    public bool IsActive => _handle != IntPtr.Zero;

    private void ConfigureKillOnClose()
    {
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr buffer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(limits, buffer, false);
            SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, buffer, (uint)length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Assigns a process to the job so it cannot outlive us. Returns false when the job
    /// is unavailable or the assignment is refused.</summary>
    public bool Assign(Process process)
    {
        if (!IsActive) return false;

        try { return AssignProcessToJobObject(_handle, process.Handle); }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    // ── Interop ─────────────────────────────────────────────────────────────
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
