# Launch StudioNEO with UserData/HSPE/selftest.txt = <Mode>, wait for "undotest: done", close the game, disable the
# trigger again and print the undotest lines. Modes: undo, bonepresets. Needs Plugins\Logs\enabled.txt (HSPE's Kit.Log).
# -HS = the game folder (default: ..\HoneySelect next to the repository, like HSPE.csproj's HSDir).
param([string]$Mode = "undo", [int]$Timeout = 240,
      [string]$HS = (Join-Path $PSScriptRoot '..\..\HoneySelect'))
$log = "$HS\Plugins\Logs\HSPlugins.debug.log"
if (Get-Process StudioNEO_64, StudioNEO_32 -ErrorAction SilentlyContinue) { "StudioNEO is already running - not launching a second copy"; exit 1 }
if (-not (Test-Path "$HS\Plugins\Logs\enabled.txt")) { "Plugins\Logs\enabled.txt is missing - HSPE would not log"; exit 1 }
$dir = "$HS\UserData\HSPE"; New-Item -ItemType Directory -Force $dir | Out-Null
Set-Content "$dir\selftest.txt" $Mode -NoNewline
$before = (Get-Item $log).Length
$p = Start-Process -FilePath "$HS\StudioNEO_64.exe" -WorkingDirectory $HS -PassThru
$t = 0
try {
    do {
        Start-Sleep 5; $t += 5
        $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite'); $fs.Seek($before, 'Begin') | Out-Null
        $new = (New-Object IO.StreamReader($fs)).ReadToEnd(); $fs.Close()
    } until ($new -match "undotest: done" -or $t -ge $Timeout -or $p.HasExited)
}
finally {
    Start-Sleep 2
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    $p.WaitForExit(15000) | Out-Null # the next run's "already running" check must not see this one
    Move-Item "$dir\selftest.txt" "$dir\selftest.off" -Force
}
"waited $t s; game closed"
$lines = $new -split "`n" | Where-Object { $_ -match 'undotest:|undo: |audit\[|spill|overlap' -or ($_ -match 'HSPE' -and $_ -match 'ERROR|Exception|failed') }
$lines | Set-Content (Join-Path $PSScriptRoot "selftest_$Mode.log")
$lines | ForEach-Object { $_.Trim() }
