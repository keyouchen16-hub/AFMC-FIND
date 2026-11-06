$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$target=Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND'
$process=Get-Process AFMC.Find | Where-Object Path -EQ (Join-Path $target 'AFMC.Find.exe') | Select-Object -First 1
if(!$process){throw 'Installed application is not running'}
$window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if($window.Current.Name -ne 'AFMC FIND v0.8.10'){throw 'Incorrect installed version'}
function Find-Id($root,$id){$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id))}
function Find-Name($root,$name){$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name))}
function Invoke-Control($control){if(!$control){throw 'Expected control unavailable'};$control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
Invoke-Control (Find-Id $window 'SourcesNav')
Start-Sleep -Milliseconds 250
Invoke-Control (Find-Name $window '添加 Torznab 源')
Start-Sleep -Milliseconds 400
$dialog=Find-Name $window '配置 Torznab 搜索源'
if(!$dialog){throw 'Source dialog unavailable'}
$discoveryDialog=$dialog
if(!(Find-Id $dialog 'WebsiteBox')){throw 'Website discovery is not the default entry'}
(Find-Id $dialog 'WebsiteBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('https://4kbt.org/category/movie')
if((Find-Id $dialog 'ProbeQueryBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne ''){throw 'Automatic probing still requires a default keyword'}
Invoke-Control (Find-Id $dialog 'DetectButton')
$until=(Get-Date).AddSeconds(90)
do {Start-Sleep -Milliseconds 250;if((Find-Id $dialog 'DetectButton').Current.IsEnabled){break}}while((Get-Date) -lt $until)
if(!(Find-Id $dialog 'SaveDiscoveryButton').Current.IsEnabled){throw 'Installed website discovery did not validate a usable source'}
$discoveryStatus=(Find-Id $dialog 'OutcomeText').Current.Name
Invoke-Control (Find-Id $dialog 'CatalogButton')
Start-Sleep -Milliseconds 400
$dialog=Find-Name $discoveryDialog '配置 Torznab 搜索源'
if(!$dialog){throw 'Nested adapter directory unavailable'}
try {
    $until=(Get-Date).AddSeconds(45)
    do {
        $status=(Find-Id $dialog 'StatusText').Current.Name
        if($status -match '目录已就绪'){break}
        Start-Sleep -Milliseconds 250
    } while((Get-Date) -lt $until)
    if($status -notmatch '目录已就绪 · (\d+) 个'){throw 'Installed engine catalog did not become ready'}
    $count=[int]$Matches[1]
    if(!(Find-Id $dialog 'AccessFilter') -or !(Find-Id $dialog 'ResourceFilter')){throw 'Catalog filters unavailable'}
    if((Find-Id $dialog 'KeyBox') -or (Find-Id $dialog 'EndpointBox')){throw 'Manual engine credential controls still present'}
    (Find-Id $dialog 'FindSourceBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('https://archive.org/')
    Start-Sleep -Milliseconds 250
    $list=Find-Id $dialog 'CatalogList'
    $item=$list.FindFirst([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
    if(!$item){throw 'Website adapter filter has no result'}
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $until=(Get-Date).AddSeconds(30)
    do {if((Find-Id $dialog 'AddButton').Current.IsEnabled){break};Start-Sleep -Milliseconds 250}while((Get-Date) -lt $until)
    if(!(Find-Id $dialog 'AddButton').Current.IsEnabled){throw 'Installed native form unavailable'}
    $engine=Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" | Where-Object ExecutablePath -EQ (Join-Path $target 'components/Jackett/JackettConsole.exe') | Select-Object -First 1
    if(!$engine){throw 'Bundled child process not found'}
    $listeners=@(Get-NetTCPConnection -OwningProcess $engine.ProcessId -State Listen)
    if($listeners.Count -eq 0 -or @($listeners | Where-Object LocalAddress -NE '127.0.0.1').Count -gt 0){throw 'Engine is not loopback-only'}
    $result=[ordered]@{Version='0.8.10';WebsiteDiscovery=$discoveryStatus;Catalog=$count;CategoryAndAccessFilters=$true;ManualEngineCredentials=$false;NativeConfiguration=$true;BundledChild=$true;LoopbackOnly=$true;Process=$process.Id;EngineProcess=$engine.ProcessId}
} finally {
    $dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 300
    $discoveryDialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class EngineExitCheck {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern uint RegisterWindowMessage(string name);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
}
'@
$message=[EngineExitCheck]::RegisterWindowMessage('AFMC.Find.ExitForUpdate.v1')
[void][EngineExitCheck]::PostMessage($process.MainWindowHandle,$message,[IntPtr]::Zero,[IntPtr]::Zero)
if(!$process.WaitForExit(12000)){throw 'Application did not exit cleanly'}
if(Get-Process -Id $engine.ProcessId -ErrorAction SilentlyContinue){throw 'Engine remained after application exit'}
$result['ChildStoppedOnAppExit']=$true
$restart=Start-Process -FilePath (Join-Path $target 'AFMC.Find.exe') -WorkingDirectory $target -PassThru
if(!$restart.WaitForInputIdle(15000)){throw 'Restart failed'}
$result['RestartProcess']=$restart.Id
$root=Split-Path $PSScriptRoot -Parent
$result | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root 'docs/evidence/v0810/installed-engine.json')
$result | ConvertTo-Json
