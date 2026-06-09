# Item-5 probe: is the .cwtls template instance readable in the live target?
# sym JOB:JOBID -> take the returned VA -> mem read the whole JOBS record buffer.
$exe = "H:\DevLaptop\Projects\ClarionDebugger\src\ClarionDbg.Cli\bin\Debug\net48\ClarionDbg.exe"
$target = "C:\Users\Public\Documents\SoftVelocity\Clarion11\Examples\HowToClarion\Browses\clbrws.exe"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = "break `"$target`" --bp clbrws002:299 --interactive --json"
$psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)

$deadline = (Get-Date).AddSeconds(30)
$state = "wait-pause"; $va = $null
while ((Get-Date) -lt $deadline) {
    $line = $p.StandardOutput.ReadLine()
    if ($null -eq $line) { break }
    $line
    if ($state -eq "wait-pause" -and $line -match '"event":"paused"') {
        $p.StandardInput.WriteLine("sym JOBS`$JOB:RECORD")
        $state = "wait-sym"
    }
    elseif ($state -eq "wait-sym" -and $line -match '"event":"sym".*"va":"(0x[0-9A-F]+)"') {
        $va = $Matches[1]
        $p.StandardInput.WriteLine("mem $va 55")
        $state = "wait-mem"
    }
    elseif ($state -eq "wait-mem" -and $line -match '"event":"mem"') {
        $p.StandardInput.WriteLine("quit")
        $state = "done"
    }
    elseif ($line -match '"event":"exited"') { break }
}
if ($state -ne "done") { "PROBE INCOMPLETE (state=$state)"; try { $p.Kill() } catch {} } else { "PROBE OK" }