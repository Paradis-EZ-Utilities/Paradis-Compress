using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ParadisCompress.Services;

/// <summary>
/// A Windows job object with "kill on close": every ffmpeg/ffprobe this app starts is added to it, so
/// they end when Paradis Compress ends, even if it crashes or is ended from Task Manager. Only our own
/// child processes are ever in the job; nothing else on the PC is touched.
/// </summary>
public static class ChildProcessGuard
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

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
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    private static readonly Lazy<IntPtr> Job = new(CreateJob);

    private static IntPtr CreateJob()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            Log.Warn($"CreateJobObject failed ({Marshal.GetLastWin32Error()}); child processes won't be tied to the app");
            return IntPtr.Zero;
        }
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(info)))
            Log.Warn($"SetInformationJobObject failed ({Marshal.GetLastWin32Error()})");
        // The handle is deliberately never closed: it closes when this process exits, which is the point.
        return job;
    }

    public static void Adopt(Process process)
    {
        try
        {
            if (Job.Value != IntPtr.Zero && !AssignProcessToJobObject(Job.Value, process.Handle))
                Log.Warn($"AssignProcessToJobObject failed ({Marshal.GetLastWin32Error()})");
        }
        catch (Exception ex)
        {
            Log.Warn("Could not tie child process to the app", ex);
        }
    }
}
