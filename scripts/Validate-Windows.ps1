#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64', 'ARM64')]
    [string]$Architecture = 'x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This script builds source and runs read-only/fake-backed tests. It never
# launches the app/helper, requests elevation, installs SDKs or applies tweaks.
$projectRoot = Split-Path -Parent $PSScriptRoot
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$artifactRoot = Join-Path $projectRoot 'artifacts'
$runDirectory = Join-Path $artifactRoot ('windows-' + $runId)
$testDirectory = Join-Path $runDirectory 'test-results'
$archivePath = Join-Path $artifactRoot ('Windows-results-' + $runId + '.zip')
$utf8 = New-Object System.Text.UTF8Encoding($false)
$steps = New-Object 'System.Collections.Generic.List[object]'
$report = [ordered]@{
    SchemaVersion = 1
    StartedAtUtc = [DateTime]::UtcNow.ToString('o')
    FinishedAtUtc = $null
    Status = 'Running'
    Configuration = $Configuration
    Architecture = $Architecture
    OperatingSystem = [Environment]::OSVersion.VersionString
    PowerShell = $PSVersionTable.PSVersion.ToString()
    Sdk = $null
    AppDirectory = $null
    Error = $null
    Steps = @()
    TestReports = @()
    NativeConfigurationWritesExecuted = $false
    ApplicationLaunched = $false
}
$exitCode = 1

# Windows PowerShell 5.1 lacks ProcessStartInfo.ArgumentList. Encode each argv
# value using Windows command-line quoting, including trailing backslashes.
function ConvertTo-NativeArgument {
    param([AllowEmptyString()][string]$Value)
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $slashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') { $slashes++; continue }
        if ($character -eq '"') {
            [void]$builder.Append(('\' * (2 * $slashes + 1)))
            [void]$builder.Append('"')
        }
        else {
            [void]$builder.Append(('\' * $slashes))
            [void]$builder.Append($character)
        }
        $slashes = 0
    }
    [void]$builder.Append(('\' * (2 * $slashes)))
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-DotNetStep {
    param([string]$Name, [string[]]$Arguments)
    $logPath = Join-Path $runDirectory ($Name + '.log')
    Write-Host ('Running ' + $Name + '...')
    $process = New-Object System.Diagnostics.Process
    $standardOutput = ''
    $standardError = ''
    $nativeExit = -1
    try {
        $process.StartInfo.FileName = $dotnetPath
        $process.StartInfo.WorkingDirectory = $projectRoot
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        $process.StartInfo.EnvironmentVariables['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
        $process.StartInfo.Arguments = ($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' '
        if (-not $process.Start()) { throw 'dotnet did not start.' }
        # Drain both pipes concurrently so verbose build failures cannot deadlock.
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $standardOutput = $outputTask.GetAwaiter().GetResult()
        $standardError = $errorTask.GetAwaiter().GetResult()
        $nativeExit = $process.ExitCode
    }
    catch { $standardError = $standardError + [Environment]::NewLine + $_.Exception.Message }
    finally { $process.Dispose() }
    [IO.File]::WriteAllText($logPath, $standardOutput + "`r`n--- stderr ---`r`n" + $standardError, $utf8)
    $step = [pscustomobject]@{
        Name = $Name
        ExitCode = $nativeExit
        Outcome = $(if ($nativeExit -eq 0) { 'Passed' } else { 'Failed' })
        Log = $Name + '.log'
    }
    [void]$steps.Add($step)
    Write-Host ($Name + ': ' + $step.Outcome)
    return [pscustomobject]@{ ExitCode = $nativeExit; Output = $standardOutput }
}

New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $report.Status = 'Blocked'
        throw 'Run this script on Windows. This host cannot validate native WinUI.'
    }
    $dotnetCommand = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        $report.Status = 'Blocked'
        throw 'Install .NET SDK 10.0.403 (or a later 10.0.4xx patch), then rerun. No SDK was installed by this script.'
    }
    $dotnetPath = $dotnetCommand.Path
    # Running from the repository makes dotnet enforce global.json.
    $sdk = Invoke-DotNetStep 'sdk-version' @('--version')
    if ($sdk.ExitCode -ne 0) {
        $report.Status = 'Blocked'
        throw 'The SDK required by global.json is unavailable. See sdk-version.log; install the required SDK and rerun.'
    }
    $report.Sdk = $sdk.Output.Trim()
    $null = Invoke-DotNetStep 'dotnet-info' @('--info')

    $suites = [ordered]@{
        'engine' = 'Carpatia.Ultimate.Tests'
        'control' = 'Carpatia.Ultimate.Control.Tests'
        'broker' = 'Carpatia.Ultimate.Broker.Tests'
        'platform' = 'Carpatia.Ultimate.Platform.Tests'
        'diagnostics' = 'Carpatia.Ultimate.Diagnostics.Tests'
    }
    foreach ($suite in $suites.GetEnumerator()) {
        $project = 'tests/' + $suite.Value + '/' + $suite.Value + '.csproj'
        $null = Invoke-DotNetStep ('test-' + $suite.Key) @(
            'test', $project, '--configuration', $Configuration, '--nologo',
            '--logger', ('trx;LogFileName=' + $suite.Key + '.trx'),
            '--results-directory', $testDirectory
        )
    }

    $runtime = 'win-' + $Architecture.ToLowerInvariant()
    $properties = @('--configuration', $Configuration, '--runtime', $runtime, ('-p:Platform=' + $Architecture), '--nologo')
    $null = Invoke-DotNetStep 'build-helper' (@('build', 'src/Carpatia.Ultimate.Helper/Carpatia.Ultimate.Helper.csproj') + $properties)
    $build = Invoke-DotNetStep 'build-app' (@('build', 'src/Carpatia.Ultimate.App/Carpatia.Ultimate.App.csproj') + $properties)
    if ($build.ExitCode -eq 0) {
        $relative = 'src/Carpatia.Ultimate.App/bin/' + $Architecture + '/' + $Configuration + '/net10.0-windows10.0.19041.0/' + $runtime
        $appDirectory = Join-Path $projectRoot $relative
        $report.AppDirectory = $appDirectory
        $required = @('Carpatia.Ultimate.exe', 'Helper/Carpatia.Ultimate.Helper.exe',
            'Helper/Carpatia.Ultimate.Helper.dll', 'Helper/Carpatia.Ultimate.Helper.deps.json',
            'Helper/Carpatia.Ultimate.Helper.runtimeconfig.json')
        $missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $appDirectory $_) -PathType Leaf) })
        [void]$steps.Add([pscustomobject]@{
            Name = 'check-app-helper-payload'
            ExitCode = $(if ($missing.Count -eq 0) { 0 } else { 1 })
            Outcome = $(if ($missing.Count -eq 0) { 'Passed' } else { 'Failed' })
            Missing = $missing
        })
    }
    $failures = @($steps | Where-Object { $_.ExitCode -ne 0 })
    if ($failures.Count -eq 0) { $report.Status = 'Succeeded'; $exitCode = 0 }
    else { $report.Status = 'Failed' }
}
catch {
    $report.Error = $_.Exception.Message
    if ($report.Status -eq 'Running') { $report.Status = 'Failed' }
    if ($report.Status -eq 'Blocked') { $exitCode = 2 }
    Write-Host ('Validation ' + $report.Status + ': ' + $report.Error)
}
finally {
    foreach ($file in @(Get-ChildItem -LiteralPath $testDirectory -Filter '*.trx' -File)) {
        try {
            [xml]$testRun = Get-Content -LiteralPath $file.FullName -Raw
            $counters = $testRun.TestRun.ResultSummary.Counters
            $report.TestReports += [pscustomobject]@{
                File = 'test-results/' + $file.Name
                Passed = [int]$counters.passed
                Failed = [int]$counters.failed
                NotExecuted = [int]$counters.total - [int]$counters.executed
            }
        }
        catch { $report.Error = 'Could not summarize a test report: ' + $_.Exception.Message; $report.Status = 'Failed'; $exitCode = 1 }
    }
    $report.FinishedAtUtc = [DateTime]::UtcNow.ToString('o')
    $report.Steps = @($steps.ToArray())
    try {
        $summaryPath = Join-Path $runDirectory 'summary.json'
        [IO.File]::WriteAllText($summaryPath, ($report | ConvertTo-Json -Depth 8), $utf8)
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($runDirectory, $archivePath)
        Write-Host ('Results ZIP: ' + $archivePath)
        Write-Host ('Status: ' + $report.Status)
        if ($report.Status -eq 'Succeeded') {
            Write-Host ('App output: ' + $report.AppDirectory)
            Write-Host 'No application was launched and no Windows settings were changed.'
        }
    }
    catch { Write-Host ('Could not package the results: ' + $_.Exception.Message); $exitCode = 1 }
}
exit $exitCode
