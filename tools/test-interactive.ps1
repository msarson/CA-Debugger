# Paced interactive smoke test for the Phase 2 engine.
# Launches ClarionDbg break --interactive, waits for the paused event, then issues
# step / stepover / stepout / continue with real delays, printing all output.
param(
    [string]$Engine = "H:\DevLaptop\Projects\ClarionDebugger\src\ClarionDbg.Cli\bin\Debug\net48\ClarionDbg.exe",
    [string]$Target = "C:\Users\Public\Documents\SoftVelocity\Clarion11\Examples\HowToClarion\Browses\clbrws.exe",
    [string]$BreakArgs = "--bp clbrws001.clw:11",
    [string[]]$Commands = @("step", "step", "stepover", "stepout", "quit"),
    [int]$PauseTimeoutSec = 20
)

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Engine
$psi.Arguments = "break `"$Target`" $BreakArgs --interactive --json"
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi

# synchronized sink filled from the OutputDataReceived event (runs on a threadpool thread)
$sink = [System.Collections.ArrayList]::Synchronized((New-Object System.Collections.ArrayList))
$handler = Register-ObjectEvent -InputObject $proc -EventName OutputDataReceived -MessageData $sink -Action {
    if ($EventArgs.Data -ne $null) { [void]$Event.MessageData.Add($EventArgs.Data) }
}

[void]$proc.Start()
$proc.BeginOutputReadLine()

$script:cursor = 0
function Wait-Paused([int]$timeoutSec) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        while ($script:cursor -lt $sink.Count) {
            $line = $sink[$script:cursor]; $script:cursor++
            Write-Host $line
            if ($line -match '"event":"paused"') { return $true }
            if ($line -match '"event":"exited"') { return $false }
        }
        if ($proc.HasExited -and $script:cursor -ge $sink.Count) { return $false }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

function Drain {
    while ($script:cursor -lt $sink.Count) { Write-Host $sink[$script:cursor]; $script:cursor++ }
}

if (-not (Wait-Paused $PauseTimeoutSec)) {
    Drain
    Write-Host "!! never paused (breakpoint not reached) — killing"
    try { $proc.StandardInput.WriteLine("quit") } catch {}
    $proc.WaitForExit(5000) | Out-Null
    if (-not $proc.HasExited) { $proc.Kill() }
    Unregister-Event -SourceIdentifier $handler.Name
    exit 3
}

foreach ($cmd in $Commands) {
    Write-Host ">>> $cmd"
    $proc.StandardInput.WriteLine($cmd)
    if ($cmd -eq "quit") { break }
    if ($cmd -in @("step", "stepover", "stepout", "continue")) {
        if (-not (Wait-Paused $PauseTimeoutSec)) {
            Drain
            Write-Host "!! did not pause again after '$cmd' — killing"
            try { $proc.StandardInput.WriteLine("quit") } catch {}
            break
        }
    } else {
        Start-Sleep -Milliseconds 300
        Drain
    }
}

$proc.WaitForExit(10000) | Out-Null
if (-not $proc.HasExited) { $proc.Kill(); Write-Host "!! force-killed" }
Start-Sleep -Milliseconds 300
Drain
Write-Host "=== exit code: $($proc.ExitCode) ==="
Unregister-Event -SourceIdentifier $handler.Name
