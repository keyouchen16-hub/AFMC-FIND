$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GlassCheck {
 [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,int a,out int v,int size);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
$root=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $root 'artifacts\AFMC-FIND-0.8.10-win-x64\AFMC.Find.exe'
$app=Get-Process -Name AFMC.Find | Where-Object {$_.Path -eq $exe} | Select-Object -First 1
if($null -eq $app){throw 'Release is not running'}
[GlassCheck]::ShowWindow($app.MainWindowHandle,9) | Out-Null
[GlassCheck]::SetForegroundWindow($app.MainWindowHandle) | Out-Null
$window=[System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
function Control([string]$id){$c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id);$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)}
$type=0;$hr=[GlassCheck]::DwmGetWindowAttribute($app.MainWindowHandle,38,[ref]$type,4)
if($hr -ne 0 -or $type -ne 3){throw "Desktop acrylic not applied: hr=$hr type=$type"}
$filterCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'筛选当前结果')
$filter=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$filterCondition)
$filter.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
Start-Sleep -Milliseconds 200
$list=Control 'ResultsList'
$scroll=$list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
if(-not $scroll.Current.VerticallyScrollable){throw 'Expected scrollable real search results'}
$scroll.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount,[System.Windows.Automation.ScrollAmount]::LargeIncrement)
Start-Sleep -Milliseconds 150
$scrollPercent=$scroll.Current.VerticalScrollPercent
if($scrollPercent -le 0){throw 'List did not scroll'}
$scroll.SetScrollPercent(-1,0)
if([GlassCheck]::GetForegroundWindow() -ne $app.MainWindowHandle){throw 'Target is not foreground; keyboard test skipped'}
[System.Windows.Forms.SendKeys]::SendWait('^k')
Start-Sleep -Milliseconds 150
$query=Control 'QueryBox'
if(-not $query.Current.HasKeyboardFocus){throw 'Ctrl+K did not focus query'}
$result=[ordered]@{version=$app.MainModule.FileVersionInfo.ProductVersion;backdropType=$type;dwmResult=$hr;scrollPercentAfter=$scrollPercent;ctrlKFocus=$query.Current.HasKeyboardFocus;filterCleared=$filter.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '';physicalKeyboardTest=$false;dllSha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'AFMC.Find.dll')).Hash;systemConfigurationChanged=$false}
$result | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root 'docs\evidence\v0810\glass-verification.json')
$result | ConvertTo-Json


