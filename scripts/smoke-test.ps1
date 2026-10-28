# Launches a published Blanky.exe the way a user would and fails if it doesn't
# render, play and shut down cleanly. Run by CI against every publish.
#
#   scripts/smoke-test.ps1 -Exe publish/Blanky.exe
param(
    [Parameter(Mandatory)] [string] $Exe,
    [int] $TimeoutSeconds = 90
)
$ErrorActionPreference = "Stop"

function Invoke-Blanky([string] $Name, [string[]] $Arguments) {
    $data = Join-Path ([IO.Path]::GetTempPath()) "blanky-smoke-$Name-$PID"
    Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $Exe -ArgumentList (@("--data", "`"$data`"") + $Arguments) -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        throw "${Name}: Blanky didn't exit within $TimeoutSeconds s"
    }

    $log = Join-Path $data "blanky.log"
    $text = if (Test-Path $log) { Get-Content -Raw $log } else { "" }
    Write-Host "--- $Name log ---`n$text"
    # Warnings are expected on machines without an audio device; errors never are.
    if ($process.ExitCode -ne 0) { throw "${Name}: exit code $($process.ExitCode)" }
    if ($text -notmatch "starting, data in") { throw "${Name}: the log was never written" }
    if ($text -match "Blanky Error") { throw "${Name}: errors in the log" }
    if ($text -notmatch "Shutdown: state saved") { throw "${Name}: no clean shutdown" }
    # The playing sounds are saved with the preset; settings.json only appears once a setting changes.
    if (-not (Test-Path (Join-Path $data "presets.json"))) { throw "${Name}: the mix wasn't saved" }
    return $data
}

# The window renders: a screenshot with sounds playing.
$shot = Join-Path ([IO.Path]::GetTempPath()) "blanky-smoke-$PID.png"
Invoke-Blanky "render" @("--demo", "rain,waves,fireplace", "--capture", "`"$shot`"") | Out-Null
if (-not (Test-Path $shot) -or (Get-Item $shot).Length -lt 10KB) { throw "render: no screenshot" }
Write-Host "Screenshot: $((Get-Item $shot).Length / 1KB) KB"

# Audio decodes and plays for a few seconds, then the app closes cleanly.
Invoke-Blanky "play" @("--demo", "rain,brown-noise,lofi", "--exit-after", "6") | Out-Null
Write-Host "Smoke test passed"
