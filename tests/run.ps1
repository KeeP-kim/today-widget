param([string]$Suite = 'all', [string]$SourceRoot = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $SourceRoot) { $SourceRoot = $root }
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$outDir = Join-Path $root ('_test\regression-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$exe = Join-Path $outDir 'RegressionTests.exe'
$refs = @('WPF\PresentationFramework', 'WPF\PresentationCore', 'WPF\WindowsBase',
          'System.Xaml', 'System', 'System.Core', 'System.Net.Http', 'System.Drawing') |
    ForEach-Object { '/reference:' + (Join-Path $fw ($_ + '.dll')) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'src') -Filter '*.cs' |
    ForEach-Object { $_.FullName })
$sources += Join-Path $PSScriptRoot 'RegressionTests.cs'
& (Join-Path $fw 'csc.exe') (@('/nologo', '/target:exe', '/platform:x64', '/codepage:65001',
    '/main:DeskWidget.RegressionTests', "/out:$exe") + $refs + $sources)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $exe $root $Suite
exit $LASTEXITCODE
