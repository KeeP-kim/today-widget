# ---------------------------------------------------------------
#  "오늘은" 위젯 설치 스크립트
#
#  옮긴 폴더에서 install.cmd 를 실행하면 이 파일이 돈다.
#  하는 일: 둘 곳 정하기 -> (필요하면) 복사 -> 차단 해제 -> 바로가기 -> 실행
#
#  ★ 기본은 '지금 이 폴더' 다 ★
#    예전에는 %LOCALAPPDATA%\오늘은 으로 말없이 복사했다. 숨은 폴더라
#    나중에 어디 있는지 찾기 어렵고, 지우려면 경로를 알아야 한다.
#    푼 자리에 그대로 두면 폴더째 옮기고 지우는 것이 그냥 된다.
#
#  ★ 다만 '지금 이 폴더' 가 남의 폴더일 수 있다 ★
#    zip 을 'Extract Here' 로 풀면 내려받기·바탕화면 같은 곳에 흩뿌려진다.
#    그런 자리는 우리 것이 아니므로 그 아래 '오늘은' 폴더를 만들어 들어간다.
#    이 구분을 놓치면 나중에 "이 폴더를 지우세요" 가 "내려받기를 통째로 지우세요" 가 된다.
#
#  ★ 남의 파일은 절대 건드리지 않는다 ★
#    차단 해제도, 청소도, 프로세스 종료도 전부 '우리가 놓은 것' 으로만 한정한다.
#    이 스크립트는 남의 PC 에서 돈다. 되돌릴 수 없는 일을 넓게 하면 안 된다.
# ---------------------------------------------------------------
$ErrorActionPreference = 'Stop'

$src = Split-Path -Parent $MyInvocation.MyCommand.Definition

# 이 설치가 놓는 것 전부. 차단 해제도 청소도 이 목록 안에서만 한다.
$OurFiles = @('Onuln.exe', 'Onuln.dll', 'launch.ps1', 'launch.vbs',
              'config.json', 'assets\widget.ico')

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

# ---------- 도우미 ----------

# ★ 루트의 후행 백슬래시는 자르면 안 된다 ★
#   'D:\' 를 'D:' 로 만들면 뜻이 달라진다. 'D:' 는 그 드라이브의 '현재 폴더' 라,
#   Join-Path 는 루트로 풀고 Get-ChildItem 은 엉뚱한 폴더로 푼다.
#   그 어긋남 하나로 파일이 드라이브 루트에 쏟아진 적이 있다(실측).
function Get-Full([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return $null }
    try { $f = [System.IO.Path]::GetFullPath($p) } catch { return $null }
    if ($f.Length -gt 3) { $f = $f.TrimEnd('\') }
    return $f
}

function Test-IsRoot([string]$p) {
    try { return ($p -eq ([System.IO.Path]::GetPathRoot($p))) } catch { return $false }
}

# 내려받기·바탕화면·문서처럼 **남과 나눠 쓰는 자리**인가.
# 여기에 그대로 깔면 우리 파일이 남의 파일과 섞이고, 지우라는 안내가 위험해진다.
function Test-SharedFolder([string]$p) {
    if (Test-IsRoot $p) { return $true }
    $names = @('Desktop', 'MyDocuments', 'Personal', 'MyPictures', 'MyMusic', 'MyVideos',
               'UserProfile', 'CommonDesktopDirectory', 'CommonDocuments')
    foreach ($n in $names) {
        try { $f = Get-Full ([Environment]::GetFolderPath($n)) } catch { $f = $null }
        if ($f -and $f -eq $p) { return $true }
    }
    foreach ($extra in @("$env:USERPROFILE\Downloads", $env:OneDrive, $env:OneDriveConsumer,
                         $env:OneDriveCommercial, $env:USERPROFILE, $env:PUBLIC)) {
        if ([string]::IsNullOrWhiteSpace($extra)) { continue }
        $f = Get-Full $extra
        if ($f -and $f -eq $p) { return $true }
    }
    return $false
}

# 압축을 풀지 않고 zip 안에서 바로 실행하면 여기가 임시 폴더다.
# 그대로 두면 Windows 가 언젠가 지운다.
function Test-TempPath([string]$p) {
    foreach ($t in @($env:TEMP, $env:TMP, (Join-Path $env:LOCALAPPDATA 'Temp'))) {
        if ([string]::IsNullOrWhiteSpace($t)) { continue }
        $tf = Get-Full $t
        if (-not $tf) { continue }
        if ($p -eq $tf) { return $true }
        if ($p.StartsWith($tf + '\', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    if ($p -match '\\INetCache\\') { return $true }
    return $false
}

# 권한만 봐서는 모른다. 한 번 써 보는 것이 확실하다.
function Test-Writable([string]$p) {
    try {
        if (-not (Test-Path -LiteralPath $p)) {
            New-Item -ItemType Directory -Force -Path $p -ErrorAction Stop | Out-Null
        }
        $probe = Join-Path $p ('.write-test-' + [guid]::NewGuid().ToString('N') + '.tmp')
        [System.IO.File]::WriteAllText($probe, 'x')
        [System.IO.File]::Delete($probe)
        return $true
    } catch { return $false }
}

# 예전에 여기 깔았던 흔적인가. 청소는 이것이 참일 때만 한다 -
# 이름만 보고 지우면 남이 같은 이름으로 둔 파일을 없앤다.
function Test-OurFolder([string]$p) {
    if (-not (Test-Path -LiteralPath $p)) { return $false }
    $marks = 0
    foreach ($f in @('launch.vbs', 'launch.ps1', 'Onuln.exe', '오늘은.exe', 'config.json')) {
        if (Test-Path -LiteralPath (Join-Path $p $f)) { $marks++ }
    }
    return ($marks -ge 2)
}

# 이 폴더에 우리 것 말고 다른 파일이 있나. 마지막 '지우는 법' 안내가 이것으로 갈린다.
function Test-Dedicated([string]$p) {
    try {
        $ours = @{}
        foreach ($f in $OurFiles) { $ours[[System.IO.Path]::GetFileName($f)] = $true }
        $ours['설치안내.txt'] = $true; $ours['install.cmd'] = $true; $ours['install.ps1'] = $true
        foreach ($item in (Get-ChildItem -LiteralPath $p -Force -ErrorAction SilentlyContinue)) {
            if ($item.PSIsContainer) {
                if ($item.Name -ne 'assets' -and $item.Name -ne '앱저장') { return $false }
            } elseif (-not $ours.ContainsKey($item.Name)) { return $false }
        }
        return $true
    } catch { return $false }
}

# ---------- 1) 둘 곳을 정한다 ----------

$srcFull = Get-Full $src
$inTemp  = Test-TempPath $srcFull
$shared  = Test-SharedFolder $srcFull

if ($inTemp) {
    $default = Join-Path ([Environment]::GetFolderPath('MyDocuments')) '오늘은'
    Write-Host ''
    Write-Host '  [알림] 압축을 풀지 않고 실행하신 것 같습니다.' -ForegroundColor Yellow
    Write-Host '         지금 자리는 임시 폴더라 Windows 가 언젠가 지웁니다.'
    Write-Host '         zip 을 원하는 폴더에 풀고 다시 실행하시는 편이 깔끔합니다.'
} elseif ($shared) {
    $default = Join-Path $srcFull '오늘은'
    Write-Host ''
    Write-Host '  [알림] 지금 자리는 여러 가지가 섞여 있는 폴더입니다.' -ForegroundColor Yellow
    Write-Host "         $srcFull"
    Write-Host '         여기에 그대로 두면 나중에 무엇을 지워야 할지 알기 어려워서,'
    Write-Host '         그 아래 전용 폴더를 만들어 넣겠습니다.'
} else {
    $default = $srcFull
}

$willCopy = ($default -ne $srcFull)

Write-Host ''
Write-Host '  둘 곳을 정하세요.' -ForegroundColor Cyan
if ($willCopy) {
    Write-Host "    여기로 옮기려면 그냥 Enter (복사합니다) :"
} else {
    Write-Host "    여기에 그대로 두려면 그냥 Enter (복사하지 않습니다) :"
}
Write-Host "      $default" -ForegroundColor White
Write-Host '    다른 곳에 두려면 경로를 적으세요 (예: D:\Tools\오늘은)'

$ans = ''
try { $ans = Read-Host '  >' } catch { $ans = '' }   # 대화형이 아니면 기본값으로 간다

if ([string]::IsNullOrWhiteSpace($ans)) {
    $dst = $default
} else {
    $typed = $ans.Trim().Trim('"').Trim("'")
    try { $typed = [Environment]::ExpandEnvironmentVariables($typed) } catch { }
    $dst = Get-Full $typed
    if (-not $dst) {
        Write-Host ''
        Write-Host '  [중단] 경로를 알아볼 수 없습니다.' -ForegroundColor Red
        Write-Host ''
        return
    }
    # 드라이브 루트나 남과 나눠 쓰는 폴더를 적었으면 그 아래 전용 폴더로 내린다.
    # 거기에 그대로 깔면 우리 파일이 남의 파일과 섞이고, 지우라는 안내가 위험해진다.
    if ((Test-SharedFolder $dst) -and (-not (Test-OurFolder $dst))) {
        $dst = Join-Path $dst '오늘은'
        Write-Host ''
        Write-Host '  [알림] 적어 주신 곳은 여러 가지가 섞여 있는 폴더라,' -ForegroundColor Yellow
        Write-Host "         그 아래 전용 폴더에 넣습니다 :  $dst"
    }
}

if (-not (Test-Writable $dst)) {
    Write-Host ''
    Write-Host '  [중단] 이 폴더에는 쓸 수가 없습니다.' -ForegroundColor Red
    Write-Host "         $dst"
    Write-Host '         Program Files 밑은 관리자 권한이 필요합니다.'
    Write-Host '         문서나 D:\Tools 처럼 쓰기가 되는 곳을 고르세요.'
    Write-Host ''
    return
}

$inPlace = ($srcFull -eq $dst)

if ($inTemp -and $inPlace) {
    Write-Host ''
    Write-Host '  [중단] 임시 폴더에 그대로 둘 수는 없습니다.' -ForegroundColor Red
    Write-Host '         Windows 가 지우면 위젯도 같이 사라집니다.'
    Write-Host '         zip 을 원하는 폴더에 풀고 다시 실행해 주세요.'
    Write-Host ''
    return
}

# ---------- 2) 우리 위젯이 떠 있으면 내린다 ----------
# 그대로 두면 파일이 잠겨서 덮어쓰지 못한다.
#
# ★ 이름만 보고 죽이지 않는다 ★
#   예전에는 명령줄에 'launch.ps1' 이 들어간 powershell 을 전부 죽였다.
#   남의 launch.ps1 이나 relaunch.ps1 까지 말없이 잡는다 - 배포 스크립트가
#   반쯤 돌다 끊길 수 있다. 우리 두 폴더의 launch.ps1 만 고른다.
$mine = @()
foreach ($d in @($srcFull, $dst)) { $mine += (Join-Path $d 'launch.ps1') }

Get-Process 'Onuln', '오늘은' -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 200 } catch { }
    try { $_.Kill() } catch { }
}
try {
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction Stop |
        Where-Object {
            $c = $_.CommandLine
            if ($_.ProcessId -eq $PID -or [string]::IsNullOrEmpty($c)) { return $false }
            foreach ($m in $mine) { if ($c -like ('*' + $m + '*')) { return $true } }
            return $false
        } |
        ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch { } }
} catch { }
Start-Sleep -Milliseconds 400

# ---------- 3) 복사 (그 자리에 두면 건너뛴다) ----------
$existed = Test-Path -LiteralPath (Join-Path $dst 'Onuln.exe')

if ($inPlace) {
    Write-Host '  파일             : 이 폴더에 그대로 둡니다 (복사 안 함)'
} else {
    New-Item -ItemType Directory -Force -Path $dst                      | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $dst 'assets') | Out-Null

    foreach ($f in @('Onuln.exe', 'Onuln.dll', 'launch.ps1', 'launch.vbs')) {
        Copy-Item -LiteralPath (Join-Path $src $f) -Destination (Join-Path $dst $f) -Force
    }
    Copy-Item -LiteralPath (Join-Path $src 'assets\widget.ico') `
              -Destination (Join-Path $dst 'assets\widget.ico') -Force
    Write-Host "  파일             : 복사 완료 -> $dst"

    # 옛 한글 이름 산출물 청소. **예전 설치 폴더가 확실할 때만** 한다 -
    # 이름만 보고 지우면 남이 같은 이름으로 둔 파일을 없앤다. 지운 것은 말해 준다.
    if (Test-OurFolder $dst) {
        foreach ($old in @('오늘은.exe', '오늘은.dll')) {
            $p = Join-Path $dst $old
            if (Test-Path -LiteralPath $p) {
                try { Remove-Item -LiteralPath $p -Force; Write-Host "  옛 파일 정리     : $old 지움" }
                catch { }
            }
        }
    }
}

# 설정 파일은 이미 있으면 건드리지 않는다 (다시 깔아도 종목/위치가 남게)
$cfgDst = Join-Path $dst 'config.json'
$cfgSrc = Join-Path $src 'config.json'
if ($inPlace) {
    if (Test-Path -LiteralPath $cfgDst) {
        Write-Host '  설정 파일        : 이 폴더의 것을 씁니다'
    } else {
        Write-Host '  설정 파일        : 없음 - 위젯이 첫 실행 때 만듭니다'
    }
} elseif (Test-Path -LiteralPath $cfgDst) {
    if ($existed) {
        Write-Host '  설정 파일        : 이미 있던 설정을 그대로 둡니다 (종목·위치 유지)' -ForegroundColor Yellow
    } else {
        Write-Host '  설정 파일        : 이미 있던 것을 그대로 둡니다'
    }
} elseif (Test-Path -LiteralPath $cfgSrc) {
    Copy-Item -LiteralPath $cfgSrc -Destination $cfgDst -Force
    Write-Host '  설정 파일        : 새로 만듦'
} else {
    Write-Host '  설정 파일        : 없음 - 위젯이 첫 실행 때 만듭니다'
}

# ---------- 4) 차단 해제 ----------
# 인터넷이나 USB 로 옮긴 파일에는 Mark-of-the-Web 이 붙어 SmartScreen 이 막는다.
#
# ★ 우리가 놓은 것만 푼다 ★
#   예전에는 `Get-ChildItem $dst -Recurse` 로 폴더를 통째로 훑었다. 두 가지가 나빴다 -
#   (1) 목적지가 내려받기 폴더면 거기 있던 남의 setup.exe·xlsm 에서도 표식이 벗겨진다.
#       그건 되돌릴 수 없고, 다음에 그 파일을 열 때 경고가 사라져 있다.
#   (2) 읽을 수 없는 하위 폴더 하나에 스크립트가 통째로 죽는다. 하필 위젯을 이미
#       내린 뒤라 "설치를 눌렀더니 위젯이 사라졌다" 가 된다.
foreach ($f in $OurFiles) {
    $p = Join-Path $dst $f
    if (Test-Path -LiteralPath $p) {
        try { Unblock-File -LiteralPath $p -ErrorAction SilentlyContinue } catch { }
    }
}
Write-Host '  차단 해제        : 완료 (이 위젯 파일만)'

# ---------- 5) 바탕화면 바로가기 ----------
$wscript = Join-Path $env:SystemRoot 'System32\wscript.exe'
$vbs     = Join-Path $dst 'launch.vbs'
$ico     = Join-Path $dst 'assets\widget.ico'
$lnkPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '오늘은.lnk'
try {
    $sh  = New-Object -ComObject WScript.Shell
    $lnk = $sh.CreateShortcut($lnkPath)
    $lnk.TargetPath       = $wscript
    $lnk.Arguments        = '"' + $vbs + '"'
    $lnk.WorkingDirectory = $dst
    if (Test-Path -LiteralPath $ico) { $lnk.IconLocation = $ico + ',0' }
    $lnk.Description      = '오늘은 - 환율 / 시세 / 날씨 위젯'
    $lnk.Save()
    Write-Host '  바로가기         : 바탕화면에 만듦'
} catch {
    Write-Host '  바로가기         : 실패 (수동으로 launch.vbs 바로가기를 만드세요)' -ForegroundColor Yellow
}

$dedicated = Test-Dedicated $dst

Write-Host '  ------------------------------------------------'
Write-Host "  둔 곳            : $dst"
Write-Host ''

# ---------- 6) 실행 ----------
Write-Host '  위젯을 띄웁니다...' -ForegroundColor Green
try { Start-Process $wscript -ArgumentList ('"' + $vbs + '"') -WorkingDirectory $dst } catch { }
Start-Sleep -Seconds 3

if (Get-Process 'Onuln' -ErrorAction SilentlyContinue) {
    Write-Host '  실행 확인        : OK (Onuln.exe)' -ForegroundColor Green
} else {
    $fallback = $false
    try {
        $fallback = [bool](Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction Stop |
                    Where-Object { $_.CommandLine -like ('*' + (Join-Path $dst 'launch.ps1') + '*') })
    } catch { }
    if ($fallback) {
        Write-Host '  실행 확인        : OK (DLL 방식 - exe 가 차단된 PC)' -ForegroundColor Green
    } else {
        Write-Host ''
        Write-Host '  [확인 필요] 위젯이 뜨지 않았습니다.' -ForegroundColor Yellow
        Write-Host '              둔 폴더의 Onuln.exe 를 직접 실행해 보세요:'
        Write-Host "              $dst"
        Write-Host '              그래도 안 되면 같이 들어 있는 설치안내.txt 를 봐 주세요.'
    }
}

Write-Host ''
Write-Host '  종료는 위젯 우클릭 -> 종료.'
if ($dedicated) {
    Write-Host '  지우려면 위젯을 끄고 이 폴더와 바탕화면 바로가기를 지우면 됩니다:'
    Write-Host "    $dst"
} else {
    # 이 폴더에는 남의 파일도 있다. '폴더를 지우라' 고 하면 안 된다.
    Write-Host '  지우려면 위젯을 끄고 아래 파일과 바탕화면 바로가기만 지우면 됩니다:'
    foreach ($f in $OurFiles) {
        $p = Join-Path $dst $f
        if (Test-Path -LiteralPath $p) { Write-Host "    $p" }
    }
}
Write-Host ''
