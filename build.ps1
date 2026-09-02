# ---------------------------------------------------------------
#  "오늘은" 위젯 빌드 스크립트
#  Windows에 기본 포함된 C# 컴파일러만 사용한다 (별도 설치 불필요)
# ---------------------------------------------------------------
param(
    # CI 에서는 돌고 있는 위젯이 없다. 프로세스를 뒤지는 단계를 건너뛴다.
    [switch]$CI
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$FW   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc  = Join-Path $FW 'csc.exe'
$exe  = Join-Path $root 'Onuln.exe'
$dll  = Join-Path $root 'Onuln.dll'

if (-not (Test-Path $csc)) {
    Write-Host "[오류] C# 컴파일러를 찾을 수 없습니다: $csc" -ForegroundColor Red
    Write-Host "       .NET Framework 4.x 가 필요합니다."
    exit 1
}

# 실행 중이면 먼저 내린다.
# exe 뿐 아니라 DLL 폴백으로 도는 런처(powershell.exe)까지 내려야 한다.
# 그러지 않으면 뮤텍스 때문에 새 버전이 뜨자마자 종료되고 구버전이 계속 돈다.
if (-not $CI) {
Get-Process 'Onuln' -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 200 } catch { }
    try { $_.Kill() } catch { }
}

$mePid = $PID
try {
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction Stop |
        Where-Object { $_.ProcessId -ne $mePid -and $_.CommandLine -like '*-File*launch.ps1*' } |
        ForEach-Object {
            Write-Host ("  이전 런처 종료: PID " + $_.ProcessId)
            try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch { }
        }
} catch { }

Start-Sleep -Milliseconds 400
}

$sources = @(
    'AssemblyInfo', 'Program', 'Config', 'Json', 'Net', 'Sources', 'Icons', 'Theme', 'Dock',
    'Apps', 'PanelWindow', 'AppPickWindow', 'AboutWindow', 'SearchWindow', 'WidgetWindow'
) | ForEach-Object { Join-Path $root "src\$_.cs" }

$refs = @(
    "/reference:$FW\WPF\PresentationFramework.dll"
    "/reference:$FW\WPF\PresentationCore.dll"
    "/reference:$FW\WPF\WindowsBase.dll"
    "/reference:$FW\System.Xaml.dll"
    "/reference:$FW\System.dll"
    "/reference:$FW\System.Core.dll"
    "/reference:$FW\System.Net.Http.dll"
    "/reference:$FW\System.Drawing.dll"
)
$icon = Join-Path $root 'assets\widget.ico'

Write-Host "빌드 중..."

# 1) DLL - PowerShell 런처(launch.vbs)가 메모리로 올려 실행한다.
#    Smart App Control 이 켜진 PC 에서도 차단되지 않는 경로다.
& $csc (@('/nologo', '/target:library', '/platform:x64', '/optimize+', '/codepage:65001', "/out:$dll") + $refs + $sources)
if (-not (Test-Path $dll)) { Write-Host "[실패] DLL 빌드 오류" -ForegroundColor Red; exit 1 }

# 2) EXE - Smart App Control 이 꺼진 PC 에서 직접 실행할 때 쓴다.
& $csc (@('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
          "/out:$exe", "/win32icon:$icon") + $refs + $sources)

Write-Host ("[완료] Onuln.dll  ({0:N0} bytes)" -f (Get-Item $dll).Length) -ForegroundColor Green
if (Test-Path $exe) {
    Write-Host ("[완료] Onuln.exe  ({0:N0} bytes)" -f (Get-Item $exe).Length) -ForegroundColor Green
}
exit 0
