using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Carpatia.Ultimate.Broker;
using Carpatia.Ultimate.Core;
using Xunit;

namespace Carpatia.Ultimate.Broker.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public async Task FramingRoundTripsReviewedTypedRequest()
    {
        var target = Guid.NewGuid();
        var preview = new ControlPreview("power:" + target, "Minimum processor state: 5 to 10",
            JsonSerializer.SerializeToElement(new { Value = 5 }),
            JsonSerializer.SerializeToElement(new { Value = 10 }), DateTimeOffset.UtcNow);
        var request = new ControlRequest(ControlAction.Apply, OperationKind.CpuMinimum)
        {
            SchemeId = target, PowerSource = PowerSource.Ac, NumericValue = 10, ReviewedPreview = preview
        };
        await using var stream = new MemoryStream();
        await ControlWire.WriteAsync(stream, request);
        stream.Position = 0;
        var read = await ControlWire.ReadAsync<ControlRequest>(stream);
        Assert.Equal(request.Action, read.Action);
        Assert.Equal(request.Kind, read.Kind);
        Assert.Equal(request.SchemeId, read.SchemeId);
        Assert.Equal(10u, read.NumericValue);
        Assert.Equal(preview.ResourceId, read.ReviewedPreview!.ResourceId);
        Assert.True(JsonElement.DeepEquals(preview.Before, read.ReviewedPreview.Before));
        Assert.True(JsonElement.DeepEquals(preview.Expected, read.ReviewedPreview.Expected));
    }

    [Fact]
    public void ReadOnlyRestorePreviewHasAnExplicitProtocolAction()
    {
        var operationId = Guid.NewGuid();
        var request = new ControlRequest(ControlAction.PreviewRestore, OperationKind.Dns) { OperationId = operationId };
        var copy = ControlWire.Deserialize<ControlRequest>(ControlWire.Serialize(request));
        Assert.Equal(ControlAction.PreviewRestore, copy.Action);
        Assert.Equal(operationId, copy.OperationId);
        Assert.Null(copy.ReviewedPreview);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"Action\":\"Apply\"}")]
    [InlineData("{\"Action\":\"Apply\",\"Kind\":\"CpuMinimum\",\"Command\":\"anything\"}")]
    [InlineData("{\"Action\":999,\"Kind\":\"CpuMinimum\"}")]
    [InlineData("{\"Action\":\"Apply\",\"Kind\":999}")]
    [InlineData("{\"Action\":\"Apply\",\"Kind\":\"999\"}")]
    [InlineData("{\"Action\":\"ExecuteScript\",\"Kind\":\"CpuMinimum\"}")]
    [InlineData("{\"Action\":\"Apply\",\"Action\":\"Restore\",\"Kind\":\"CpuMinimum\"}")]
    [InlineData("{\"Action\":\"Apply\",\"Kind\":\"CpuMinimum\",\"PowerSource\":4}")]
    [InlineData("{\"Action\":\"Apply\",\"Kind\":\"Dns\",\"AdapterId\":\"../../bad\"}")]
    public void RejectsUnsupportedOrAmbiguousRequests(string json)
        => Assert.Throws<InvalidDataException>(() => ControlWire.Deserialize<ControlRequest>(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65537)]
    [InlineData(int.MaxValue)]
    public async Task RejectsInvalidFrameLengthBeforeAllocation(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, length);
        await using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => ControlWire.ReadAsync<ControlRequest>(stream));
    }

    [Fact]
    public async Task RejectsTruncatedHeaderAndPayload()
    {
        await using var header = new MemoryStream(new byte[3]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => ControlWire.ReadAsync<ControlRequest>(header));
        var bytes = new byte[6];
        BinaryPrimitives.WriteInt32BigEndian(bytes, 8);
        await using var payload = new MemoryStream(bytes);
        await Assert.ThrowsAsync<EndOfStreamException>(() => ControlWire.ReadAsync<ControlRequest>(payload));
    }

    [Fact]
    public void RejectsOversizedPayloadBeforeWriting()
    {
        var response = new ControlResponse(OperationStatus.Failed, new string('x', ControlWire.MaximumPayloadBytes));
        Assert.Throws<InvalidDataException>(() => ControlWire.Serialize(response));
        Assert.Throws<InvalidDataException>(() => ControlWire.Deserialize<ControlResponse>(new byte[ControlWire.MaximumPayloadBytes + 1]));
    }

    [Theory]
    [InlineData("{\"Message\":\"missing status\"}")]
    [InlineData("{\"Status\":\"Verified\",\"Message\":null}")]
    [InlineData("{\"Status\":\"MagicSuccess\",\"Message\":\"bad\"}")]
    public void NeverAcceptsMalformedSuccessResponse(string json)
        => Assert.Throws<InvalidDataException>(() => ControlWire.Deserialize<ControlResponse>(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public async Task CancellationInterruptsWaitingForFrame()
    {
        await using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ControlWire.ReadAsync<ControlRequest>(stream, cancellation.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anything")]
    [InlineData("../Carpatia.Ultimate.Helper.11111111111111111111111111111111")]
    [InlineData("Carpatia.Ultimate.Helper.00000000000000000000000000000000")]
    [InlineData("Carpatia.Ultimate.Helper.1111111111111111111111111111111z")]
    [InlineData("Carpatia.Ultimate.Helper.11111111-1111-1111-1111-111111111111")]
    public void RejectsUnscopedPipeNames(string? name) => Assert.False(HelperPipeName.IsValid(name));

    [Fact]
    public void CreatesFreshScopedNonce()
    {
        var first = HelperPipeName.Create();
        Assert.True(HelperPipeName.IsValid(first));
        Assert.NotEqual(first, HelperPipeName.Create());
    }

    [Fact]
    public async Task ElevationIsUnavailableOnNonWindows()
    {
        if (OperatingSystem.IsWindows()) return;
        var response = await new ElevatedControlClient().ExecuteAsync(new ControlRequest(ControlAction.Apply, OperationKind.Mtu));
        Assert.Equal(OperationStatus.Unsupported, response.Status);
    }
}
