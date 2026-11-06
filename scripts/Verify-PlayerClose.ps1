$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$ui = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]::Descendants
function Find-Control($root,$property,$value) {
    $root.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new($property,$value))
}
function Invoke-Control($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
$target=Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND/AFMC.Find.exe'
$proc=Get-Process AFMC.Find | Where-Object Path -eq $target | Select-Object -First 1
$main=$ui::FromHandle($proc.MainWindowHandle)
if ($main.Current.Name -ne 'AFMC FIND v0.8.17') {throw 'Wrong installed version'}
foreach($i in 1..3) {
    Invoke-Control (Find-Control $main $ui::AutomationIdProperty 'PlayerEntryButton')
    $deadline=(Get-Date).AddSeconds(5)
    do {$window=Find-Control $main $ui::NameProperty 'AFMC FIND · 播放器';if(!$window){Start-Sleep -Milliseconds 100}} while (!$window -and (Get-Date) -lt $deadline)
    if(!$window){throw 'Player did not open'}
    Invoke-Control (Find-Control $window $ui::NameProperty '关闭播放器')
    $deadline=(Get-Date).AddSeconds(5)
    do {Start-Sleep -Milliseconds 100;$remaining=Find-Control $main $ui::NameProperty 'AFMC FIND · 播放器'} while($remaining -and (Get-Date) -lt $deadline)
    if($remaining -or $proc.HasExited -or !(Find-Control $main $ui::AutomationIdProperty 'PlayerEntryButton').Current.IsEnabled){throw 'Main window unavailable after player close'}
    Write-Output "PASS installed caption close round $i, same main process $($proc.Id)"
}
