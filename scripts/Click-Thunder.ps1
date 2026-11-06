param([long]$Handle,[int]$X,[int]$Y)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ThunderInput {
[StructLayout(LayoutKind.Sequential)]public struct R{public int L,T,Rt,B;}
[DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out R r);
[DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
[DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(System.Drawing.Point point);
[DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
[DllImport("user32.dll")]public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
}
'@
$h=[IntPtr]$Handle;$r=New-Object ThunderInput+R
[void][ThunderInput]::GetWindowRect($h,[ref]$r);[void][ThunderInput]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 200
$point=[Drawing.Point]::new($r.L+$X,$r.T+$Y)
[uint32]$owner=0;[void][ThunderInput]::GetWindowThreadProcessId([ThunderInput]::WindowFromPoint($point),[ref]$owner)
$p=Get-Process -Id $owner -ErrorAction Stop
if($p.ProcessName -ne 'thunder' -or $p.Path -ne 'C:\Program Files\Thunder Network\Thunder\Program\Thunder.exe'){throw 'Target occluded: no click sent'}
[void][ThunderInput]::SetCursorPos($point.X,$point.Y)
Start-Sleep -Milliseconds 250
[ThunderInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 50;[ThunderInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
