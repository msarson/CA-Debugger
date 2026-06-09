$Exe = "C:\Users\Public\Documents\SoftVelocity\Clarion11\Examples\HowToClarion\Browses\clbrws.exe"
$b = [System.IO.File]::ReadAllBytes($Exe); $base=0x16CC00
$offA=[BitConverter]::ToUInt32($b,$base+0x14); $offB=[BitConverter]::ToUInt32($b,$base+0x18)
$txtLo=0x1000; $txtHi=0xE0000

# module names + +0x10 chunks
$offModArr=[BitConverter]::ToUInt32($b,$base+0x08); $offModPool=[BitConverter]::ToUInt32($b,$base+0x0C); $offRange=[BitConverter]::ToUInt32($b,$base+0x10)
$names=New-Object Collections.Generic.List[string]
for($o=$offModArr;$o+4 -le $offModPool;$o+=4){ $no=[BitConverter]::ToUInt32($b,$base+$o); $s=$base+$offModPool+$no;$e=$s;while($b[$e]-ne 0){$e++}; $names.Add([Text.Encoding]::ASCII.GetString($b,$s,$e-$s)) }
$chunks=@()
for($i=0;$i -lt $names.Count;$i++){ $ro=$base+$offRange+$i*8; $chunks += [pscustomobject]@{Idx=$i;Name=$names[$i];S=[BitConverter]::ToUInt32($b,$ro);E=[BitConverter]::ToUInt32($b,$ro+4)} }
function OwnerName($byteOff){ foreach($c in $chunks){ if($c.E -gt $c.S -and $byteOff -ge $c.S -and $byteOff -lt $c.E){ return $c.Name } } return "?" }

# Walk one continuous 6-byte grid from offA; segment by line reset (line < prevLine)
$runs=New-Object Collections.Generic.List[object]
$cur=$null; $idx=0
for($p=$base+$offA; $p+6 -le $base+$offB; $p+=6){
  $ln=[BitConverter]::ToUInt16($b,$p); $rva=[BitConverter]::ToUInt32($b,$p+2)
  $valid = ($rva -ge $txtLo -and $rva -lt $txtHi -and $ln -ge 1 -and $ln -lt 20000)
  $boff = $p - $base
  if(-not $valid){ if($cur){$runs.Add($cur);$cur=$null}; $idx++; continue }
  if($cur -ne $null -and $ln -lt $cur.LMax - 2){ $runs.Add($cur); $cur=$null }   # reset (allow tiny non-monotonic noise)
  if($cur -eq $null){ $cur=[pscustomobject]@{StartRec=$idx;StartByte=$boff;Phase=(($boff-$offA)%6);LMin=$ln;LMax=$ln;RvaMin=$rva;RvaMax=$rva;N=0;Owner=(OwnerName $boff)} }
  if($ln -lt $cur.LMin){$cur.LMin=$ln}; if($ln -gt $cur.LMax){$cur.LMax=$ln}
  if($rva -lt $cur.RvaMin){$cur.RvaMin=$rva}; if($rva -gt $cur.RvaMax){$cur.RvaMax=$rva}
  $cur.N++; $idx++
}
if($cur){$runs.Add($cur)}
# keep runs of meaningful size
$runs = $runs | Where-Object { $_.N -ge 3 }
"Table A [0x{0:X},0x{1:X})  records={2}  runs(>=3 rec)={3}  code-modules=56" -f $offA,$offB,[math]::Floor(($offB-$offA)/6),$runs.Count | Write-Host
"`n{0,3} {1,9} {2,8} {3,3} {4,6} {5,6} {6,9} {7,9} {8,5}  {9}" -f "run","startByte","startRec","ph","lMin","lMax","rvaMin","rvaMax","nrec","owner(+0x10)" | Write-Host
$i=0
foreach($r in $runs){ "{0,3} 0x{1:X6}   {2,6} {3,3} {4,6} {5,6} 0x{6:X6} 0x{7:X6} {8,5}  {9}" -f $i,$r.StartByte,$r.StartRec,$r.Phase,$r.LMin,$r.LMax,$r.RvaMin,$r.RvaMax,$r.N,$r.Owner | Write-Host; $i++ }
