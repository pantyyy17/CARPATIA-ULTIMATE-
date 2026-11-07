using System.Text.Json;
using System.Text.Json.Serialization;
using Carpatia.Ultimate.Core;

namespace Carpatia.Ultimate.Broker;

public enum ControlAction { Preview, PreviewRestore, Apply, Restore }
public enum OperationKind
{
    CpuMinimum, CpuMaximum, CpuEnergyPreference, CpuBoost,
    Dns, Mtu, TcpAutoTuning, ReceiveSideScaling
}
public enum PowerSource { Ac, Dc }
public enum NetworkFamily { IPv4, IPv6 }
public enum TcpAutoTuningLevel { Disabled, HighlyRestricted, Restricted, Normal, Experimental }

/// <summary>
/// A request names a fixed platform operation, never an executable, command,
/// registry path or journal directory. Dispatchers must reject unrelated fields.
/// </summary>
public sealed record ControlRequest([property: JsonRequired] ControlAction Action, [property: JsonRequired] OperationKind Kind)
{
    public Guid? SchemeId { get; init; }
    public PowerSource? PowerSource { get; init; }
    public Guid? AdapterId { get; init; }
    public NetworkFamily? AddressFamily { get; init; }
    public uint? NumericValue { get; init; }
    public bool? DnsAutomatic { get; init; }
    public string[]? DnsServers { get; init; }
    public TcpAutoTuningLevel? TcpLevel { get; init; }
    public bool? Enabled { get; init; }
    public Guid? OperationId { get; init; }
    public ControlPreview? ReviewedPreview { get; init; }
}

public sealed record ControlPreview(
    [property: JsonRequired] string ResourceId,
    [property: JsonRequired] string Description,
    [property: JsonRequired] JsonElement Before,
    [property: JsonRequired] JsonElement Expected,
    [property: JsonRequired] DateTimeOffset CapturedAt);

public sealed record ControlResponse(
    [property: JsonRequired] OperationStatus Status,
    [property: JsonRequired] string Message,
    ControlPreview? Preview = null,
    Guid? OperationId = null,
    bool Cancelled = false,
    bool RestartRequired = false);

public interface IControlDispatcher
{
    Task<ControlResponse> ExecuteAsync(
        ControlRequest request,
        Func<ControlPreview, Task<bool>>? confirmation = null,
        CancellationToken cancellationToken = default);
}
