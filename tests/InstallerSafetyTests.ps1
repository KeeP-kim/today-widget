param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
. (Join-Path $Root 'installer\safety.ps1')
$work = Join-Path $Root ('_test\installer-safety-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work) | Out-Null
$script:checks = 0
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Reject([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert $failed $Message
}
function Put([string]$Path, [string]$Value) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Value, (New-Object Text.UTF8Encoding($false)))
}

# Mock the complete process boundary. Tests never query or close running processes.
$script:rows = @(); $script:closed = @(); $script:waits = @(); $script:exits = $true; $script:queryFails = $false; $script:queries = 0
function Get-CimInstance {
    param($ClassName, $Filter, $ErrorAction)
    $script:queries++
    if ($script:queryFails) { throw 'Synthetic access denial' }
    return $script:rows
}
function Send-OnulnClose([int]$ProcessId) { $script:closed += $ProcessId }
function Get-Process {
    param($Id, $ErrorAction)
    $process = [pscustomobject]@{ HasExited = $false }
    $process | Add-Member ScriptMethod WaitForExit { param($ms) $script:waits += $ms; return $script:exits }
    $process | Add-Member ScriptMethod Dispose { }
    return $process
}
function ProcessRow([int]$Number, [string]$Name, [string]$Path, [string]$Command) {
    return [pscustomobject]@{ ProcessId = $Number; Name = $Name; ExecutablePath = $Path; CommandLine = $Command }
}
$owned = Join-Path $work '설치 폴더'
$foreign = Join-Path $work 'other'
[IO.Directory]::CreateDirectory($owned) | Out-Null
[IO.Directory]::CreateDirectory($foreign) | Out-Null
$launch = Join-Path $owned 'launch.ps1'
Put $launch '# synthetic'
Put (Join-Path $owned 'Onuln.exe') 'synthetic'
$script:rows = @(
    (ProcessRow 41001 'powershell.exe' 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' ('powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Sta -WindowStyle Hidden -File "' + $launch + '"')),
    (ProcessRow 41002 'powershell.exe' '' ('powershell.exe -File "' + (Join-Path $foreign 'launch.ps1') + '"')),
    (ProcessRow 41003 'powershell.exe' '' ('powershell.exe -File "' + (Join-Path $owned 'relaunch.ps1') + '"')),
    (ProcessRow 41004 'powershell.exe' '' ('powershell.exe -Command "echo -File ' + $launch + '"')),
    (ProcessRow 41005 'powershell.exe' '' ('powershell.exe -c echo -File "' + $launch + '"')),
    (ProcessRow 41006 'Onuln.exe' (Join-Path $owned 'Onuln.exe') ''),
    (ProcessRow 41007 'Onuln.exe' (Join-Path $foreign 'Onuln.exe') ''),
    (ProcessRow 41008 'powershell.exe' '' ('powershell.exe -File "' + (Join-Path $owned 'launch.ps1.backup') + '"'))
)
$selected = @(Get-OnulnOwnedProcesses @($owned) | ForEach-Object { $_.ProcessId })
Assert (($selected -join ',') -eq '41001,41006') 'Ownership selected a foreign process or missed the exact executable/launcher.'
Stop-OnulnInstances @($owned) 35
Assert (($script:closed -join ',') -eq '41001,41006') 'Normal close went to an unrelated process.'
Assert ($script:waits.Count -eq 2 -and $script:waits[0] -eq 35) 'Normal close did not use the bounded wait.'
$script:exits = $false; $script:closed = @()
Reject { Stop-OnulnInstances @($owned) 1 } 'A hung process did not stop the operation.'
Assert ($script:closed.Count -eq 1) 'Close failure continued to other processes.'
$script:exits = $true; $script:queryFails = $true
Reject { Stop-OnulnInstances @($owned) 1 } 'Process ownership inspection failure was ignored.'
$script:queryFails = $false

# Exercise the installer's actual pre-copy ownership helper and copy block in isolated fixtures.
$installerText = [IO.File]::ReadAllText((Join-Path $Root 'installer\install.ps1'))
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($installerText, [ref]$tokens, [ref]$parseErrors)
Assert ($parseErrors.Count -eq 0) 'Installer has a PowerShell parse error.'
$ownership = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Test-OurFolder' }, $true)
. ([scriptblock]::Create($ownership.Extent.Text))
$src = Join-Path $work 'source'; $srcFull = $src
$dst = Join-Path $work 'destination'
foreach ($name in @('Onuln.exe', 'Onuln.dll', 'launch.ps1', 'launch.vbs', 'assets\widget.ico')) { Put (Join-Path $src $name) 'synthetic package' }
Put (Join-Path $src 'config.json') '{"version":"test","apps":[{"file":"sample.lnk","label":"synthetic"}]}'
Put (Join-Path $src '앱저장\즐겨찾기\sample.lnk') 'synthetic shortcut'
Put (Join-Path $src '앱저장\즐겨찾기\sample.lnk.png') 'synthetic custom icon'
Put (Join-Path $src 'prediction-history\sample.xml') 'synthetic history'
Put (Join-Path $src 'dollar-analysis.json') '{"synthetic":true}'
$otherApp = Join-Path $work 'other-app-destination'
Put (Join-Path $otherApp 'launch.ps1') '# unrelated launcher'
Assert (-not (Test-OurFolder $otherApp)) 'A single foreign launcher established ownership.'
$script:rows = @((ProcessRow 41009 'powershell.exe' '' ('powershell.exe -File "' + (Join-Path $otherApp 'launch.ps1') + '"')))
$script:queries = 0; $script:closed = @()
Reject { Prepare-OnulnInstall $src $otherApp $false } 'An unowned destination launcher collision did not stop installation.'
Assert ($script:queries -eq 0 -and $script:closed.Count -eq 0) 'Processes were inspected or closed before destination collision preflight.'
Assert ([IO.File]::ReadAllText((Join-Path $otherApp 'launch.ps1')) -eq '# unrelated launcher') 'The foreign launcher changed on collision.'
Assert (-not (Test-Path -LiteralPath (Join-Path $otherApp '앱저장'))) 'User data was copied before destination collision preflight.'
Put (Join-Path $otherApp 'config.json') '{"foreign":true}'
Assert (-not (Test-OurFolder $otherApp)) 'A generic launcher and config established ownership.'
foreach ($name in @('Onuln.exe', 'Onuln.dll', 'launch.vbs', 'assets\widget.ico', 'config.json')) {
    $collisionRoot = Join-Path $work ('collision-' + [guid]::NewGuid().ToString('N'))
    Put (Join-Path $collisionRoot $name) 'foreign package name'
    Reject { Prepare-OnulnInstall $src $collisionRoot $false } ('An unowned package file collision was accepted: ' + $name)
    Assert ([IO.File]::ReadAllText((Join-Path $collisionRoot $name)) -eq 'foreign package name') ('A conflicting package file was changed: ' + $name)
}
Put (Join-Path $dst '오늘은.exe') 'unrelated legacy filename'
$destinationWasOurs = Test-OurFolder $dst
Assert (-not $destinationWasOurs) 'An unrelated legacy filename established installation ownership.'
$script:rows = @((ProcessRow 41010 '오늘은.exe' (Join-Path $dst '오늘은.exe') ''))
$script:closed = @()
Prepare-OnulnInstall $src $dst $destinationWasOurs
Assert ($script:closed.Count -eq 0) 'An unowned destination was included in the process close scope.'
$inPlace = $false
$start = $installerText.IndexOf('# ---------- 3)'); $end = $installerText.IndexOf('# ---------- 4)')
Assert ($start -ge 0 -and $end -gt $start) 'Cannot locate the bounded installer copy phase.'
& ([scriptblock]::Create($installerText.Substring($start, $end - $start)))
Assert (Test-Path -LiteralPath (Join-Path $dst '오늘은.exe')) 'Copy-created ownership markers caused deletion of a foreign legacy filename.'
foreach ($name in @('앱저장\즐겨찾기\sample.lnk', '앱저장\즐겨찾기\sample.lnk.png', 'prediction-history\sample.xml', 'dollar-analysis.json', 'config.json')) {
    Assert ([IO.File]::ReadAllText((Join-Path $dst $name)) -eq [IO.File]::ReadAllText((Join-Path $src $name))) ('User state was not preserved: ' + $name)
}
Remove-OnulnLegacyFiles $dst $true
Assert (Test-Path -LiteralPath (Join-Path $dst '오늘은.exe')) 'Even a known folder allowed deletion of an unowned legacy file.'
# A real version resource establishes the known installation case without running the fixture.
$metadataSource = Join-Path $work 'OwnedFixture.cs'
Put $metadataSource '[assembly: System.Reflection.AssemblyProduct("오늘은")] public sealed class OwnedFixture { }'
$knownDestination = Join-Path $work 'known-installation'
[IO.Directory]::CreateDirectory($knownDestination) | Out-Null
$metadataDll = Join-Path $knownDestination 'Onuln.dll'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:library /codepage:65001 ('/out:' + $metadataDll) $metadataSource
Assert ($LASTEXITCODE -eq 0 -and (Test-OurFolder $knownDestination)) 'An actual Onuln product resource was not recognized.'
Put (Join-Path $knownDestination 'launch.ps1') '# existing app launcher'
Put (Join-Path $knownDestination 'config.json') '{"keepExisting":true}'
$script:rows = @((ProcessRow 41011 'powershell.exe' '' ('powershell.exe -File "' + (Join-Path $knownDestination 'launch.ps1') + '"')))
$script:closed = @()
Prepare-OnulnInstall $src $knownDestination $true
Assert (($script:closed -join ',') -eq '41011') 'A verified destination did not receive a normal close request.'
Assert ([IO.File]::ReadAllText((Join-Path $knownDestination 'config.json')) -eq '{"keepExisting":true}') 'Verified destination config was overwritten.'
[IO.File]::Copy($metadataDll, (Join-Path $knownDestination '오늘은.dll'), $false)
Remove-OnulnLegacyFiles $knownDestination $true
Assert (-not (Test-Path -LiteralPath (Join-Path $knownDestination '오늘은.dll'))) 'A verified legacy binary was not cleaned up.'
# Identical state is idempotent. Different state aborts before adding any planned files.
Copy-OnulnUserData $src $dst
Put (Join-Path $src 'prediction-history\new.xml') 'new source record'
Put (Join-Path $dst 'prediction-history\sample.xml') 'destination record'
Reject { Copy-OnulnUserData $src $dst } 'Conflicting history was replaced or silently merged.'
Assert ([IO.File]::ReadAllText((Join-Path $dst 'prediction-history\sample.xml')) -eq 'destination record') 'Destination history changed on conflict.'
Assert (-not (Test-Path -LiteralPath (Join-Path $dst 'prediction-history\new.xml'))) 'Data copied before conflict preflight finished.'
Put (Join-Path $dst 'prediction-history\sample.xml') 'synthetic history'
Put (Join-Path $dst '앱저장\즐겨찾기\sample.lnk') 'destination shortcut'
Reject { Copy-OnulnUserData $src $dst } 'Conflicting shortcut was replaced.'
Assert ([IO.File]::ReadAllText((Join-Path $dst '앱저장\즐겨찾기\sample.lnk')) -eq 'destination shortcut') 'Destination shortcut changed on conflict.'
Put (Join-Path $dst 'config.json') '{"keepExisting":true}'
& ([scriptblock]::Create($installerText.Substring($start, $end - $start)))
Assert ([IO.File]::ReadAllText((Join-Path $dst 'config.json')) -eq '{"keepExisting":true}') 'An existing destination config was replaced.'
Reject { Assert-OnulnContainedPath $dst (Join-Path $dst '..\outside.txt') } 'An escaping destination path was accepted.'
Reject { Get-OnulnFullPath 'D:relative\launch.ps1' } 'A drive-relative launcher path was resolved against the installer working directory.'
Reject { Get-OnulnFullPath '\relative\launch.ps1' } 'A current-drive-relative launcher path was accepted.'
Reject { Copy-OnulnUserData $src (Join-Path $src '앱저장\nested') } 'Recursive copying into the source data tree was allowed.'
$junction = Join-Path $work 'junction-source'
[IO.Directory]::CreateDirectory($junction) | Out-Null
New-Item -ItemType Junction -Path (Join-Path $junction '앱저장') -Target $foreign | Out-Null
Reject { Copy-OnulnUserData $junction (Join-Path $work 'junction-target') } 'A source junction was traversed.'
$targetJunction = Join-Path $work 'destination-junction'
[IO.Directory]::CreateDirectory($targetJunction) | Out-Null
New-Item -ItemType Junction -Path (Join-Path $targetJunction '앱저장') -Target $foreign | Out-Null
Reject { Copy-OnulnUserData $src $targetJunction } 'A destination junction was followed.'
Write-Output ('PASS: ' + $script:checks + ' installer/build safety checks; fixture=' + $work)
