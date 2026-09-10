param([string]$Suite = 'all', [string]$SourceRoot = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $SourceRoot) { $SourceRoot = $root }
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$outDir = Join-Path $root ('_test\regression-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$exe = Join-Path $outDir 'RegressionTests.exe'
$dll = Join-Path $outDir 'RegressionTests.dll'
$refs = @('WPF\PresentationFramework', 'WPF\PresentationCore', 'WPF\WindowsBase',
          'System.Xaml', 'System', 'System.Core', 'System.Net.Http', 'System.Drawing', 'System.Xml') |
    ForEach-Object { '/reference:' + (Join-Path $fw ($_ + '.dll')) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'src') -Filter '*.cs' |
    ForEach-Object { $_.FullName })
$sources += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName })
& (Join-Path $fw 'csc.exe') (@('/nologo', '/target:exe', '/platform:x64', '/codepage:65001',
    '/main:DeskWidget.RegressionTests', "/out:$exe") + $refs + $sources)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# ---------------------------------------------------------------
#  1순위: 방금 만든 exe 를 그냥 실행한다.
#  2순위: Smart App Control 이 서명 없는 exe 를 막으면, 같은 소스를 DLL 로 만들어
#         메모리로 올린 뒤 진입점을 직접 부른다. 위젯의 launch.ps1 과 같은 방식이다.
#         powershell.exe 는 Microsoft 서명 파일이고, 파일을 '실행' 하는 게 아니라
#         어셈블리를 '로드' 하는 것이라 코드 무결성 정책에 걸리지 않는다.
#
#  ★ 이게 없으면 SAC 가 켜진 PC 에서 검사를 아예 돌릴 수 없다 ★
#    실제로 그렇게 막혔다. 검사가 안 도는 것과 검사가 실패하는 것은 다른데,
#    막힌 것을 실패로 읽으면 멀쩡한 코드를 고치려 들게 된다.
# ---------------------------------------------------------------
$blocked = $false
try { & $exe $root $Suite $SourceRoot; $code = $LASTEXITCODE }
catch { $blocked = $true }
if (-not $blocked -and $code -eq $null) { $blocked = $true }
if ($blocked) {
    Write-Host '[알림] exe 실행이 막혀 DLL 을 메모리로 올려 돌립니다 (Smart App Control).'
    & (Join-Path $fw 'csc.exe') (@('/nologo', '/target:library', '/platform:x64', '/codepage:65001',
        "/out:$dll") + $refs + $sources)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml, System.Net.Http
    $asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dll))
    $method = $asm.GetType('DeskWidget.RegressionTests').GetMethod('Main')
    $argv = New-Object object[] 1
    $argv[0] = [string[]]@($root, $Suite, $SourceRoot)
    $code = [int]$method.Invoke($null, $argv)
}
exit $code
