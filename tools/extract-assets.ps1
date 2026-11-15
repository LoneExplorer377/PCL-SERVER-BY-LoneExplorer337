
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'assets')
)
$ErrorActionPreference = 'Stop'
$exe = $Exe
$out = $Out
$a = [System.Reflection.Assembly]::LoadFrom($exe)

foreach ($rn in $a.GetManifestResourceNames()) {
    $s = $a.GetManifestResourceStream($rn)
    $ms = New-Object System.IO.MemoryStream
    $s.CopyTo($ms)
    $bytes = $ms.ToArray()
    $manDir = Join-Path $out 'manifest'
    New-Item -ItemType Directory -Force -Path $manDir | Out-Null
    [System.IO.File]::WriteAllBytes((Join-Path $manDir $rn), $bytes)
    Write-Host ("MANIFEST {0}  {1:N0} bytes" -f $rn, $bytes.Length)

    if ($rn -like '*.resources') {
        $unDir = Join-Path $out ('unpacked\' + $rn)
        $s.Position = 0
        $rr = New-Object System.Resources.ResourceReader($s)
        $count = 0
        foreach ($e in $rr) {
            $key = [string]$e.Key
            $val = $e.Value
            $rel = ($key -replace '/', '\')
            $target = Join-Path $unDir $rel
            $parent = Split-Path $target -Parent
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
            if ($val -is [byte[]]) {
                [System.IO.File]::WriteAllBytes($target, $val)
            } elseif ($val -is [System.IO.Stream]) {
                $fs = [System.IO.File]::Create($target)
                $val.CopyTo($fs); $fs.Close()
            } else {
                Set-Content -Path ($target + '.txt') -Value ([string]$val) -Encoding UTF8
                Write-Host ("  OTHER {0} = {1}" -f $key, $val.GetType().Name)
            }
            $count++
        }
        Write-Host ("  UNPACKED {0} entries -> {1}" -f $count, $unDir)
    }
}
