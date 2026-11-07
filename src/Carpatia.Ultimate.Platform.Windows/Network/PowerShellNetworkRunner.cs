using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carpatia.Ultimate.Platform.Windows.Network;

internal sealed class PowerShellNetworkRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public async ValueTask<T> ExecuteAsync<T>(object request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows PowerShell is required.");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Path.Combine(windows, "System32");
        var powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = system
        };
        start.Environment["PSModulePath"] = Path.Combine(system, "WindowsPowerShell", "v1.0", "Modules");
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) }) start.ArgumentList.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("Windows PowerShell could not be started.");
        try
        {
            var outputTask = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var errorTask = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            if (process.ExitCode != 0)
                throw new NotSupportedException($"The Windows network provider rejected the operation. {error}");
            try
            {
                return JsonSerializer.Deserialize<T>(output, JsonOptions)
                    ?? throw new IOException("The network provider returned an empty result.");
            }
            catch (JsonException failure) { throw new IOException("The network provider returned an invalid result.", failure); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("The Windows network provider exceeded its 30-second deadline. Re-read system state before continuing.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) return builder.ToString();
            if (builder.Length + count > 1024 * 1024) throw new IOException("The network provider exceeded its output limit.");
            builder.Append(buffer, 0, count);
        }
    }

    // Fixed code only. The JSON request cannot introduce PowerShell expressions, names,
    // paths, module names, executable names or additional cmdlets.
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        Set-StrictMode -Version Latest
        [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        try {
            $r = [Console]::In.ReadToEnd() | ConvertFrom-Json
            if ($r.Operation -notin @('ReadMtu','WriteMtu','ReadTcp','WriteTcp','ReadRss','WriteRss')) { throw 'Unsupported operation.' }
            function Require-Administrator {
                $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
                try {
                    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
                    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator token required.' }
                } finally { $identity.Dispose() }
            }
            function Resolve-Adapter {
                $id = [Guid]$r.AdapterId
                if ($id -eq [Guid]::Empty) { throw 'Adapter identity is empty.' }
                $matches = @(NetAdapter\Get-NetAdapter -IncludeHidden | Where-Object { [Guid]$_.InterfaceGuid -eq $id })
                if ($matches.Count -ne 1) { throw 'Selected adapter is missing or ambiguous.' }
                return $matches[0]
            }
            function Read-Mtu {
                $a = Resolve-Adapter
                if ($r.Family -notin @('IPv4','IPv6')) { throw 'Unknown address family.' }
                $active = @(NetTCPIP\Get-NetIPInterface -InterfaceIndex $a.ifIndex -AddressFamily $r.Family -PolicyStore ActiveStore)
                $persistent = @(NetTCPIP\Get-NetIPInterface -InterfaceIndex $a.ifIndex -AddressFamily $r.Family -PolicyStore PersistentStore)
                if ($active.Count -ne 1 -or $persistent.Count -ne 1 -or [uint32]$a.MtuSize -eq 0) {
                    throw 'Both unambiguous MTU policy stores and the link MTU must be readable.'
                }
                return [ordered]@{ AdapterId=([Guid]$a.InterfaceGuid).ToString('D'); Family=[string]$r.Family;
                    ActiveBytes=[uint32]$active[0].NlMtu; PersistentBytes=[uint32]$persistent[0].NlMtu; LinkMtuBytes=[uint32]$a.MtuSize }
            }
            function Read-Tcp {
                if ($r.Template -notin @('Internet','InternetCustom','DatacenterCustom')) { throw 'Unsupported TCP template.' }
                $items = @(NetTCPIP\Get-NetTCPSetting -SettingName ([string]$r.Template))
                if ($items.Count -ne 1) { throw 'TCP template is missing or ambiguous.' }
                return $items[0]
            }
            function Read-Rss {
                $a = Resolve-Adapter
                $rss = @(NetAdapter\Get-NetAdapterRss -IncludeHidden | Where-Object { $_.Name -ceq $a.Name })
                if ($rss.Count -ne 1) { throw 'The selected adapter does not expose one supported RSS configuration.' }
                return $rss[0]
            }
            switch ($r.Operation) {
                'ReadMtu' { $result = Read-Mtu }
                'WriteMtu' {
                    Require-Administrator
                    $current = Read-Mtu
                    $minimum = if ($r.Family -eq 'IPv6') { 1280 } else { 576 }
                    foreach ($bytes in @([uint32]$r.ActiveBytes,[uint32]$r.PersistentBytes)) {
                        if ($bytes -lt $minimum -or $bytes -gt 65535 -or $bytes -gt $current.LinkMtuBytes) { throw 'MTU is outside interface bounds.' }
                    }
                    $tool = Join-Path ([Environment]::GetFolderPath('Windows')) 'System32\netsh.exe'
                    $family = if ($r.Family -eq 'IPv6') { 'ipv6' } else { 'ipv4' }
                    $a = Resolve-Adapter
                    $persistentArgument = 'mtu=' + ([uint32]$r.PersistentBytes).ToString([Globalization.CultureInfo]::InvariantCulture)
                    & $tool interface $family set subinterface ([string]$a.ifIndex) $persistentArgument store=persistent | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw 'Persistent MTU update failed.' }
                    $a = Resolve-Adapter
                    $activeArgument = 'mtu=' + ([uint32]$r.ActiveBytes).ToString([Globalization.CultureInfo]::InvariantCulture)
                    & $tool interface $family set subinterface ([string]$a.ifIndex) $activeArgument store=active | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw 'Active MTU update failed; recovery may be needed.' }
                    $result = $true
                }
                'ReadTcp' {
                    $t = Read-Tcp
                    $result = [ordered]@{ Template=[string]$r.Template; Local=[string]$t.AutoTuningLevelLocal;
                        Effective=[string]$t.AutoTuningLevelEffective; GroupPolicy=[string]$t.AutoTuningLevelGroupPolicy }
                }
                'WriteTcp' {
                    Require-Administrator
                    if ($r.Level -notin @('Disabled','HighlyRestricted','Restricted','Normal','Experimental')) { throw 'Unsupported TCP level.' }
                    $t = Read-Tcp
                    if ([string]$t.AutoTuningLevelGroupPolicy -ne 'NotConfigured' -or [string]$t.AutoTuningLevelLocal -ne [string]$t.AutoTuningLevelEffective) {
                        throw 'A managed or ineffective TCP policy cannot be modified safely.'
                    }
                    NetTCPIP\Set-NetTCPSetting -InputObject $t -AutoTuningLevelLocal ([string]$r.Level) -Confirm:$false
                    $result = $true
                }
                'ReadRss' {
                    $s = Read-Rss
                    $result = [ordered]@{ AdapterId=([Guid]$r.AdapterId).ToString('D'); Enabled=[bool]$s.Enabled; Profile=[string]$s.Profile;
                        NumberOfReceiveQueues=[uint32]$s.NumberOfReceiveQueues; BaseProcessorGroup=[uint16]$s.BaseProcessorGroup;
                        BaseProcessorNumber=[byte]$s.BaseProcessorNumber; MaxProcessorGroup=[uint16]$s.MaxProcessorGroup;
                        MaxProcessorNumber=[byte]$s.MaxProcessorNumber; MaxProcessors=[uint32]$s.MaxProcessors }
                }
                'WriteRss' {
                    Require-Administrator
                    if ($r.Enabled -isnot [bool]) { throw 'RSS value must be Boolean.' }
                    $s = Read-Rss
                    NetAdapter\Set-NetAdapterRss -InputObject $s -Enabled ([bool]$r.Enabled) -NoRestart -Confirm:$false
                    $result = $true
                }
            }
            $result | ConvertTo-Json -Depth 8 -Compress
            exit 0
        } catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 1
        }
        """;
}
