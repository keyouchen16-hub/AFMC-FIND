using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Afmc.Find.App;

// Kill only the child processes assigned to this private job, including on parent crash.
internal sealed class EngineJob : IDisposable
{
    private IntPtr handle;
    public EngineJob()
    {
        handle=CreateJobObject(IntPtr.Zero,null);
        var limits=new ExtendedLimits();limits.Basic.LimitFlags=0x2000;
        var size=Marshal.SizeOf<ExtendedLimits>();var pointer=Marshal.AllocHGlobal(size);
        try{Marshal.StructureToPtr(limits,pointer,false);if(handle==IntPtr.Zero||!SetInformationJobObject(handle,9,pointer,(uint)size))throw new InvalidOperationException("无法建立引擎进程保护。");}
        catch{Dispose();throw;}
        finally{Marshal.FreeHGlobal(pointer);}
    }
    public void Assign(Process process){if(!AssignProcessToJobObject(handle,process.Handle))throw new InvalidOperationException("无法托管搜索引擎进程。");}
    public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Sequential)]private struct BasicLimits{public long ProcessTime,JobTime;public uint LimitFlags;public UIntPtr MinWorking,MaxWorking;public uint ActiveProcesses;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)]private struct IoCounters{public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]private struct ExtendedLimits{public BasicLimits Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll")]private static extern bool SetInformationJobObject(IntPtr job,int info,IntPtr data,uint length);
    [DllImport("kernel32.dll")]private static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr handle);
}
