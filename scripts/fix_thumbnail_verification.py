from pathlib import Path
p=Path('scripts/Verify-Release.ps1');s=p.read_text(encoding='utf-8-sig')
s=s.replace("$filter.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()", "$toggle = $filter.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)\n    if ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off) { $toggle.Toggle() }")
s=s.replace('$thumbnailCount -gt 0 -or','$thumbnailCount -gt 2 -or').replace('if ($thumbnailCount -eq 0)', 'if ($thumbnailCount -lt 3)')
p.write_text(s,encoding='utf-8-sig')
