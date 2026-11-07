using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carpatia.Ultimate.Broker;

/// <summary>Length-delimited, bounded JSON. A malformed message fails closed.</summary>
public static class ControlWire
{
    public const int MaximumPayloadBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static byte[] Serialize<T>(T value) where T : notnull
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (bytes.Length is 0 or > MaximumPayloadBytes)
            throw new InvalidDataException("The control message exceeds the permitted size.");
        return bytes;
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> data) where T : notnull
    {
        if (data.Length is 0 or > MaximumPayloadBytes)
            throw new InvalidDataException("The control message has an invalid size.");
        try
        {
            return JsonSerializer.Deserialize<T>(data, Options)
                ?? throw new InvalidDataException("The control message is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The control message is malformed or uses an unsupported schema.", error);
        }
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
        where T : notnull
    {
        var bytes = Serialize(value);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        where T : notnull
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size is <= 0 or > MaximumPayloadBytes)
            throw new InvalidDataException("The control message has an invalid frame size.");
        var payload = new byte[size];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return Deserialize<T>(payload);
    }
}
