param([string]$Name='thunder-window')
Add-Type -AssemblyName System.Drawing,UIAutomationClient,UIAutomationTypes
if(-not ('ThunderShot' -as [type])){Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ThunderShot {
[StructLayout(LayoutKind.Sequential)]public struct R{public int L,T,Rt,B;}
[DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out R r);
[DllImport("user32.dll")]public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
}
'@}
$ids=@(Get-Process thunder -ErrorAction SilentlyContinue | ForEach-Object Id)
$windows=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)
$index=0
foreach($w in $windows){
 if($w.Current.ProcessId -notin $ids -or $w.Current.Name -eq 'suspension-renderer'){continue}
 $h=[IntPtr]$w.Current.NativeWindowHandle;$r=New-Object ThunderShot+R
 [void][ThunderShot]::GetWindowRect($h,[ref]$r)
 if($r.Rt-$r.L -lt 100 -or $r.B-$r.T -lt 100){continue}
 $b=[Drawing.Bitmap]::new($r.Rt-$r.L,$r.B-$r.T);$g=[Drawing.Graphics]::FromImage($b);$dc=$g.GetHdc()
 try{[void][ThunderShot]::PrintWindow($h,$dc,2)}finally{$g.ReleaseHdc($dc)}
 $path=Join-Path (Split-Path $PSScriptRoot -Parent) "docs/evidence/thunder-comparison/$Name-$index.png"
 $b.Save($path);$g.Dispose();$b.Dispose();$index++
 [pscustomobject]@{Name=$w.Current.Name;Handle=$h;Left=$r.L;Top=$r.T;Width=$r.Rt-$r.L;Height=$r.B-$r.T;Path=$path}
}
