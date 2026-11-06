$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$ui=[System.Windows.Automation.AutomationElement]
$target=Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND/AFMC.Find.exe'
$process=Get-Process AFMC.Find | Where-Object Path -eq $target | Select-Object -First 1
$main=$ui::FromHandle($process.MainWindowHandle)
$list=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new($ui::AutomationIdProperty,'ResultsList'))
$scroll=$list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
try {
    $scroll.SetScrollPercent(-1,100)
    Start-Sleep -Milliseconds 600
    $text=$list.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new($ui::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text))
    $failed=@($text | Where-Object {!$_.Current.IsOffscreen -and $_.Current.Name -match '本次连接超时|本次试播失败'} | ForEach-Object {$_.Current.Name})
    $failed | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $PSScriptRoot '../docs/evidence/v0810/installed-failure-tail.json')
    if(!$failed.Count){throw 'No known failed rows visible at end of installed results'}
    Write-Output "PASS installed list end contains $($failed.Count) visible failed probe rows"
} finally { $scroll.SetScrollPercent(-1,0) }
