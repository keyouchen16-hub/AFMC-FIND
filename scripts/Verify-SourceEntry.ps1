$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$root=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $root 'artifacts\AFMC-FIND-0.4.1-win-x64\AFMC.Find.exe'
$app=Start-Process -FilePath $exe -PassThru
if(-not $app.WaitForInputIdle(15000)){throw 'Window not ready'}
$deadline=[DateTime]::UtcNow.AddSeconds(15)
do {$app.Refresh();if($app.MainWindowHandle -eq [IntPtr]::Zero){Start-Sleep -Milliseconds 150}}while($app.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
$window=[System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
$navCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'☷　搜索源')
$nav=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$navCondition)
if($null -eq $nav){throw 'Source navigation missing'}
$nav.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 300
$condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)
$matches=@($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object {$_.Current.Name -like '*无极磁链*'})
if($matches.Count -ne 1 -or $matches[0].Current.Name -notlike '*未计入聚合*'){throw 'Source entry/status not verified'}
$matches[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$result=[ordered]@{version=$app.MainModule.FileVersionInfo.ProductVersion;pid=$app.Id;sourceEntry=$matches[0].Current.Name;responding=$app.Responding;dllSha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'AFMC.Find.dll')).Hash;externalAppInvoked=$false}
$result | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root 'docs\evidence\v041\source-entry.json')
$result | ConvertTo-Json
