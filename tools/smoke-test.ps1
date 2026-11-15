param(
    [string]$Source = (Join-Path $PSScriptRoot "..\src\bin\Release\net48"),
    # 不要放在 %TEMP% 下：PCL 在临时目录启动会弹窗并退出（返回码 4）
    [string]$Target = (Join-Path ([Environment]::GetFolderPath("UserProfile")) "pcl-smoke-check"),
    [int]$WaitSeconds = 20
)

$ErrorActionPreference = "Stop"
Write-Host "source: $Source"
Write-Host "target: $Target"

if (Test-Path $Target) { Remove-Item -Recurse -Force $Target }
New-Item -ItemType Directory -Path $Target -Force | Out-Null
Copy-Item (Join-Path $Source "*") -Destination $Target -Recurse -Force

$exe = Join-Path $Target "Plain Craft Launcher 2.exe"
# 必须用绝对路径 + WorkingDirectory 启动，否则 Paths.Base 会带上 .\ 前缀导致权限检查误报
$proc = Start-Process -FilePath $exe -WorkingDirectory $Target -PassThru
Start-Sleep -Seconds $WaitSeconds

if ($proc.HasExited) {
    Write-Host ("RESULT: EXITED code={0} (0x{0:X8})" -f $proc.ExitCode)
} else {
    Write-Host ("RESULT: ALIVE pid={0}" -f $proc.Id)
    Stop-Process -Id $proc.Id -Force
}

$log = Join-Path $Target "PCL\Log1.txt"
if (Test-Path $log) {
    $lines = Get-Content $log
    $bad = @($lines | Where-Object { $_ -match "\sE\s\[|错误|异常|Exception" })
    Write-Host ("LOG: {0} lines, {1} suspicious" -f $lines.Count, $bad.Count)
    $bad | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Host "LOG: not found"
}