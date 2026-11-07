#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(3, 60)][int]$ObserveSeconds = 12
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$appProcess = $null
$stdoutTask = $null
$stderrTask = $null
$exitCode = 1
$result = [ordered]@{
    SchemaVersion = 1; Status = 'Running'; Error = $null
    StartedAtUtc = [DateTime]::UtcNow.ToString('o'); FinishedAtUtc = $null
    LaunchingTokenIsAdministrator = $null; ProcessId = $null
    ObserveSeconds = $ObserveSeconds; ObservedAliveSeconds = 0
    NaturalExitCode = $null; HelperChildrenObserved = @(); HelperCheck = 'Direct-child polling'; Cleanup = 'NotStarted'
    ElevationRequested = $false; SettingsOperationsInvoked = $false
    InteractiveRenderingVerified = $false
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $result.Status = 'Blocked'; $exitCode = 2
        throw 'Native startup smoke requires Windows.'
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        $result.LaunchingTokenIsAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    finally { $identity.Dispose() }
    $executable = [IO.Path]::GetFullPath($ExecutablePath)
    if ([IO.Path]::GetFileName($executable) -ine 'Carpatia.Ultimate.exe' -or -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw 'The explicit path must point to the built Carpatia.Ultimate.exe.'
    }
    $null = Get-Command Get-CimInstance -ErrorAction Stop
    $appProcess = New-Object System.Diagnostics.Process
    $appProcess.StartInfo.FileName = $executable
    $appProcess.StartInfo.WorkingDirectory = Split-Path -Parent $executable
    $appProcess.StartInfo.UseShellExecute = $false
    $appProcess.StartInfo.RedirectStandardOutput = $true
    $appProcess.StartInfo.RedirectStandardError = $true
    # asInvoker inherits the CI process token; no runas, helper or token changes.
    if (-not $appProcess.Start()) { throw 'The application process did not start.' }
    $result.ProcessId = $appProcess.Id
    $result.Cleanup = 'Pending'
    $stdoutTask = $appProcess.StandardOutput.ReadToEndAsync()
    $stderrTask = $appProcess.StandardError.ReadToEndAsync()
    $started = [DateTime]::UtcNow
    do {
        if ($appProcess.WaitForExit(500)) {
            $result.NaturalExitCode = $appProcess.ExitCode
            throw ('Application exited before the observation window; exit code ' + $appProcess.ExitCode)
        }
        $filter = "ParentProcessId = $($result.ProcessId) AND Name = 'Carpatia.Ultimate.Helper.exe'"
        $helpers = @(Get-CimInstance -ClassName Win32_Process -Filter $filter -ErrorAction Stop)
        if ($helpers.Count -gt 0) {
            $result.HelperChildrenObserved = @($helpers | ForEach-Object { [int]$_.ProcessId })
            throw 'An unexpected helper child was observed during read-only startup.'
        }
        $result.ObservedAliveSeconds = [Math]::Round(([DateTime]::UtcNow - $started).TotalSeconds, 2)
    } while ($result.ObservedAliveSeconds -lt $ObserveSeconds)
    $appProcess.Refresh()
    if ($appProcess.HasExited) {
        $result.NaturalExitCode = $appProcess.ExitCode
        throw ('Application exited during observation; exit code ' + $appProcess.ExitCode)
    }
    $result.Status = 'Passed'; $exitCode = 0
}
catch {
    $result.Error = $_.Exception.Message
    if ($result.Status -ne 'Blocked') { $result.Status = 'Failed' }
}
finally {
    if ($null -ne $appProcess) {
        try {
            if ($result.ProcessId -and -not $appProcess.HasExited) {
                [void]$appProcess.CloseMainWindow()
                if ($appProcess.WaitForExit(3000)) { $result.Cleanup = 'ClosedStartedProcess' }
                else {
                    $appProcess.Kill() # Exact instance started above; never kill by process name.
                    if (-not $appProcess.WaitForExit(3000)) { throw 'Started process did not stop within the cleanup deadline.' }
                    $result.Cleanup = 'StoppedStartedProcess'
                }
            }
            elseif ($result.ProcessId) { $result.Cleanup = 'AlreadyExited' }
        }
        catch { $result.Status = 'Failed'; $result.Error = 'Cleanup: ' + $_.Exception.Message; $exitCode = 1 }
        foreach ($capture in @(@{ Name = 'stdout.log'; Task = $stdoutTask }, @{ Name = 'stderr.log'; Task = $stderrTask })) {
            $text = ''
            if ($null -ne $capture.Task) {
                try {
                    if ($capture.Task.Wait(2000)) { $text = $capture.Task.GetAwaiter().GetResult() }
                    else { $text = 'Output capture exceeded its cleanup deadline.' }
                }
                catch { $text = 'Output capture failed: ' + $_.Exception.Message }
            }
            [IO.File]::WriteAllText((Join-Path $OutputDirectory $capture.Name), $text, $utf8)
        }
        $appProcess.Dispose()
    }
    else {
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stdout.log'), '', $utf8)
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stderr.log'), '', $utf8)
    }
    $result.FinishedAtUtc = [DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'summary.json'), ($result | ConvertTo-Json -Depth 5), $utf8)
    Write-Host ('Read-only startup observation: ' + $result.Status)
}
exit $exitCode
