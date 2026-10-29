# Claude Buddy hook: appends one small JSON line per Claude Code event for buddy to read.
# Usage (from ~/.claude/settings.json): powershell -File hook.ps1 prompt|stop|notify|end
param([string]$Event = "")
try {
    $raw = [Console]::In.ReadToEnd()
    $p = $null
    try { $p = $raw | ConvertFrom-Json } catch {}
    $o = [ordered]@{
        event      = $Event
        app        = "$env:TERM_PROGRAM"
        session_id = "$($p.session_id)"
        cwd        = "$($p.cwd)"
        message    = "$($p.message)"
        type       = "$($p.notification_type)"
    }
    $line = ($o | ConvertTo-Json -Compress) + "`n"
    $dir = Join-Path $env:USERPROFILE ".claude\claude-buddy"
    $file = Join-Path $dir "events.jsonl"
    $utf8 = New-Object System.Text.UTF8Encoding $false
    for ($i = 0; $i -lt 5; $i++) {
        try { [System.IO.File]::AppendAllText($file, $line, $utf8); break } catch { Start-Sleep -Milliseconds 40 }
    }
} catch {}
exit 0
