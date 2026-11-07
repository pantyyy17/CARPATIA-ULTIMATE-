using System.Collections.ObjectModel;
using System.Net;
using Carpatia.Ultimate.Broker;
using Carpatia.Ultimate.Control;
using Carpatia.Ultimate.Core;
using Carpatia.Ultimate.Platform.Windows.Network;
using Microsoft.UI.Xaml;
using BrokerTcpLevel = Carpatia.Ultimate.Broker.TcpAutoTuningLevel;

namespace Carpatia.Ultimate.App.ViewModels;

public sealed record OperationChoice(OperationKind Kind, string Name);

public sealed class OperationsViewModel : ObservableObject
{
    private readonly WindowsControlRuntime _runtime;
    private readonly Action<string, string> _activity;
    private readonly Func<Task> _refreshRecovery;
    private readonly ElevatedControlClient _client = new();
    private Guid? _scheme;
    private bool _busy, _enabled = true;
    private double _value = 5;
    private int _source, _family;
    private string _preset = "Automatic", _servers = "", _message = "Choose a setting and read its exact change plan.";
    private string _schemeText = "Power scheme not read yet";
    private OperationChoice _selected;
    private NetworkAdapterInfo? _adapter;
    private BrokerTcpLevel _tcpLevel = BrokerTcpLevel.Normal;
    private ControlRequest? _reviewedRequest;

    public OperationsViewModel(WindowsControlRuntime runtime, bool cpu, Action<string, string> activity, Func<Task> refreshRecovery)
    {
        _runtime = runtime; IsCpu = cpu; _activity = activity; _refreshRecovery = refreshRecovery;
        Choices = cpu
            ? [new(OperationKind.CpuMinimum, "Minimum processor state"), new(OperationKind.CpuMaximum, "Maximum processor state"),
               new(OperationKind.CpuEnergyPreference, "Energy Performance Preference"), new(OperationKind.CpuBoost, "Processor boost mode")]
            : [new(OperationKind.Dns, "DNS configuration"), new(OperationKind.Mtu, "Interface MTU"),
               new(OperationKind.TcpAutoTuning, "TCP auto-tuning · Internet template"), new(OperationKind.ReceiveSideScaling, "Receive Side Scaling")];
        _selected = Choices[0];
    }

    public bool IsCpu { get; }
    public IReadOnlyList<OperationChoice> Choices { get; }
    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = new();
    public IReadOnlyList<string> DnsChoices { get; } = ["Automatic", "Cloudflare", "Quad9", "Google", "Custom"];
    public IReadOnlyList<BrokerTcpLevel> TcpLevels { get; } = [BrokerTcpLevel.Disabled, BrokerTcpLevel.HighlyRestricted, BrokerTcpLevel.Restricted, BrokerTcpLevel.Normal];
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) { Notify(nameof(CanRead)); Notify(nameof(CanApply)); } } }
    public bool CanRead => !IsBusy;
    public bool CanApply => !IsBusy && _reviewedRequest is not null;
    public string SchemeDescription { get => _schemeText; private set => Set(ref _schemeText, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public ControlPreview? Preview { get; private set; }
    public OperationChoice SelectedChoice
    {
        get => _selected;
        set
        {
            if (value is null || !Choices.Contains(value) || !Set(ref _selected, value)) return;
            NumericValue = value.Kind switch { OperationKind.CpuMaximum => 100, OperationKind.CpuEnergyPreference => 50, OperationKind.CpuBoost => 1, OperationKind.Mtu => 1500, _ => 5 };
            Invalidate();
            foreach (var name in new[] { nameof(AdapterVisibility), nameof(FamilyVisibility), nameof(NumericVisibility), nameof(DnsVisibility), nameof(TcpVisibility), nameof(RssVisibility), nameof(ValueHeader), nameof(Explanation) }) Notify(name);
        }
    }
    public NetworkAdapterInfo? SelectedAdapter { get => _adapter; set { if (Set(ref _adapter, value)) Invalidate(); } }
    public double NumericValue { get => _value; set { if (Set(ref _value, value)) Invalidate(); } }
    public int SourceIndex { get => _source; set { if (Set(ref _source, value)) Invalidate(); } }
    public int FamilyIndex { get => _family; set { if (Set(ref _family, value)) { SelectDnsPreset(); Invalidate(); } } }
    public string DnsPreset { get => _preset; set { if (value is not null && Set(ref _preset, value)) { SelectDnsPreset(); Notify(nameof(IsDnsCustom)); Invalidate(); } } }
    public bool IsDnsCustom => DnsPreset == "Custom";
    public string DnsServers { get => _servers; set { if (Set(ref _servers, value)) Invalidate(); } }
    public BrokerTcpLevel TcpLevel { get => _tcpLevel; set { if (Set(ref _tcpLevel, value)) Invalidate(); } }
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) Invalidate(); } }
    public Visibility CpuVisibility => IsCpu ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AdapterVisibility => !IsCpu && SelectedChoice.Kind != OperationKind.TcpAutoTuning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FamilyVisibility => SelectedChoice.Kind is OperationKind.Dns or OperationKind.Mtu ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NumericVisibility => IsCpu || SelectedChoice.Kind == OperationKind.Mtu ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DnsVisibility => SelectedChoice.Kind == OperationKind.Dns ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TcpVisibility => SelectedChoice.Kind == OperationKind.TcpAutoTuning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RssVisibility => SelectedChoice.Kind == OperationKind.ReceiveSideScaling ? Visibility.Visible : Visibility.Collapsed;
    public string ValueHeader => SelectedChoice.Kind == OperationKind.CpuBoost ? "Boost index (0–6)" : SelectedChoice.Kind == OperationKind.Mtu ? "Requested MTU in bytes" : "Requested policy value (0–100%)";
    public string Explanation => SelectedChoice.Kind switch
    {
        OperationKind.CpuMinimum => "100% minimum state is not 100% CPU utilization. It may increase power use and heat. Only the chosen AC/DC index changes.",
        OperationKind.CpuMaximum => "Maximum cannot be below the selected scheme's current minimum. Readback verifies the stored policy, not CPU clock speed.",
        OperationKind.CpuEnergyPreference => "Lower values favor performance; higher values favor energy saving. Firmware and Windows overlays can limit the effect.",
        OperationKind.CpuBoost => "0 Disabled · 1 Enabled · 2 Aggressive · 3 Efficient Enabled · 4 Efficient Aggressive · 5 Aggressive at Guaranteed · 6 Efficient Aggressive at Guaranteed. Only detected supported modes are accepted.",
        OperationKind.Dns => "Choose the exact adapter. DNS changes affect name resolution, not guaranteed game latency. IPv6 DNS and encrypted/profile-managed DNS are currently unsupported.",
        OperationKind.Mtu => "Both active and persistent MTU are recorded independently. 1500 is not universally optimal. A change can interrupt connectivity; path-MTU discovery is not integrated.",
        OperationKind.TcpAutoTuning => "Experimental, workload-dependent policy. Only the Internet template changes. Managed policies are rejected; existing connections may retain negotiated behavior.",
        _ => "Changes only RSS enabled state on a supported adapter. Other queue/processor settings are preserved. Restart Windows after applying or restoring."
    };

    public async Task LoadTargetsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var inventory = await _runtime.ReadInventoryAsync();
            _scheme = inventory.ActiveSchemeId;
            SchemeDescription = _scheme is { } id ? $"Active scheme: {id:D}" : "No supported active power scheme detected";
            var selectedId = SelectedAdapter?.Id;
            Adapters.Clear();
            foreach (var adapter in inventory.Adapters) Adapters.Add(adapter);
            SelectedAdapter = Adapters.FirstOrDefault(x => x.Id == selectedId);
            // The user selects an adapter explicitly; never infer the route carrying their session.
            Invalidate();
            Message = string.IsNullOrWhiteSpace(inventory.Notice) ? "Targets refreshed. Read a change plan when ready." : inventory.Notice;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Message = error.Message; }
        finally { IsBusy = false; }
    }

    public async Task<ControlResponse?> ReadPreviewAsync()
    {
        if (IsBusy) return null;
        IsBusy = true;
        try
        {
            var request = BuildRequest();
            var response = await Task.Run(() => _runtime.Dispatcher.ExecuteAsync(request));
            Preview = response.Preview;
            _reviewedRequest = response.Status == OperationStatus.Available && response.Preview is not null
                ? request with { Action = ControlAction.Apply, ReviewedPreview = response.Preview } : null;
            Message = response.Message;
            Notify(nameof(Preview)); Notify(nameof(CanApply));
            return response;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Invalidate(); Message = error.Message; return null; }
        finally { IsBusy = false; }
    }

    public async Task ApplyReviewedAsync()
    {
        if (!CanApply || _reviewedRequest is null) return;
        var request = _reviewedRequest;
        IsBusy = true;
        try
        {
            var result = await _client.ExecuteAsync(request);
            Invalidate(); Message = $"{result.Status}: {result.Message}";
            _activity(SelectedChoice.Name, Message);
            await _refreshRecovery();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Invalidate(); Message = error.Message; }
        finally { IsBusy = false; }
    }

    private ControlRequest BuildRequest()
    {
        var kind = SelectedChoice.Kind;
        var request = new ControlRequest(ControlAction.Preview, kind);
        if (IsCpu)
        {
            if (_scheme is null) throw new InvalidOperationException("Refresh and select an available active Windows power scheme.");
            if (SourceIndex is not (0 or 1)) throw new ArgumentException("Select AC or DC.");
            return request with { SchemeId = _scheme, PowerSource = SourceIndex == 0 ? PowerSource.Ac : PowerSource.Dc, NumericValue = WholeValue() };
        }
        if (kind == OperationKind.TcpAutoTuning) return request with { TcpLevel = TcpLevel };
        if (SelectedAdapter is null) throw new ArgumentException("Select the exact network adapter first.");
        request = request with { AdapterId = SelectedAdapter.Id };
        if (kind == OperationKind.ReceiveSideScaling) return request with { Enabled = Enabled };
        if (FamilyIndex is not (0 or 1)) throw new ArgumentException("Select IPv4 or IPv6.");
        request = request with { AddressFamily = FamilyIndex == 0 ? NetworkFamily.IPv4 : NetworkFamily.IPv6 };
        if (kind == OperationKind.Mtu) return request with { NumericValue = WholeValue() };
        var servers = DnsPreset == "Automatic" ? [] : DnsServers.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => IPAddress.TryParse(x, out var address) ? address.ToString() : throw new ArgumentException($"'{x}' is not a literal IP address.")).ToArray();
        return request with { DnsAutomatic = DnsPreset == "Automatic", DnsServers = servers };
    }

    private uint WholeValue()
    {
        if (!double.IsFinite(NumericValue) || NumericValue < 0 || NumericValue > uint.MaxValue || Math.Truncate(NumericValue) != NumericValue)
            throw new ArgumentException("Enter a whole, non-negative policy value.");
        return (uint)NumericValue;
    }

    private void SelectDnsPreset()
    {
        DnsServers = DnsPreset switch
        {
            "Automatic" => "",
            "Cloudflare" => FamilyIndex == 0 ? "1.1.1.1, 1.0.0.1" : "2606:4700:4700::1111, 2606:4700:4700::1001",
            "Quad9" => FamilyIndex == 0 ? "9.9.9.9, 149.112.112.112" : "2620:fe::fe, 2620:fe::9",
            "Google" => FamilyIndex == 0 ? "8.8.8.8, 8.8.4.4" : "2001:4860:4860::8888, 2001:4860:4860::8844",
            _ => DnsServers
        };
    }

    private void Invalidate() { _reviewedRequest = null; Preview = null; Notify(nameof(Preview)); Notify(nameof(CanApply)); }
}
