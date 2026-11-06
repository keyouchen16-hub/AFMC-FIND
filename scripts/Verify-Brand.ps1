$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class BrandNative {
 [DllImport("user32.dll")]public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
}
'@
[void][BrandNative]::SetProcessDPIAware()
$evidence=Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\evidence\v0810'
$target=Join-Path $env:LOCALAPPDATA 'Programs\AFMC FIND\AFMC.Find.exe'
$process=@(Get-Process AFMC.Find | Where-Object Path -eq $target)
if($process.Count -ne 1){throw 'Expected exactly one installed application.'}
$automation=[System.Windows.Automation.AutomationElement]
$scope=[System.Windows.Automation.TreeScope]::Descendants
function Find-Control($parent,$id) {
 $condition=New-Object System.Windows.Automation.PropertyCondition($automation::AutomationIdProperty,$id)
 $result=$parent.FindFirst($scope,$condition)
 if(-not $result){throw "Missing control $id"};return $result
}
function Invoke-Control($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Get-Dialog($title) {
 Start-Sleep -Milliseconds 350
 $condition=New-Object System.Windows.Automation.PropertyCondition($automation::NameProperty,$title)
 $result=$main.FindFirst($scope,$condition)
 if(-not $result){$result=$automation::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition) | Where-Object {$_.Current.ProcessId -eq $process[0].Id} | Select-Object -First 1}
 if(-not $result){throw "Missing dialog $title"};return $result
}
function Snapshot($element,$name) {
 [void][BrandNative]::SetForegroundWindow([IntPtr]$element.Current.NativeWindowHandle)
 Start-Sleep -Milliseconds 300
 $rect=$element.Current.BoundingRectangle
 $bitmap=New-Object System.Drawing.Bitmap([int]$rect.Width,[int]$rect.Height)
 $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
 try {$dc=$graphics.GetHdc();try{if(-not [BrandNative]::PrintWindow([IntPtr]$element.Current.NativeWindowHandle,$dc,2)){throw 'Window capture failed.'}}finally{$graphics.ReleaseHdc($dc)};$bitmap.Save((Join-Path $evidence $name))} finally {$graphics.Dispose();$bitmap.Dispose()}
}
$main=$automation::FromHandle($process[0].MainWindowHandle)
if($main.Current.Name -ne 'AFMC FIND v0.8.10'){throw 'Version title mismatch.'}
$feedback=Find-Control $main 'FeedbackButton'
if($feedback.Current.IsOffscreen){throw 'Feedback entry is not visible.'}
Snapshot $main 'main-installed.png'
Invoke-Control $feedback
$dialog=Get-Dialog 'AFMC FIND · 反馈'
$email=Find-Control $dialog 'EmailText'
if($email.Current.Name -ne 'keyouchen16@gmail.com'){throw 'Email mismatch.'}
$emailAddress=$email.Current.Name
Snapshot $dialog 'feedback-installed.png'
# Close through the accessible cancel button; do not launch mail or change clipboard.
$closeCondition=New-Object System.Windows.Automation.PropertyCondition($automation::NameProperty,'关闭反馈')
Invoke-Control ($dialog.FindFirst($scope,$closeCondition))
Start-Sleep -Milliseconds 400
$main=$automation::FromHandle($process[0].MainWindowHandle)
Invoke-Control (Find-Control $main 'PreferencesButton')
$settings=Get-Dialog 'AFMC FIND · 设置'
Snapshot $settings 'settings-installed.png'
$closeCondition=New-Object System.Windows.Automation.PropertyCondition($automation::NameProperty,'关闭设置')
Invoke-Control ($settings.FindFirst($scope,$closeCondition))
[ordered]@{Version=$main.Current.Name;Email=$emailAddress;FeedbackVisible=$true;Process=$process[0].Id;MailClientInvoked=$false} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $evidence 'brand-verification.json')
Write-Output 'PASS installed version, feedback entry, email and settings dialogs; screenshots saved.'


