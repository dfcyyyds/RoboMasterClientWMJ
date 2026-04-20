@echo off
rem ═══════════════════════════════════════════════════════════════════════
rem RoboMasterClient Windows 启动器 —— 基于 manifest.json 的 HTTPS 增量更新
rem
rem 依赖：Windows 10 1809+（自带 curl + PowerShell 5+）
rem
rem 环境变量:
rem   ROBOMASTER_UPDATE_URL    默认 https://antientropy.xin/robomaster/Windows64/
rem   ROBOMASTER_SKIP_UPDATE=1 跳过更新
rem ═══════════════════════════════════════════════════════════════════════
setlocal EnableDelayedExpansion
cd /d "%~dp0"

set "INSTALL_DIR=%CD%"
if "%ROBOMASTER_UPDATE_URL%"=="" (
    set "UPDATE_URL=https://antientropy.xin/robomaster/Windows64/"
) else (
    set "UPDATE_URL=%ROBOMASTER_UPDATE_URL%"
)
set "CLIENT_BIN=%INSTALL_DIR%\RoboMasterClient.exe"

echo [launcher] === RoboMasterClient Launcher (Windows) ===
echo [launcher] Install: %INSTALL_DIR%
echo [launcher] Source:  %UPDATE_URL%

if "%ROBOMASTER_SKIP_UPDATE%"=="1" (
    echo [launcher] ROBOMASTER_SKIP_UPDATE=1, skip update
    goto :RUN
)

rem ── 调 PowerShell 完成 manifest 对比 + 下载 ──
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop'; $install=$env:INSTALL_DIR; $base=$env:UPDATE_URL.TrimEnd('/')+'/'; $manifestUrl=$base+'manifest.json'; $filesBase=$base+'files/'; $local=Join-Path $install 'manifest.local.json';" ^
  "try { $tmp=Join-Path $env:TEMP 'rm_manifest.json'; Invoke-WebRequest -Uri $manifestUrl -OutFile $tmp -TimeoutSec 15 -UseBasicParsing } catch { Write-Host '[launcher] WARN manifest 下载失败, 沿用本地版本'; exit 0 };" ^
  "$remote = Get-Content $tmp -Raw | ConvertFrom-Json;" ^
  "$remoteMap = @{}; foreach ($e in $remote.files) { $remoteMap[$e.path] = $e };" ^
  "$cache = @{}; if (Test-Path $local) { try { foreach ($e in (Get-Content $local -Raw | ConvertFrom-Json).files) { $cache[$e.path] = $e } } catch { } };" ^
  "function Get-Sha256($p) { (Get-FileHash -Path $p -Algorithm SHA256).Hash.ToLower() };" ^
  "$need=@(); $total=0L;" ^
  "foreach ($rel in $remoteMap.Keys) { $entry=$remoteMap[$rel]; $abs=Join-Path $install ($rel -replace '/','\\'); $ok=$false; if (Test-Path $abs) { $fi=Get-Item $abs; $c=$cache[$rel]; if ($c -and $c.size -eq $fi.Length -and $c.sha256 -eq $entry.sha256) { $ok=$true } elseif ($fi.Length -eq $entry.size -and (Get-Sha256 $abs) -eq $entry.sha256) { $ok=$true } }; if (-not $ok) { $need+=$entry; $total+=$entry.size } };" ^
  "$localPaths=@{}; Get-ChildItem -Path $install -Recurse -File | ForEach-Object { $rel=(Resolve-Path -LiteralPath $_.FullName -Relative).TrimStart('.\\','\\') -replace '\\','/'; if ($rel -notin @('launch.sh','launch.bat','manifest.local.json')) { $localPaths[$rel]=$true } };" ^
  "$toRemove = @($localPaths.Keys | Where-Object { -not $remoteMap.ContainsKey($_) });" ^
  "if ($need.Count -eq 0 -and $toRemove.Count -eq 0) { Write-Host ('[launcher] OK 已是最新版本 (' + $remote.version + ')'); Copy-Item $tmp $local -Force; exit 0 };" ^
  "$mb=[math]::Round($total/1MB,1); Write-Host ('[launcher] 发现更新 ' + $remote.version + ': ' + $need.Count + ' 个文件 (' + $mb + ' MB), ' + $toRemove.Count + ' 个文件需删除');" ^
  "$i=0; foreach ($entry in $need) { $i++; $rel=$entry.path; $url=$filesBase + [System.Uri]::EscapeUriString($rel); $dst=Join-Path $install ($rel -replace '/','\\'); $dir=Split-Path $dst; if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }; $tmpDst=$dst + '.tmp.download'; & curl.exe -fSL --retry 3 --retry-delay 2 -C - -o $tmpDst $url; if ($LASTEXITCODE -ne 0) { Write-Host ('[launcher] ERR 下载失败: ' + $rel); exit 2 }; if ((Get-Sha256 $tmpDst) -ne $entry.sha256) { Write-Host ('[launcher] ERR SHA256 不匹配: ' + $rel); Remove-Item $tmpDst -Force; exit 3 }; if (Test-Path $dst) { Remove-Item $dst -Force }; Move-Item $tmpDst $dst; Write-Host ('[launcher]   [' + $i + '/' + $need.Count + '] ' + $rel) };" ^
  "foreach ($rel in $toRemove) { $abs=Join-Path $install ($rel -replace '/','\\'); try { Remove-Item $abs -Force -ErrorAction Stop; Write-Host ('[launcher]   x 移除 ' + $rel) } catch { } };" ^
  "Get-ChildItem -Path $install -Recurse -Directory | Sort-Object FullName -Descending | Where-Object { @(Get-ChildItem -LiteralPath $_.FullName -Force).Count -eq 0 } | Remove-Item -Force -ErrorAction SilentlyContinue;" ^
  "Copy-Item $tmp $local -Force;" ^
  "Write-Host ('[launcher] OK 更新完成 -> ' + $remote.version);"

if errorlevel 2 (
    echo [launcher] WARN 更新过程出错, 沿用现有版本启动
)

:RUN
if not exist "%CLIENT_BIN%" (
    echo [launcher] ERROR 找不到可执行文件: %CLIENT_BIN%
    pause
    exit /b 1
)

if exist "%INSTALL_DIR%\manifest.local.json" (
    for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Content '%INSTALL_DIR%\manifest.local.json' -Raw | ConvertFrom-Json).version"`) do set "VER=%%v"
    echo [launcher] 启动 RoboMasterClient ^(version=!VER!^)
)

start "" "%CLIENT_BIN%" %*
endlocal
