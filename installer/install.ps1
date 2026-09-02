# ---------------------------------------------------------------
#  "오늘은" 위젯 설치 스크립트
#
#  USB / 네트워크로 옮긴 폴더에서 install.cmd 를 실행하면 이 파일이 돈다.
#  하는 일: 사용자 폴더로 복사 -> 차단 해제 -> 바탕화면 바로가기 -> 실행
# ---------------------------------------------------------------
$ErrorActionPreference = 'Stop'

$src = Split-Path -Parent $MyInvocation.MyCommand.Definition
$dst = Join-Path $env:LOCALAPPDATA '오늘은'

Write-Host ''
Write-Host '  오늘은 위젯 설치' -ForegroundColor Cyan
Write-Host '  ------------------------------------------------'

# ---------- 0) .NET Framework 4.8 확인 ----------
# Windows 10 1903 이상 / Windows 11 에는 기본으로 들어 있다.
$rel = 0
try {
    $rel = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' `
            -Name Release -ErrorAction Stop).Release
} catch { }

if ($rel -lt 528040) {
    Write-Host ''
    Write-Host '  [경고] .NET Framework 4.8 을 찾지 못했습니다.' -ForegroundColor Yellow
    Write-Host '         Windows 10 1903 이상 / Windows 11 이면 원래 들어 있습니다.'
    Write-Host '         없다면 아래에서 받아 설치한 뒤 다시 실행하세요.'
    Write-Host '         https://dotnet.microsoft.com/download/dotnet-framework/net48'
    Write-Host ''
} else {
    Write-Host "  .NET Framework   : 4.8 이상 확인 (Release $rel)"
}

# ---------- 1) 이미 떠 있으면 내린다 ----------
# exe 로 도는 경우와, DLL 폴백으로 powershell.exe 안에서 도는 경우를 모두 잡는다.
Get-Process 'Onuln', '오늘은' -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 200 } catch { }
    try { $_.Kill() } catch { }
}
$mePid = $PID
try {
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction Stop |
        Where-Object { $_.ProcessId -ne $mePid -and $_.CommandLine -like '*-File*launch.ps1*' } |
        ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch { } }
} catch { }
Start-Sleep -Milliseconds 400

# ---------- 2) 복사 ----------
New-Item -ItemType Directory -Force -Path $dst              | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dst 'assets') | Out-Null

foreach ($f in @('Onuln.exe', 'Onuln.dll', 'launch.ps1', 'launch.vbs')) {
    Copy-Item (Join-Path $src $f) (Join-Path $dst $f) -Force
}
Copy-Item (Join-Path $src 'assets\widget.ico') (Join-Path $dst 'assets\widget.ico') -Force

# 옛 한글 이름 산출물이 남아 있으면 지운다 (v0.82 이전에 설치한 폴더).
# launch.ps1 은 Onuln 을 먼저 찾으므로 동작에는 지장이 없지만, 남겨 두면
# 다음에 이 폴더를 보는 사람이 어느 것이 도는지 알 수 없다.
foreach ($old in @('오늘은.exe', '오늘은.dll')) {
    $p = Join-Path $dst $old
    if (Test-Path $p) { try { Remove-Item $p -Force } catch { } }
}

# 설정 파일은 이미 있으면 건드리지 않는다 (재설치해도 종목/위치가 남게)
$cfgDst = Join-Path $dst 'config.json'
if (Test-Path $cfgDst) {
    Write-Host '  설정 파일        : 기존 것 그대로 둠' -ForegroundColor Yellow
} else {
    Copy-Item (Join-Path $src 'config.json') $cfgDst -Force
    Write-Host '  설정 파일        : 새로 만듦'
}

# ---------- 3) 차단 해제 ----------
# USB 나 네트워크로 옮긴 파일에는 Mark-of-the-Web 이 붙어 SmartScreen 이 막는다.
Get-ChildItem $dst -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
Write-Host '  차단 해제        : 완료'

# ---------- 4) 바탕화면 바로가기 ----------
$wscript = Join-Path $env:SystemRoot 'System32\wscript.exe'
$vbs     = Join-Path $dst 'launch.vbs'
$lnkPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '오늘은.lnk'
try {
    $sh  = New-Object -ComObject WScript.Shell
    $lnk = $sh.CreateShortcut($lnkPath)
    $lnk.TargetPath       = $wscript
    $lnk.Arguments        = '"' + $vbs + '"'
    $lnk.WorkingDirectory = $dst
    $lnk.IconLocation     = (Join-Path $dst 'assets\widget.ico') + ',0'
    $lnk.Description      = '오늘은 - 환율 / 시세 / 날씨 위젯'
    $lnk.Save()
    Write-Host '  바로가기         : 바탕화면에 만듦'
} catch {
    Write-Host '  바로가기         : 실패 (수동으로 launch.vbs 바로가기를 만드세요)' -ForegroundColor Yellow
}

Write-Host '  ------------------------------------------------'
Write-Host "  설치 위치        : $dst"
Write-Host ''

# ---------- 5) 실행 ----------
Write-Host '  위젯을 띄웁니다...' -ForegroundColor Green
try { Start-Process $wscript -ArgumentList ('"' + $vbs + '"') } catch { }
Start-Sleep -Seconds 3

if (Get-Process 'Onuln' -ErrorAction SilentlyContinue) {
    Write-Host '  실행 확인        : OK (Onuln.exe)' -ForegroundColor Green
} else {
    $fallback = $false
    try {
        $fallback = [bool](Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction Stop |
                    Where-Object { $_.CommandLine -like '*-File*launch.ps1*' })
    } catch { }
    if ($fallback) {
        Write-Host '  실행 확인        : OK (DLL 방식 - exe 가 차단된 PC)' -ForegroundColor Green
    } else {
        Write-Host ''
        Write-Host '  [확인 필요] 위젯이 뜨지 않았습니다.' -ForegroundColor Yellow
        Write-Host '              설치 폴더의 Onuln.exe 를 직접 실행해 보세요:'
        Write-Host "              $dst"
        Write-Host '              그래도 안 되면 같이 들어 있는 설치안내.txt 를 봐 주세요.'
    }
}

Write-Host ''
Write-Host '  종료는 위젯 우클릭 -> 종료.'
Write-Host ''
