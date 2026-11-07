using System.Text.Json;
using Carpatia.Ultimate.Broker;
using Carpatia.Ultimate.Control;
using Xunit;

namespace Carpatia.Ultimate.Control.Tests;

public sealed class RequestValidationTests
{
    private static readonly Guid Target = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static ControlRequest Cpu => new(ControlAction.Preview, OperationKind.CpuMinimum)
    { SchemeId = Target, PowerSource = PowerSource.Ac, NumericValue = 50 };
    private static ControlRequest Dns => new(ControlAction.Preview, OperationKind.Dns)
    { AdapterId = Target, AddressFamily = NetworkFamily.IPv4, DnsAutomatic = true, DnsServers = [] };
    private static ControlRequest Mtu => new(ControlAction.Preview, OperationKind.Mtu)
    { AdapterId = Target, AddressFamily = NetworkFamily.IPv4, NumericValue = 1492 };
    private static ControlRequest Tcp => new(ControlAction.Preview, OperationKind.TcpAutoTuning)
    { TcpLevel = TcpAutoTuningLevel.Normal };
    private static ControlRequest Rss => new(ControlAction.Preview, OperationKind.ReceiveSideScaling)
    { AdapterId = Target, Enabled = true };
    private static ControlPreview Review => new("test/resource", "Test review",
        JsonSerializer.SerializeToElement(new { Value = 10 }), JsonSerializer.SerializeToElement(new { Value = 20 }), DateTimeOffset.UtcNow);

    public static IEnumerable<object[]> ValidRequests()
    {
        yield return [Cpu];
        yield return [Cpu with { Kind = OperationKind.CpuMaximum, PowerSource = PowerSource.Dc }];
        yield return [Cpu with { Kind = OperationKind.CpuEnergyPreference }];
        yield return [Cpu with { Kind = OperationKind.CpuBoost, NumericValue = 0 }];
        yield return [Dns];
        yield return [Dns with { DnsAutomatic = false, DnsServers = ["1.1.1.1", "1.0.0.1"] }];
        yield return [Mtu];
        yield return [Tcp];
        yield return [Rss];
        yield return [Cpu with { Action = ControlAction.Apply, ReviewedPreview = Review }];
        yield return [new ControlRequest(ControlAction.PreviewRestore, OperationKind.Dns) { OperationId = Target }];
        yield return [new ControlRequest(ControlAction.Restore, OperationKind.Dns) { OperationId = Target, ReviewedPreview = Review }];
    }

    [Theory]
    [MemberData(nameof(ValidRequests))]
    public void FixedOperationRequestsHaveOnlyTheirRequiredTypedFields(ControlRequest request) => RequestValidation.Validate(request);

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return ["unknown action", Cpu with { Action = (ControlAction)99 }];
        yield return ["unknown kind", Cpu with { Kind = (OperationKind)99 }];
        yield return ["CPU cannot target network", Cpu with { AdapterId = Target }];
        yield return ["CPU source required", Cpu with { PowerSource = null }];
        yield return ["CPU source enum validated", Cpu with { PowerSource = (PowerSource)99 }];
        yield return ["CPU scheme required", Cpu with { SchemeId = Guid.Empty }];
        yield return ["CPU value required", Cpu with { NumericValue = null }];
        yield return ["DNS cannot target CPU", Dns with { SchemeId = Target }];
        yield return ["DNS family required", Dns with { AddressFamily = null }];
        yield return ["DNS family enum validated", Dns with { AddressFamily = (NetworkFamily)99 }];
        yield return ["DNS numeric field forbidden", Dns with { NumericValue = 1500 }];
        yield return ["DNS server array required", Dns with { DnsServers = null }];
        yield return ["DNS array bounded", Dns with { DnsServers = Enumerable.Repeat("1.1.1.1", 9).ToArray() }];
        yield return ["MTU DNS fields forbidden", Mtu with { DnsAutomatic = true }];
        yield return ["MTU adapter required", Mtu with { AdapterId = Guid.Empty }];
        yield return ["RSS family forbidden", Rss with { AddressFamily = NetworkFamily.IPv4 }];
        yield return ["RSS enabled required", Rss with { Enabled = null }];
        yield return ["TCP adapter forbidden", Tcp with { AdapterId = Target }];
        yield return ["TCP enum validated", Tcp with { TcpLevel = (TcpAutoTuningLevel)99 }];
        yield return ["Preview reads its own state", Cpu with { ReviewedPreview = Review }];
        yield return ["Apply requires reviewed state", Cpu with { Action = ControlAction.Apply }];
        yield return ["Apply cannot select journal ID", Cpu with { Action = ControlAction.Apply, ReviewedPreview = Review, OperationId = Target }];
        yield return ["Preview cannot select journal ID", Cpu with { OperationId = Target }];
        yield return ["Reviewed resource required", Cpu with { Action = ControlAction.Apply, ReviewedPreview = Review with { ResourceId = "" } }];
        yield return ["Reviewed before required", Cpu with { Action = ControlAction.Apply, ReviewedPreview = Review with { Before = default } }];
        yield return ["Restore ID required", new ControlRequest(ControlAction.PreviewRestore, OperationKind.Dns)];
        yield return ["Restore target read from journal", new ControlRequest(ControlAction.PreviewRestore, OperationKind.Dns) { OperationId = Target, AdapterId = Target }];
        yield return ["Restore requires reviewed state", new ControlRequest(ControlAction.Restore, OperationKind.Dns) { OperationId = Target }];
        yield return ["PreviewRestore reads its own state", new ControlRequest(ControlAction.PreviewRestore, OperationKind.Dns) { OperationId = Target, ReviewedPreview = Review }];
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void AmbiguousOrUnrelatedFieldsAreRejectedBeforeAnAdapterCanRun(string reason, ControlRequest request)
    {
        _ = reason;
        Assert.Throws<ArgumentException>(() => RequestValidation.Validate(request));
    }
}
