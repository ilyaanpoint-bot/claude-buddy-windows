# Removes Claude Buddy's hooks, shortcuts and startup entry, and quits it.
# Your progress (save.json) stays in %USERPROFILE%\.claude\claude-buddy so a reinstall keeps it.
$ErrorActionPreference = "SilentlyContinue"
Get-Process ClaudeBuddy | Stop-Process -Force
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "ClaudeBuddy"
foreach ($dir in @([Environment]::GetFolderPath("Programs"), [Environment]::GetFolderPath("Desktop"))) {
    Remove-Item (Join-Path $dir "Claude Buddy.lnk")
}
$settingsPath = Join-Path $env:USERPROFILE ".claude\settings.json"
if (Test-Path $settingsPath) {
    Copy-Item $settingsPath "$settingsPath.backup-before-uninstall" -Force
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if ($settings.hooks) {
        foreach ($name in @($settings.hooks.PSObject.Properties.Name)) {
            $kept = @()
            foreach ($entry in @($settings.hooks.$name)) {
                $entry.hooks = @($entry.hooks | Where-Object { $_.command -notlike "*claude-buddy/hook.ps1*" })
                if ($entry.hooks.Count -gt 0) { $kept += $entry }
            }
            if ($kept.Count -gt 0) { $settings.hooks.$name = $kept } else { $settings.hooks.PSObject.Properties.Remove($name) }
        }
    }
    [System.IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 32), (New-Object System.Text.UTF8Encoding $false))
}
Write-Host "Claude Buddy removed. Your progress is still in $env:USERPROFILE\.claude\claude-buddy\save.json"
