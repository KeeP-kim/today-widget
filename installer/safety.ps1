# Shared build/installer guards. No work runs until a function is called.
function Get-OnulnFullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\[^\\]+(?:\\|$))') {
        throw 'An absolute installation path is required.'
    }
    $full = [IO.Path]::GetFullPath($Path)
    if (Test-Path -LiteralPath $full) { $full = (Get-Item -LiteralPath $full -Force -ErrorAction Stop).FullName }
    if ($full.Length -gt 3) { $full = $full.TrimEnd('\') }
    return $full
}

function Initialize-OnulnNative {
    if ('OnulnInstallerNative' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class OnulnInstallerNative {
    [DllImport("shell32.dll", SetLastError = true)] static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string command, out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr value);
    delegate bool EnumProc(IntPtr window, IntPtr arg);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr arg);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    public static string[] Arguments(string command) {
        int count; IntPtr block = CommandLineToArgvW(command, out count);
        if (block == IntPtr.Zero) throw new InvalidOperationException("Cannot parse the launch command.");
        try {
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(block, i * IntPtr.Size));
            return result;
        } finally { LocalFree(block); }
    }
    public static int CloseWindows(int processId) {
        int sent = 0;
        EnumWindows(delegate(IntPtr window, IntPtr arg) {
            uint owner; GetWindowThreadProcessId(window, out owner);
            if (owner != processId || !IsWindowVisible(window)) return true;
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            // Closing a console window would terminate PowerShell before WPF saves its state.
            if (name.ToString().StartsWith("HwndWrapper[", StringComparison.Ordinal) &&
                PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero)) sent++;
            return true;
        }, IntPtr.Zero);
        return sent;
    }
}
'@
}

function Get-OnulnOwnedProcesses([string[]]$InstallRoots) {
    $executables = @{}; $launchers = @{}
    foreach ($folder in $InstallRoots) {
        $full = Get-OnulnFullPath $folder
        foreach ($name in @('Onuln.exe', '오늘은.exe')) { $executables[(Join-Path $full $name)] = $true }
        $launchers[(Join-Path $full 'launch.ps1')] = $true
    }
    # Failure to inspect ownership must stop the caller before it overwrites running files.
    $rows = @(Get-CimInstance Win32_Process -Filter "Name='Onuln.exe' OR Name='오늘은.exe' OR Name='powershell.exe'" -ErrorAction Stop)
    foreach ($row in $rows) {
        if ($row.ProcessId -eq $PID) { continue }
        if ($row.Name -ieq 'powershell.exe') {
            if ([string]::IsNullOrWhiteSpace($row.CommandLine)) { continue }
            Initialize-OnulnNative
            $parts = [OnulnInstallerNative]::Arguments($row.CommandLine)
            for ($i = 1; $i -lt $parts.Length; $i++) {
                if ($parts[$i] -ine '-File') {
                    if ($parts[$i] -iin @('-NoLogo', '-NoProfile', '-NonInteractive', '-NoExit', '-Sta', '-Mta')) { continue }
                    if ($parts[$i] -iin @('-ExecutionPolicy', '-WindowStyle', '-Version', '-InputFormat', '-OutputFormat', '-ConfigurationName', '-PSConsoleFile') -and $i + 1 -lt $parts.Length) { $i++; continue }
                    # Unknown/abbreviated command modes are not proof that this is our launcher.
                    break
                }
                if ($i + 1 -lt $parts.Length) {
                    try { $script = Get-OnulnFullPath $parts[$i + 1] } catch { break }
                    if ($launchers.ContainsKey($script)) { $row }
                }
                break
            }
        } elseif (-not [string]::IsNullOrWhiteSpace($row.ExecutablePath)) {
            try { $path = Get-OnulnFullPath $row.ExecutablePath } catch { continue }
            if ($executables.ContainsKey($path)) { $row }
        }
    }
}

function Send-OnulnClose([int]$ProcessId) {
    Initialize-OnulnNative
    [OnulnInstallerNative]::CloseWindows($ProcessId) | Out-Null
}

function Stop-OnulnInstances([string[]]$InstallRoots, [int]$TimeoutMs = 5000) {
    foreach ($row in @(Get-OnulnOwnedProcesses $InstallRoots)) {
        $process = Get-Process -Id $row.ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $process) { continue }
        try {
            if ($process.HasExited) { continue }
            Write-Host ('  정상 종료 대기: PID ' + $row.ProcessId)
            Send-OnulnClose $row.ProcessId
            if (-not $process.WaitForExit($TimeoutMs)) {
                throw ('위젯이 정상 종료되지 않아 작업을 중단합니다. 위젯을 직접 종료한 뒤 다시 실행해 주세요. PID ' + $row.ProcessId)
            }
        } finally { $process.Dispose() }
    }
}

function Assert-OnulnContainedPath([string]$Root, [string]$Path) {
    $base = Get-OnulnFullPath $Root
    $full = Get-OnulnFullPath $Path
    if ($full -ine $base -and -not $full.StartsWith($base.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A file path leaves the installation folder.'
    }
    # Inspect each existing ancestor before enumeration or writes. Never follow a junction.
    $current = $full
    while ($current) {
        $item = $null
        try { $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop }
        catch [System.Management.Automation.ItemNotFoundException] { }
        if ($null -ne $item) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw ('링크/재분석점 경로는 자동 이전하지 않습니다: ' + $current)
            }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
    return $full
}

function Remove-OnulnLegacyFiles([string]$Root, [bool]$WasOwned) {
    if (-not $WasOwned) { return }
    foreach ($name in @('오늘은.exe', '오늘은.dll')) {
        $path = Assert-OnulnContainedPath $Root (Join-Path $Root $name)
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            # A folder marker alone does not establish ownership of an individual legacy file.
            $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
            if ($info.ProductName -ne '오늘은') { continue }
            Remove-Item -LiteralPath $path -Force -ErrorAction Stop
            Write-Host ('  옛 파일 정리     : ' + $name + ' 지움')
        }
    }
}

function Prepare-OnulnInstall([string]$Source, [string]$Destination, [bool]$DestinationWasOwned) {
    $sourceRoot = Get-OnulnFullPath $Source
    $targetRoot = Get-OnulnFullPath $Destination
    $inPlace = $sourceRoot -ieq $targetRoot
    foreach ($name in @('Onuln.exe', 'Onuln.dll', 'launch.ps1', 'launch.vbs', 'assets\widget.ico', 'config.json')) {
        Assert-OnulnContainedPath $sourceRoot (Join-Path $sourceRoot $name) | Out-Null
        $target = Assert-OnulnContainedPath $targetRoot (Join-Path $targetRoot $name)
        if (-not $inPlace -and -not $DestinationWasOwned -and (Test-Path -LiteralPath $target)) {
            throw ('다른 프로그램의 파일일 수 있어 설치를 중단합니다. 비어 있는 폴더를 선택해 주세요: ' + $target)
        }
    }
    # Only the source package and an already verified installation may receive WM_CLOSE.
    $roots = @($sourceRoot)
    if (-not $inPlace -and $DestinationWasOwned) { $roots += $targetRoot }
    Stop-OnulnInstances $roots
    if (-not $inPlace) { Copy-OnulnUserData $sourceRoot $targetRoot }
}

function Copy-OnulnUserData([string]$Source, [string]$Destination) {
    $sourceRoot = Get-OnulnFullPath $Source
    $targetRoot = Get-OnulnFullPath $Destination
    if ($sourceRoot -ieq $targetRoot) { return }
    $pending = New-Object 'Collections.Generic.Queue[string]'
    foreach ($name in @('앱저장', 'apps', 'prediction-history', 'dollar-analysis.json')) {
        $path = Assert-OnulnContainedPath $sourceRoot (Join-Path $sourceRoot $name)
        if ($targetRoot -ieq $path -or $targetRoot.StartsWith($path + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The destination cannot be inside the source data folder.'
        }
        if (Test-Path -LiteralPath $path) { $pending.Enqueue($path) }
    }
    $files = New-Object 'Collections.Generic.List[object]'
    while ($pending.Count -gt 0) {
        $path = Assert-OnulnContainedPath $sourceRoot $pending.Dequeue()
        $relative = $path.Substring($sourceRoot.TrimEnd('\').Length + 1)
        $target = Assert-OnulnContainedPath $targetRoot (Join-Path $targetRoot $relative)
        $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if ($item.PSIsContainer) {
            if ((Test-Path -LiteralPath $target) -and -not (Test-Path -LiteralPath $target -PathType Container)) {
                throw ('기존 파일과 자료 폴더가 충돌합니다: ' + $relative)
            }
            foreach ($child in @(Get-ChildItem -LiteralPath $path -Force -ErrorAction Stop)) { $pending.Enqueue($child.FullName) }
        } elseif (Test-Path -LiteralPath $target) {
            if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
                throw ('기존 사용자 자료와 충돌하여 덮어쓰지 않고 중단합니다. 비어 있는 설치 폴더를 선택해 주세요: ' + $relative)
            }
        } else { $files.Add([pscustomobject]@{ Source = $path; Destination = $target }) }
    }
    # Preflight every conflict before copying any user state. Existing files are never replaced.
    foreach ($file in $files) {
        $source = Assert-OnulnContainedPath $sourceRoot $file.Source
        $target = Assert-OnulnContainedPath $targetRoot $file.Destination
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        $target = Assert-OnulnContainedPath $targetRoot $target
        [IO.File]::Copy($source, $target, $false)
    }
    Write-Host ('  사용자 자료      : 즐겨찾기·그림·예측 기록 ' + $files.Count + '개 이전 (기존 동일 파일 유지)')
}
