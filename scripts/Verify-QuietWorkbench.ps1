[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$ExpectedVersion = '0.8.17',
    [switch]$Search,
    [ValidateRange(10, 240)][int]$SearchTimeoutSeconds = 150
)

# Run against the already-open installed app. Never launches, closes, or replaces it.
# -Search issues a real "ubuntu" query; the app may record that query in its normal history.
# No settings are toggled or saved. UIA/native input is not physical-pointer acceptance.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$evidence = Join-Path $root 'docs/evidence/v0817'
$target = Join-Path $env:LOCALAPPDATA 'Programs/AFMC FIND/AFMC.Find.exe'
$profile = Join-Path $env:LOCALAPPDATA 'AFMC/Find'
$checks = [Collections.Generic.List[object]]::new()
$captures = [Collections.Generic.List[object]]::new()
$settingsWindow = $null
$main = $null
$process = $null
$searchStarted = $false
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$report = [ordered]@{
    ExpectedVersion = $ExpectedVersion
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    Mode = $(if ($Search) { 'UIAndRealSearch' } else { 'UIOnly' })
    InstalledPath = $target
    Input = 'UI Automation Invoke/Selection/Scroll plus synthetic Ctrl+K and Escape'
    Scope = 'Visible-state and installed interaction checks; not physical-mouse, all-DPI, multi-monitor, playback, or source-availability certification.'
    Search = [ordered]@{ Requested = [bool]$Search; Query = $(if ($Search) { 'ubuntu' } else { $null }); SyntheticResults = $false; Note = 'Real search may update ordinary search history. Configuration files are not written by this script.' }
    Checks = $checks
    Captures = $captures
}

function Add-Check([string]$Name, [ValidateSet('Passed','Failed','Unverified','Skipped')][string]$Status, [string]$Detail, $Data = $null) {
    $checks.Add([pscustomobject]@{ Name=$Name; Status=$Status; Detail=$Detail; Data=$Data })
    Write-Host "$Status : $Name - $Detail"
}
function Find-Id($Parent, [string]$Id) {
    if ($null -eq $Parent) { return $null }
    $Parent.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
}
function Find-Name($Parent, [string]$Name, $Type = $null) {
    if ($null -eq $Parent) { return $null }
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name)
    if ($null -ne $Type) {
        $condition = [Windows.Automation.AndCondition]::new($condition,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $Type))
    }
    $Parent.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Find-AppWindow([string]$Name) {
    $process.Refresh()
    if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
        $native=[Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        if ($native.Current.Name -eq $Name) { return $native }
        $owned=Find-Name $native $Name ([Windows.Automation.ControlType]::Window)
        if ($null -ne $owned) { return $owned }
    }
    [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$process.Id),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Window)))
}
function Is-Visible($Element) {
    if ($null -eq $Element) { return $false }
    try { $current=$Element.Current; return !$current.IsOffscreen -and $current.BoundingRectangle.Width -gt 0 -and $current.BoundingRectangle.Height -gt 0 }
    catch [Windows.Automation.ElementNotAvailableException] { return $false }
}
function Invoke-Control($Element) {
    if (!(Is-Visible $Element)) { throw 'Required control is missing or offscreen.' }
    if (!$Element.Current.IsEnabled) { throw 'Required control is disabled.' }
    $Element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Wait-Until([scriptblock]$Predicate, [int]$Seconds = 8) {
    $until=[DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        try { if (& $Predicate) { return $true } } catch [Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $until)
    return $false
}
function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Stop-Unverified([string]$Message) { throw "UNVERIFIED: $Message" }
function Get-Selection($List) {
    $selection=$List.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern)
    @($selection.Current.GetSelection())
}
function Get-Rect($Element) {
    $bounds=$Element.Current.BoundingRectangle
    [pscustomobject]@{ X=$bounds.X; Y=$bounds.Y; Width=$bounds.Width; Height=$bounds.Height; Bottom=$bounds.Bottom }
}
function Get-ConfigHashes {
    $result=[ordered]@{}
    foreach ($name in 'preferences.json','sources.json') {
        $path=Join-Path $profile $name
        $result[$name]=$(if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { 'Absent' })
    }
    return $result
}
function Activate-Window($Window) {
    $handle=[IntPtr]$Window.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero -or [QuietWorkbenchAcceptance]::IsIconic($handle)) { return $false }
    if ([QuietWorkbenchAcceptance]::GetForegroundWindow() -ne $handle) {
        [void][QuietWorkbenchAcceptance]::SetForegroundWindow($handle)
        if ([QuietWorkbenchAcceptance]::GetForegroundWindow() -ne $handle) {
            [uint32]$foregroundProcess=0
            $foregroundThread=[QuietWorkbenchAcceptance]::GetWindowThreadProcessId([QuietWorkbenchAcceptance]::GetForegroundWindow(),[ref]$foregroundProcess)
            $currentThread=[QuietWorkbenchAcceptance]::GetCurrentThreadId()
            $attached=$foregroundThread -ne $currentThread -and [QuietWorkbenchAcceptance]::AttachThreadInput($currentThread,$foregroundThread,$true)
            try { [void][QuietWorkbenchAcceptance]::SetForegroundWindow($handle) }
            finally { if($attached){[void][QuietWorkbenchAcceptance]::AttachThreadInput($currentThread,$foregroundThread,$false)} }
        }
        Start-Sleep -Milliseconds 250
    }
    return [QuietWorkbenchAcceptance]::GetForegroundWindow() -eq $handle
}
function Capture-Window($Window, [string]$Name) {
    $record=[ordered]@{ Name=$Name; Status='Unverified'; Path=$null; Reason=$null }
    try {
        if (!(Activate-Window $Window)) { throw 'Windows did not grant exact foreground ownership; no screenshot was taken.' }
        $handle=[IntPtr]$Window.Current.NativeWindowHandle
        if ($Window.Current.ProcessId -ne $process.Id) { throw 'Window process does not match the installed executable.' }
        $bounds=$Window.Current.BoundingRectangle
        $screen=[Windows.Forms.SystemInformation]::VirtualScreen
        if ($bounds.Width -lt 100 -or $bounds.Height -lt 100 -or $bounds.Left -lt $screen.Left -or $bounds.Top -lt $screen.Top -or $bounds.Right -gt $screen.Right -or $bounds.Bottom -gt $screen.Bottom) {
            throw 'The full app window is not on the visible desktop; no partial or desktop capture was taken.'
        }
        # Check a grid of interior points, rejecting another window obscuring this one.
        foreach ($xFraction in 0.1,0.5,0.9) { foreach ($yFraction in 0.1,0.5,0.9) {
            $point=[Drawing.Point]::new([int]($bounds.X+$bounds.Width*$xFraction),[int]($bounds.Y+$bounds.Height*$yFraction))
            $hit=[QuietWorkbenchAcceptance]::WindowFromPoint($point)
            if ([QuietWorkbenchAcceptance]::GetAncestor($hit,2) -ne $handle) { throw 'Another window overlaps the capture area; no screenshot was taken.' }
        } }
        if ([QuietWorkbenchAcceptance]::GetForegroundWindow() -ne $handle) { throw 'Foreground changed during verification; no screenshot was taken.' }
        $path=Join-Path $evidence "$Name-$runId.png"
        $bitmap=[Drawing.Bitmap]::new([int]$bounds.Width,[int]$bounds.Height)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen([int]$bounds.X,[int]$bounds.Y,0,0,$bitmap.Size)
            if ([QuietWorkbenchAcceptance]::GetForegroundWindow() -ne $handle) { throw 'Foreground changed during capture; the image was discarded.' }
            $bitmap.Save($path,[Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
        $record.Status='Captured'; $record.Path=$path; $record.Reason='Exact foreground handle, installed process, full window rectangle and occlusion samples checked.'
    } catch { $record.Reason=$_.Exception.Message }
    $captures.Add([pscustomobject]$record)
    Add-Check "Screenshot.$Name" $(if ($record.Status -eq 'Captured') { 'Passed' } else { 'Unverified' }) $record.Reason $record.Path
}
function Send-AppKey([string]$Key) {
    $front=[QuietWorkbenchAcceptance]::GetForegroundWindow()
    [uint32]$frontProcess=0
    [void][QuietWorkbenchAcceptance]::GetWindowThreadProcessId($front,[ref]$frontProcess)
    if ($frontProcess -ne $process.Id) { throw 'Keyboard action cancelled: installed application is not foreground.' }
    foreach ($modifier in 0x10,0x11,0x12,0x5B,0x5C) {
        if (([QuietWorkbenchAcceptance]::GetAsyncKeyState($modifier) -band 0x8000) -ne 0) { throw 'Keyboard action cancelled: a physical modifier is already held.' }
    }
    if ($Key -eq 'CtrlK') { [QuietWorkbenchAcceptance]::CtrlK() } else { [QuietWorkbenchAcceptance]::Escape() }
}
function Get-AppMenuItems {
    $items=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$process.Id),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::MenuItem)))
    @($items | Where-Object { Is-Visible $_ })
}

New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$beforeHashes=Get-ConfigHashes
try {
    Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
    if (!('QuietWorkbenchAcceptance' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class QuietWorkbenchAcceptance {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(System.Drawing.Point p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flag);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a,uint b,bool link);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    public static void CtrlK() {
        try { keybd_event(0x11,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(60); keybd_event(0x4B,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(80); }
        finally { keybd_event(0x4B,0,2,UIntPtr.Zero); keybd_event(0x11,0,2,UIntPtr.Zero); }
    }
    public static void Escape() { try { keybd_event(0x1B,0,0,UIntPtr.Zero); } finally { keybd_event(0x1B,0,2,UIntPtr.Zero); } }
}
'@
    }
    Require (Test-Path -LiteralPath $target) 'Installed executable is missing.'
    $matches=@(Get-Process -Name 'AFMC.Find' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target })
    Require ($matches.Count -eq 1) "Expected exactly one running process at the installed path; observed $($matches.Count)."
    $process=$matches[0]
    $report.ProcessId=$process.Id
    $report.ExecutableSha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    $report.FileVersion=(Get-Item -LiteralPath $target).VersionInfo.FileVersion
    $report.ProductVersion=(Get-Item -LiteralPath $target).VersionInfo.ProductVersion
    $main=Find-AppWindow "AFMC FIND v$ExpectedVersion"
    Require ($null -ne $main) 'Main window title/version does not match the expected installed version.'
    Require ($report.FileVersion -eq "$ExpectedVersion.0" -or $report.FileVersion -eq $ExpectedVersion) 'Executable file version does not match expected version.'
    $report.MainWindowTitle=$main.Current.Name
    Add-Check 'InstalledVersionAndWindow' 'Passed' 'One process at the fixed install path; file version and native main-window title match.'
    if (Activate-Window $main) { Add-Check 'MainForeground' 'Passed' 'The exact installed main-window handle owns foreground.' (Get-Rect $main) }
    else { Add-Check 'MainForeground' 'Unverified' 'Windows denied foreground ownership; continue only target-bound UI Automation, no global keyboard input.' }
    $query=Find-Id $main 'QueryBox'
    if (!(Is-Visible $query) -and (Is-Visible (Find-Id $main 'SourceList'))) {
        Invoke-Control (Find-Id $main 'SearchNav')
        Require (Wait-Until { Is-Visible (Find-Id $main 'QueryBox') }) 'Could not return from the existing source page.'
        $query=Find-Id $main 'QueryBox'
        Add-Check 'EnteredSearchPage' 'Passed' 'The existing app was on Sources; used its Search navigation without resetting data.'
    }
    $results=Find-Id $main 'ResultsList'
    Require (Is-Visible $query) 'Search field is not visible on entry; initial workbench state cannot be verified.'
    Require ($null -ne $results) 'Result list is missing.'
    $originalQuery=$query.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    $cancelSearch=Find-Id $main 'CancelButton'
    Require (!(Is-Visible $cancelSearch) -or !$cancelSearch.Current.IsEnabled) 'A search is already in progress; the script will not replace it.'
    $hasInitialSelection=@(Get-Selection $results).Count -gt 0
    $initialDetailVisible=Is-Visible (Find-Id $main 'DetailCloseButton')
    Add-Check 'InitialDetailHidden' $(if (!$hasInitialSelection -and !$initialDetailVisible) { 'Passed' } else { 'Failed' }) 'Checked existing state before changing selection; an already selected resource is not treated as a clean startup.'
    $duplicate=Find-Name $main '聚合搜索' ([Windows.Automation.ControlType]::Text)
    Add-Check 'NoRepeatedSearchHeading' $(if (!(Is-Visible $duplicate) -and !(Is-Visible (Find-Id $main 'PageTitle'))) { 'Passed' } else { 'Failed' }) 'No visible repeated aggregate-search heading/PageTitle.'
    Capture-Window $main 'installed-workbench-initial'

    Invoke-Control (Find-Id $main 'SourcesNav')
    Require (Wait-Until { Is-Visible (Find-Id $main 'SourceList') }) 'Source-management navigation did not show its list.'
    Add-Check 'SourcesNavigation' 'Passed' 'Source list appears from the header entry.'
    foreach($id in 'AddSourceButton','SourceWebsiteBox','AddedSourcesTab','CatalogSourcesTab') {Require (Is-Visible (Find-Id $main $id)) "Integrated source control absent: $id"}
    Capture-Window $main 'installed-sources'
    Invoke-Control (Find-Id $main 'CatalogSourcesTab')
    Require (Wait-Until { (Find-Id $main 'ConnectionStatus').Current.Name -notlike '*正在读取*' } 70) 'Catalog load timed out.'
    $catalog=Find-Id $main 'SourceCatalogList'
    $entries=$catalog.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
    Require ($entries.Count -gt 0) 'Live adapted catalog returned no entries.'
    Add-Check 'IntegratedLiveCatalog' 'Passed' 'Read actual bundled engine catalog; no sources were added or edited.' @{RealizedRows=$entries.Count;Status=(Find-Id $main 'ConnectionStatus').Current.Name}
    Capture-Window $main 'installed-catalog'
    Invoke-Control (Find-Id $main 'AddedSourcesTab')
    Invoke-Control (Find-Id $main 'SearchNav')
    Require (Wait-Until { Is-Visible (Find-Id $main 'QueryBox') }) 'Return to search did not restore the query field.'
    Add-Check 'SearchNavigationReturn' 'Passed' 'Search view returns through the header entry.'
    try {
        Require (Activate-Window $main) 'Foreground activation denied before shortcut test.'
        (Find-Id $main 'SourcesNav').SetFocus()
        Require ([Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId -ne 'QueryBox') 'Could not establish a non-search focus baseline.'
        Send-AppKey 'CtrlK'
        Require (Wait-Until { $focused=[Windows.Automation.AutomationElement]::FocusedElement; $focused.Current.ProcessId -eq $process.Id -and $focused.Current.AutomationId -eq 'QueryBox' }) 'Ctrl+K did not move UIA keyboard focus to QueryBox.'
        Add-Check 'CtrlKSearchFocus' 'Passed' 'Synthetic Ctrl+K from a non-query focus baseline; UIA confirms QueryBox keyboard focus. No physical-key claim.'
    } catch { Add-Check 'CtrlKSearchFocus' 'Unverified' $_.Exception.Message }

    Invoke-Control (Find-Id $main 'PreferencesButton')
    Require (Wait-Until { $script:settingsWindow=Find-AppWindow 'AFMC FIND · 设置'; $null -ne $script:settingsWindow }) 'Settings window did not open.'
    try {
        $reduce=Find-Id $settingsWindow 'ReduceMotionBox'
        $settingsScroll=Find-Id $settingsWindow 'SettingsScroll'
        $cancel=Find-Name $settingsWindow '取消' ([Windows.Automation.ControlType]::Button)
        $save=Find-Name $settingsWindow '保存设置' ([Windows.Automation.ControlType]::Button)
        Require ($null -ne $reduce -and $null -ne $settingsScroll) 'Reduce-motion switch or settings scroll area is missing.'
        foreach ($id in 'CloseToTrayBox','MinimizeToTrayBox','RememberHistoryBox') { Require ($null -ne (Find-Id $settingsWindow $id)) "Settings content missing: $id" }
        Require ((Is-Visible $cancel) -and (Is-Visible $save)) 'Settings footer buttons are not visible.'
        Add-Check 'SettingsContent' 'Passed' 'Window behavior, reduce-motion and local-history controls are present; no toggle was changed.'
        $toggleBefore=$reduce.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString()
        $footerBefore=Get-Rect $save
        $scroll=$settingsScroll.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
        $didScroll=$scroll.Current.VerticallyScrollable
        if ($didScroll) { $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll,100); Start-Sleep -Milliseconds 200 }
        $footerAfter=Get-Rect $save
        $history=Find-Id $settingsWindow 'RememberHistoryBox'
        $scrollBounds=$settingsScroll.Current.BoundingRectangle
        $historyBounds=$history.Current.BoundingRectangle
        $windowBounds=$settingsWindow.Current.BoundingRectangle
        $footerGood=(Is-Visible $save) -and (Is-Visible $cancel) -and [Math]::Abs($footerBefore.Y-$footerAfter.Y) -le 1 -and $footerAfter.Bottom -le $windowBounds.Bottom -and $footerAfter.Y -ge ($scrollBounds.Bottom-1)
        $historyGood=(Is-Visible $history) -and $historyBounds.Bottom -le ($scrollBounds.Bottom+1)
        Require ($footerGood -and $historyGood) 'Settings footer moved/clipped or the last setting is still clipped after scrolling.'
        Add-Check 'SettingsFixedFooterAndLastRow' 'Passed' $(if ($didScroll) { 'ScrollPattern moved content to the bottom; footer coordinates stayed fixed and last setting is fully visible.' } else { 'All content fits without scrolling; footer and final setting are inside the window. Fixed position during scrolling was not exercised.' }) @{ ScrollExercised=$didScroll; FooterBefore=$footerBefore; FooterAfter=$footerAfter }
        Require ($reduce.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString() -eq $toggleBefore) 'Reduce-motion toggle changed during read-only inspection.'
        Require (Is-Visible (Find-Id $settingsWindow 'SettingsCopyEmail')) 'Feedback email action is clipped.'
        Require ($null -ne (Find-Name $settingsWindow 'Find the You.')) 'Settings signature is missing.'
        Add-Check 'SettingsFeedback' 'Passed' 'Feedback action and Find the You. are available after real scrolling.'
        Capture-Window $settingsWindow 'installed-workbench-settings'
    } finally {
        if ($null -ne $settingsWindow) {
            Invoke-Control (Find-Name $settingsWindow '取消' ([Windows.Automation.ControlType]::Button))
            Require (Wait-Until { $null -eq (Find-AppWindow 'AFMC FIND · 设置') }) 'Settings did not close after Cancel.'
            $settingsWindow=$null
        }
    }
    Add-Check 'SettingsCancelled' 'Passed' 'Closed with Cancel; never invoked Save or toggled preferences.'

    if ($Search) {
        $category=Find-Id $main 'CategoryBox'
        $categorySelection=@(Get-Selection $category)
        Require ($categorySelection.Count -eq 1 -and $categorySelection[0].Current.Name -eq '全部类型') 'Real ubuntu check requires existing All Types selection; script does not rewrite user filters.'
        $query=Find-Id $main 'QueryBox'
        $query.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('ubuntu')
        $searchStarted=$true
        $timer=[Diagnostics.Stopwatch]::StartNew()
        Invoke-Control (Find-Id $main 'SearchButton')
        Start-Sleep -Milliseconds 400
        $finished=Wait-Until { $button=Find-Id $main 'CancelButton'; !(Is-Visible $button) -or !$button.Current.IsEnabled } $SearchTimeoutSeconds
        $report.Search.ElapsedSeconds=[Math]::Round($timer.Elapsed.TotalSeconds,2)
        $report.Search.StatusText=(Find-Id $main 'StatusText').Current.Name
        Require $finished "Real search did not finish within $SearchTimeoutSeconds seconds; result acceptance is not passed."
        $searchStarted=$false
        $results=Find-Id $main 'ResultsList'
        $items=$results.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
        $report.Search.RealizedListItems=$items.Count
        Require ($items.Count -gt 0) 'Real search returned no displayed results (including network/source failure); no synthetic rows are injected.'
        Require (@(Get-Selection $results).Count -eq 0 -and !(Is-Visible (Find-Id $main 'DetailCloseButton'))) 'New search auto-selected a result or opened detail unexpectedly.'
        Add-Check 'RealSearchWithoutAutoSelection' 'Passed' 'Real ubuntu results appeared with no selected result and detail hidden.' @{ RealizedItems=$items.Count; Seconds=$report.Search.ElapsedSeconds }
        $items[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Require (Wait-Until { Is-Visible (Find-Id $main 'DetailCloseButton') }) 'Selecting a real result did not reveal detail close/action controls.'
        Add-Check 'SelectShowsDetail' 'Passed' 'UIA selected an actual returned resource; detail close button is visible.'
        Capture-Window $main 'installed-workbench-selected'
        Invoke-Control (Find-Id $main 'DetailCloseButton')
        Require (Wait-Until { !(Is-Visible (Find-Id $main 'DetailCloseButton')) -and @(Get-Selection $results).Count -eq 0 }) 'Detail close did not restore unselected/full-list state.'
        Add-Check 'CloseDetailClearsSelection' 'Passed' 'Detail close hides the pane and UIA selection is empty.'
        $items[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Require (Wait-Until { Is-Visible (Find-Id $main 'CopyButton') }) 'Reselecting resource did not restore copy action.'
        $copy=Find-Id $main 'CopyButton';$download=Find-Id $main 'OpenButton'
        Require ([Math]::Abs($copy.Current.BoundingRectangle.Width-$download.Current.BoundingRectangle.Width) -le 2) 'Download and copy widths differ.'
        Require ($null -ne (Find-Id $main 'DetailSourceEvidence') -and $null -eq (Find-Id $main 'MoreActionsButton')) 'Direct source explanation or replacement of ellipsis is missing.'
        Add-Check 'EqualDownloadCopyAndDirectEvidence' 'Passed' 'Real selected result has equal download/copy actions and inline source evidence.'
        if(Is-Visible (Find-Id $main 'DetailCloseButton')){Invoke-Control (Find-Id $main 'DetailCloseButton')}
        Require (Wait-Until { !(Is-Visible (Find-Id $main 'DetailCloseButton')) -and @(Get-Selection $results).Count -eq 0 }) 'Detail close did not restore unselected/full-list state.'
        Capture-Window $main 'installed-workbench-results'
    } else {
        Add-Check 'RealSearchAndSelectionFlow' 'Skipped' 'No -Search switch: network query, result selection, contextual actions, and detail closing were not exercised.'
    }
} catch {
    Add-Check 'AcceptanceFlow' $(if ($_.Exception.Message.StartsWith('UNVERIFIED:')) { 'Unverified' } else { 'Failed' }) $_.Exception.Message
} finally {
    # Only dismiss a settings window opened by this run, using its Cancel command.
    if ($null -ne $settingsWindow) {
        try { Invoke-Control (Find-Name $settingsWindow '取消' ([Windows.Automation.ControlType]::Button)) }
        catch { Add-Check 'SettingsCleanup' 'Unverified' 'Could not dismiss test-opened settings with Cancel; main process was left running.' }
    }
    if ($searchStarted -and $null -ne $main) {
        try { $cancel=Find-Id $main 'CancelButton'; if ((Is-Visible $cancel) -and $cancel.Current.IsEnabled) { Invoke-Control $cancel; Add-Check 'SearchCleanup' 'Passed' 'Cancelled only the timed-out search started by this script; returned rows remain.' } }
        catch { Add-Check 'SearchCleanup' 'Unverified' 'Could not cancel this script-owned query; app was left running.' }
    }
    try {
        $afterHashes=Get-ConfigHashes
        $same=$true
        foreach ($name in $beforeHashes.Keys) { if ($beforeHashes[$name] -ne $afterHashes[$name]) { $same=$false } }
        Add-Check 'ConfigurationUnchanged' $(if ($same) { 'Passed' } else { 'Failed' }) 'Compared SHA-256/existence of preferences.json and sources.json before/after; no contents or credentials recorded.'
    } catch { Add-Check 'ConfigurationUnchanged' 'Unverified' $_.Exception.Message }
    $report.CompletedUtc=[DateTime]::UtcNow.ToString('o')
    $failed=@($checks | Where-Object Status -eq 'Failed').Count
    $unverified=@($checks | Where-Object Status -eq 'Unverified').Count
    $report.Overall=$(if ($failed -gt 0) { 'Failed' } elseif ($unverified -gt 0) { 'Unverified' } elseif ($Search) { 'PassedUIAndRealSearch' } else { 'PassedUIOnlySearchNotExercised' })
    $report.MainProcessLeftRunning=$(if ($null -ne $process) { $null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) } else { $false })
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence 'installed-workbench.json') -Encoding UTF8
    Write-Host "Evidence: $(Join-Path $evidence 'installed-workbench.json')"
}
if ($failed -gt 0) { exit 1 }
if ($unverified -gt 0) { exit 2 }
