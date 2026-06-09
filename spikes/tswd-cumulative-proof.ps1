$Exe="C:\Users\Public\Documents\SoftVelocity\Clarion11\Examples\HowToClarion\Browses\clbrws.exe"
$b=[System.IO.File]::ReadAllBytes($Exe); $base=0x16CC00
$offA=[BitConverter]::ToUInt32($b,$base+0x14); $offB=[BitConverter]::ToUInt32($b,$base+0x18)
# run 3 starts at byte 0x1B90. Walk grid from there, show records with table-line in 990..1030 and 1500..1525, with rva.
$prev=-1
for($p=$base+0x1B90; $p+6 -le $base+$offB; $p+=6){
  $ln=[BitConverter]::ToUInt16($b,$p); $rva=[BitConverter]::ToUInt32($b,$p+2)
  if($prev -ne -1 -and $ln -lt $prev-2){ "  -- RESET at byte 0x{0:X} (line {1} < prev {2}) : end of run 3 --" -f ($p-$base),$ln,$prev; break }
  if(($ln -ge 990 -and $ln -le 1030) -or ($ln -ge 1498)){ "  line {0,5}  rva 0x{1:X6}" -f $ln,$rva }
  $prev=$ln
}
