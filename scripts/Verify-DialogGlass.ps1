$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class DialogMaterialCheck {
 [DllImport("dwmapi.dll")]public static extern int DwmGetWindowAttribute(IntPtr h,int a,out int v,int size);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("kernel32.dll")]public static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]public static extern bool AttachThreadInput(uint a,uint b,bool link);
}
'@
$target=Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND/AFMC.Find.exe'
$process=Get-Process AFMC.Find | Where-Object Path -EQ $target | Select-Object -First 1
if(!$process){throw 'Installed application is not running'}
$readyUntil=(Get-Date).AddSeconds(15)
while($process.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $readyUntil){Start-Sleep -Milliseconds 100;$process.Refresh()}
if($process.MainWindowHandle -eq [IntPtr]::Zero){throw 'Installed main window did not become ready'}
$main=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if($main.Current.Name -ne 'AFMC FIND v0.8.10'){throw 'Wrong installed version'}
function Id($root,$id){$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id))}
function Named($root,$name){$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name))}
function Click($control){$control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
function Capture($window,$name){
    $handle=[IntPtr]$window.Current.NativeWindowHandle
    $material=0
    if([DialogMaterialCheck]::DwmGetWindowAttribute($handle,38,[ref]$material,4) -ne 0 -or $material -ne 3){throw "$name lacks acrylic"}
    [void][DialogMaterialCheck]::SetForegroundWindow($handle)
    if([DialogMaterialCheck]::GetForegroundWindow() -ne $handle){
        [uint32]$ownerProcess=0
        $foreground=[DialogMaterialCheck]::GetWindowThreadProcessId([DialogMaterialCheck]::GetForegroundWindow(),[ref]$ownerProcess)
        $current=[DialogMaterialCheck]::GetCurrentThreadId()
        $attached=$foreground -ne $current -and [DialogMaterialCheck]::AttachThreadInput($current,$foreground,$true)
        try{[void][DialogMaterialCheck]::SetForegroundWindow($handle)}finally{if($attached){[void][DialogMaterialCheck]::AttachThreadInput($current,$foreground,$false)}}
    }
    Start-Sleep -Milliseconds 500
    if([DialogMaterialCheck]::GetForegroundWindow() -ne $handle){throw 'Screenshot foreground not verified'}
    $rect=$window.Current.BoundingRectangle
    $bitmap=[Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try{$graphics.CopyFromScreen([int]$rect.X,[int]$rect.Y,0,0,$bitmap.Size);$bitmap.Save((Join-Path $PSScriptRoot "../docs/evidence/v0810/installed-$name.png"),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}
    [pscustomobject]@{Window=$name;Backdrop=$material;Foreground=$true}
}
$results=@()
Click (Id $main 'PreferencesButton')
Start-Sleep -Milliseconds 300
$settings=Named $main 'AFMC FIND · 设置'
try{$results+=Capture $settings 'settings'}finally{$settings.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}
Start-Sleep -Milliseconds 300
Click (Id $main 'SourcesNav')
Click (Named $main '添加 Torznab 源')
Start-Sleep -Milliseconds 300
$discovery=Named $main '配置 Torznab 搜索源'
try{
    $results+=Capture $discovery 'discovery'
    Click (Id $discovery 'CatalogButton')
    Start-Sleep -Milliseconds 500
    $catalog=Named $discovery '配置 Torznab 搜索源'
    try{$results+=Capture $catalog 'catalog'}finally{$catalog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}
}finally{Start-Sleep -Milliseconds 300;$discovery.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}
$results | ConvertTo-Json | Tee-Object -FilePath (Join-Path $PSScriptRoot '../docs/evidence/v0810/installed-dialog-glass.json')
