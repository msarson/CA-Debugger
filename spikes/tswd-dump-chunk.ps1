$Exe = "C:\Users\Public\Documents\SoftVelocity\Clarion11\Examples\HowToClarion\Browses\clbrws.exe"
$b = [System.IO.File]::ReadAllBytes($Exe)
$base = 0x16CC00
# clbrws011 slice 0x1986..0x1BE6 phase 0
$r=New-Object Collections.Generic.List[object]
for($p=$base+0x1986; $p+6 -le $base+0x1BE6; $p+=6){
  $ln=[BitConverter]::ToUInt16($b,$p); $rva=[BitConverter]::ToUInt32($b,$p+2)
  $r.Add([pscustomobject]@{Off=("0x{0:X}" -f ($p-$base));Line=$ln;Rva=$rva})
}
Write-Host "=== clbrws011 ALL records in FILE order (decode order) ==="
$i=0; foreach($x in $r){ "{0,3}: off {1,-7} line {2,5}  rva 0x{3:X}" -f $i,$x.Off,$x.Line,$x.Rva; $i++ }
