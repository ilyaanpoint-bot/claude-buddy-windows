# Installs Claude Buddy for Windows.
#   powershell -ExecutionPolicy Bypass -File install.ps1          install + start
#   powershell -ExecutionPolicy Bypass -File install.ps1 -Login   also start with Windows
#
# Builds from source if the .NET 8 SDK is installed, otherwise downloads the latest
# ClaudeBuddy.exe from GitHub Releases. Adds Start menu + desktop shortcuts and the
# Claude Code hooks (your settings.json is backed up first).
param([switch]$Login, [switch]$NoDesktopShortcut)
$ErrorActionPreference = "Stop"
$repo = "ilyaanpoint-bot/claude-buddy-windows"
$dest = Join-Path $env:USERPROFILE ".claude\claude-buddy"
$exe = Join-Path $dest "ClaudeBuddy.exe"
$here = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
New-Item -ItemType Directory -Force -Path $dest | Out-Null

Write-Host "-> Stopping any running buddy..."
Get-Process ClaudeBuddy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$proj = Join-Path $here "src\ClaudeBuddy.csproj"
$haveSdk = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((& dotnet --list-sdks 2>$null) -match "^8\.|^9\.|^10\.")
if ((Test-Path $proj) -and $haveSdk) {
    Write-Host "-> Building from source..."
    & dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $here "out") | Out-Host
    Copy-Item (Join-Path $here "out\ClaudeBuddy.exe") $exe -Force
} else {
    Write-Host "-> Downloading the latest ClaudeBuddy.exe..."
    Invoke-WebRequest "https://github.com/$repo/releases/latest/download/ClaudeBuddy.exe" -OutFile $exe -UseBasicParsing
}

$hookSrc = Join-Path $here "hook.ps1"
$hook = Join-Path $dest "hook.ps1"
if (Test-Path $hookSrc) { Copy-Item $hookSrc $hook -Force }
else { Invoke-WebRequest "https://raw.githubusercontent.com/$repo/main/hook.ps1" -OutFile $hook -UseBasicParsing }

Write-Host "-> Adding Claude Code hooks..."
$settingsPath = Join-Path $env:USERPROFILE ".claude\settings.json"
if (Test-Path $settingsPath) {
    Copy-Item $settingsPath "$settingsPath.backup-before-claude-buddy" -Force
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
} else { $settings = [pscustomobject]@{} }
if (-not $settings) { $settings = [pscustomobject]@{} }
if (-not ($settings.PSObject.Properties.Name -contains "hooks")) { $settings | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{}) }
$hookPath = $hook -replace "\\", "/"
foreach ($pair in @(@("UserPromptSubmit", "prompt"), @("Stop", "stop"), @("Notification", "notify"), @("SessionEnd", "end"))) {
    $event = $pair[0]
    $cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$hookPath`" $($pair[1])"
    if (-not ($settings.hooks.PSObject.Properties.Name -contains $event)) { $settings.hooks | Add-Member -NotePropertyName $event -NotePropertyValue @() }
    $entries = @($settings.hooks.$event)
    $exists = $entries | Where-Object { $_.hooks | Where-Object { $_.command -like "*claude-buddy/hook.ps1*" } }
    if (-not $exists) {
        $entries += [pscustomobject]@{ hooks = @([pscustomobject]@{ type = "command"; command = $cmd; timeout = 10 }) }
        $settings.hooks.$event = $entries
    }
}
$json = $settings | ConvertTo-Json -Depth 32
[System.IO.File]::WriteAllText($settingsPath, $json, (New-Object System.Text.UTF8Encoding $false))

Write-Host "-> Adding shortcuts..."
$shell = New-Object -ComObject WScript.Shell
$places = @([Environment]::GetFolderPath("Programs"))
if (-not $NoDesktopShortcut) { $places += [Environment]::GetFolderPath("Desktop") }
foreach ($dir in $places) {
    $lnk = $shell.CreateShortcut((Join-Path $dir "Claude Buddy.lnk"))
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $dest
    $lnk.Description = "Claude Buddy - opening it again brings buddy back to the corner"
    $lnk.Save()
}

if ($Login) {
    Write-Host "-> Starting buddy with Windows..."
    Set-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "ClaudeBuddy" -Value "`"$exe`""
}

Start-Process $exe
Write-Host ""
Write-Host "Claude Buddy is in the bottom-right corner of your screen."
Write-Host "Lost him or quit him? Open 'Claude Buddy' from the Start menu and he comes right back."
Write-Host "New Claude Code sessions will talk to him."
