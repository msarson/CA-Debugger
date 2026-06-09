$Exe = "C:\Users\Public\Documents\SoftVelocity\Clarion11\Examples\HowToClarion\Browses\clbrws.exe"
$b = [System.IO.File]::ReadAllBytes($Exe)
$base = 0x16CC00
$offA=[BitConverter]::ToUInt32($b,$base+0x14); $offB=[BitConverter]::ToUInt32($b,$base+0x18)
$regionBytes = $offB-$offA
Write-Host ("Table A region [0x{0:X},0x{1:X}) = {2} bytes = {3} recs(/6)" -f $offA,$offB,$regionBytes,[math]::Floor($regionBytes/6))

# PIN#1: re-interpret +0x10 pair for module[37] as indices vs bytes
$ro=$base+0x444+37*8; $s=[BitConverter]::ToUInt32($b,$ro); $e=[BitConverter]::ToUInt32($b,$ro+4)
Write-Host ("`nmodule[37] +0x10 pair raw = {0} , {1}  (0x{0:X},0x{1:X})" -f $s,$e)
Write-Host ("  as BYTE offsets: into region? start-0x624={0}, fits={1}" -f ($s-$offA),($e -le $offB))
Write-Host ("  as RECORD index: start*6={0} (region has {1} B) -> {2}" -f ($s*6),$regionBytes,(if($s*6 -gt $regionBytes){"OVERFLOW"}else{"fits"}))

# decode a slice at given phase, file order
function Dump($name,$ss,$ee,$ph,$head,$tail){
  $r=New-Object Collections.Generic.List[object]
  for($p=$base+$ss+$ph;$p+6 -le $base+$ee;$p+=6){ $r.Add([pscustomobject]@{Line=[BitConverter]::ToUInt16($b,$p);Rva=[BitConverter]::ToUInt32($b,$p+2)}) }
  Write-Host ("`n=== {0} slice 0x{1:X}..0x{2:X} ph{3} ({4} recs) ===" -f $name,$ss,$ee,$ph,$r.Count)
  Write-Host ("  HEAD: " + (($r | Select -First $head | ForEach-Object {"{0}@0x{1:X}" -f $_.Line,$_.Rva}) -join "  "))
  Write-Host ("  TAIL: " + (($r | Select -Last $tail | ForEach-Object {"{0}@0x{1:X}" -f $_.Line,$_.Rva}) -join "  "))
}
# phases from engine: 011 ph0, 012 ph3, 013 ph2, 014 ph5, 015 ph3
Dump "clbrws011" 0x1986 0x1BE6 0 4 6
Dump "clbrws012" 0x1BE7 0x1F8F 3 8 4
Dump "clbrws013" 0x1F90 0x21F0 2 8 4
Dump "clbrws014" 0x21F1 0x2510 5 8 4
Dump "clbrws015" 0x2511 0x278A 3 8 4
