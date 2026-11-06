from pathlib import Path
p=Path('scripts/Verify-Release.ps1');s=p.read_text(encoding='utf-8-sig')
s='param([string]$Query="ubuntu", [switch]$ImagesOnly)\n'+s
s=s.replace(".SetValue('ubuntu')",".SetValue($Query)")
needle='[FindCapture]::SetForegroundWindow($process.MainWindowHandle) | Out-Null'
block='''$thumbnailCount = 0
if ($ImagesOnly) {
    $filter = Find-Control 'ImagesOnly'
    $filter.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    $imageDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $imageCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '来源缩略图已加载')
        $thumbnailCount = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $imageCondition).Count
    } until ($thumbnailCount -gt 0 -or [DateTime]::UtcNow -gt $imageDeadline)
    if ($thumbnailCount -eq 0) { throw 'No thumbnail rendered in release window' }
}
'''
s=s.replace(needle,block+needle).replace('version = $process.MainModule.FileVersionInfo.ProductVersion','version = $process.MainModule.FileVersionInfo.ProductVersion\n    query = $Query\n    thumbnailCount = $thumbnailCount')
p.write_text(s,encoding='utf-8-sig')
