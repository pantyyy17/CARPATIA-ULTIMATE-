using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Carpatia.Ultimate.Core;

namespace Carpatia.Ultimate.Engine;

public sealed record OperationRecord(
    int SchemaVersion,
    Guid Id,
    string TweakId,
    string ResourceId,
    OperationStatus Status,
    JsonElement OriginalState,
    JsonElement BeforeState,
    JsonElement ExpectedState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Revision,
    IReadOnlyList<string> ConflictsWith,
    string? Detail = null,
    bool RestartRequired = false,
    JsonElement? LastObservedState = null,
    bool RequiresSuccessfulWrite = false);

public interface IOperationJournal
{
    ValueTask<IReadOnlyList<OperationRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(OperationRecord record, CancellationToken cancellationToken = default);
    ValueTask<IAsyncDisposable> AcquireExecutionLeaseAsync(CancellationToken cancellationToken = default);
    ValueTask<IAsyncDisposable> AcquireResourceLeaseAsync(string resourceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resource snapshots are flushed before atomic replacement. Lock files deliberately
/// remain in place: deleting them could let a different process lock another inode.
/// The execution lease also serializes conflict checks across different resources.
/// </summary>
public sealed class FileOperationJournal : IOperationJournal
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<OperationStatus>(allowIntegerValues: false) }
    };

    public FileOperationJournal(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public async ValueTask<IReadOnlyList<OperationRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return Array.Empty<OperationRecord>();
        var records = new List<OperationRecord>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "operation-*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
                var record = await JsonSerializer.DeserializeAsync<OperationRecord>(stream, JsonOptions, cancellationToken)
                    ?? throw new RecoveryDataException($"Recovery record '{Path.GetFileName(path)}' is empty.");
                Validate(record);
                if (Path.GetFileName(path) != FileName(record.Id))
                    throw new RecoveryDataException("Recovery filename does not match its operation ID.");
                records.Add(record);
            }
        }
        catch (JsonException error)
        {
            throw new RecoveryDataException("Recovery data is invalid. No modification is permitted until it is inspected.", error);
        }
        return records.OrderByDescending(x => x.UpdatedAt).ToArray();
    }

    public async ValueTask SaveAsync(OperationRecord record, CancellationToken cancellationToken = default)
    {
        Validate(record);
        Directory.CreateDirectory(_directory);
        var destination = Path.Combine(_directory, FileName(record.Id));
        var temporary = Path.Combine(_directory, $".pending-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public ValueTask<IAsyncDisposable> AcquireExecutionLeaseAsync(CancellationToken cancellationToken = default)
        => AcquireLeaseAsync(".execution.lock", cancellationToken);

    public ValueTask<IAsyncDisposable> AcquireResourceLeaseAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceId)));
        return AcquireLeaseAsync($".resource-{name}.lock", cancellationToken);
    }

    private async ValueTask<IAsyncDisposable> AcquireLeaseAsync(string name, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new Lease(new FileStream(Path.Combine(_directory, name), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None));
            }
            catch (IOException error) when (IsSharingViolation(error))
            {
                await Task.Delay(30, cancellationToken);
            }
        }
    }

    private static bool IsSharingViolation(IOException error)
        => (error.HResult & 0xffff) is 11 or 32 or 33;

    private static string FileName(Guid id) => $"operation-{id:N}.json";

    private static void Validate(OperationRecord record)
    {
        if (record.SchemaVersion != 1 || record.Id == Guid.Empty || record.Revision < 1
            || string.IsNullOrWhiteSpace(record.TweakId) || string.IsNullOrWhiteSpace(record.ResourceId)
            || !Enum.IsDefined(record.Status) || record.ConflictsWith is null
            || record.ConflictsWith.Any(string.IsNullOrWhiteSpace)
            || record.Status is OperationStatus.Available or OperationStatus.Unsupported or OperationStatus.AlreadyApplied
                or OperationStatus.RestoreAvailable
            || record.OriginalState.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            || record.BeforeState.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            || record.ExpectedState.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            || record.LastObservedState is { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null }
            || record.CreatedAt == default || record.UpdatedAt < record.CreatedAt)
            throw new RecoveryDataException("Recovery record is incomplete or uses an unsupported schema.");
    }

    private sealed class Lease(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
