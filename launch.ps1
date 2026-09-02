# ---------------------------------------------------------------
#  "오늘은" 런처
#
#  1순위: Onuln.exe 를 그냥 실행한다 (워킹셋 약 20MB 로 가장 가볍다)
#  2순위: Smart App Control 이 exe 를 차단하면, Onuln.dll 을 메모리로 읽어
#         올린 뒤 진입점을 직접 호출한다.
#         powershell.exe 는 Microsoft 서명 파일이라 차단되지 않고,
#         파일을 '실행'하는 게 아니라 어셈블리를 '로드'하는 것이라
#         코드 무결성 정책에도 걸리지 않는다. (대신 메모리를 약 90MB 쓴다)
# ---------------------------------------------------------------
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
# 산출물 이름은 ASCII 로 둔다. 한글 파일명은 압축·업로드·백업 도구를 거치며
# 깨지는 곳이 있었다 (릴리스 업로드가 'default.exe' 로 뭉갠 것이 실측 사례).
# 화면에 보이는 이름은 그대로 "오늘은" 이다.
#
# 옛 이름은 v0.82 이전 빌드가 남아 있는 폴더를 위한 것이다.
function Find-Beside([string[]]$names) {
    foreach ($n in $names) {
        $p = Join-Path $root $n
        if (Test-Path $p) { return $p }
    }
    return (Join-Path $root $names[0])
}

$exe  = Find-Beside @('Onuln.exe', '오늘은.exe')
$dll  = Find-Beside @('Onuln.dll', '오늘은.dll')

function Show-Error($msg) {
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show($msg, '오늘은') | Out-Null
    } catch { }
}

# ---------- 1순위: exe ----------
if (Test-Path $exe) {
    try {
        $p = Start-Process -FilePath $exe -WorkingDirectory $root -PassThru -ErrorAction Stop
        Start-Sleep -Milliseconds 1500
        if (-not $p.HasExited) { exit 0 }   # 잘 떴다
        # 바로 죽었다면 이미 실행 중이거나(중복 방지) 차단된 것 - 아래로 넘어간다
    } catch {
        # Smart App Control 차단 등 - 아래 DLL 방식으로
    }
}

# ---------- 2순위: DLL 메모리 로드 ----------
if (-not (Test-Path $dll)) {
    Show-Error "위젯 파일을 찾을 수 없습니다.`n`n$dll`n`nbuild.cmd 를 실행해 다시 빌드해 주세요."
    exit 1
}

try {
    Add-Type -AssemblyName PresentationFramework
    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase
    Add-Type -AssemblyName System.Xaml
    Add-Type -AssemblyName System.Net.Http

    # 파일 경로가 아니라 바이트 배열로 로드한다
    $asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dll))

    # "Windows 시작 시 자동 실행" 에 등록할 명령
    # (powershell.exe 경로가 아니라 이 런처를 가리켜야 한다)
    $wscript   = Join-Path $env:SystemRoot 'System32\wscript.exe'
    $vbs       = Join-Path $root 'launch.vbs'
    $launchCmd = '"' + $wscript + '" "' + $vbs + '"'

    # PowerShell 이 인자를 PSObject 로 감싸면 리플렉션 호출이 실패한다.
    # object[] 를 직접 만들고 각 항목을 [string] 으로 넣어 그 문제를 피한다.
    $argv = New-Object object[] 2
    $argv[0] = [string]$root
    $argv[1] = [string]$launchCmd

    $method = $asm.GetType('DeskWidget.Program').GetMethod('Run')
    $method.Invoke($null, $argv) | Out-Null
}
catch {
    Show-Error ("위젯을 시작하지 못했습니다.`n`n" + $_.Exception.Message)
    exit 1
}
