using Carpatia.Ultimate.Broker;

namespace Carpatia.Ultimate.Control;

public static class RequestValidation
{
    public static void Validate(ControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Action) || !Enum.IsDefined(request.Kind)) throw new ArgumentException("Unknown operation.");
        if (request.Action is ControlAction.Restore or ControlAction.PreviewRestore)
        {
            if (request.OperationId is null || request.OperationId == Guid.Empty)
                throw new ArgumentException("Select an existing recovery operation.");
            if (request.SchemeId is not null || request.PowerSource is not null || request.AdapterId is not null
                || request.AddressFamily is not null || request.NumericValue is not null || request.DnsAutomatic is not null
                || request.DnsServers is not null || request.TcpLevel is not null || request.Enabled is not null)
                throw new ArgumentException("Restore accepts only the stored operation ID and kind; its resource is read from the journal.");
            if ((request.Action == ControlAction.Restore) != (request.ReviewedPreview is not null))
                throw new ArgumentException("Restore requires a reviewed plan; PreviewRestore must read its own state.");
            return;
        }
        if (request.OperationId is not null) throw new ArgumentException("Apply/preview does not accept a recovery ID.");
        if (request.Action == ControlAction.Apply && request.ReviewedPreview is null)
            throw new ArgumentException("A reviewed preview is required.");
        if (request.Action == ControlAction.Preview && request.ReviewedPreview is not null)
            throw new ArgumentException("A preview request cannot provide its own state.");
        if (request.ReviewedPreview is { } preview
            && (string.IsNullOrWhiteSpace(preview.ResourceId) || string.IsNullOrWhiteSpace(preview.Description)
                || preview.Before.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null
                || preview.Expected.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null))
            throw new ArgumentException("The reviewed plan is incomplete.");

        bool cpu = request.Kind is OperationKind.CpuMinimum or OperationKind.CpuMaximum or OperationKind.CpuEnergyPreference or OperationKind.CpuBoost;
        if (cpu)
        {
            Require(request.SchemeId is { } scheme && scheme != Guid.Empty && request.PowerSource is { } source
                && Enum.IsDefined(source) && request.NumericValue is not null, "Select a power scheme, AC/DC source and policy value.");
            Require(request.AdapterId is null && request.AddressFamily is null && request.DnsAutomatic is null
                && request.DnsServers is null && request.TcpLevel is null && request.Enabled is null, "Unrelated fields are not allowed for CPU policies.");
            return;
        }
        Require(request.SchemeId is null && request.PowerSource is null, "Network operations cannot provide CPU targets.");
        if (request.Kind == OperationKind.TcpAutoTuning)
        {
            Require(request.AdapterId is null && request.AddressFamily is null && request.NumericValue is null
                && request.DnsAutomatic is null && request.DnsServers is null && request.Enabled is null
                && request.TcpLevel is { } level && Enum.IsDefined(level), "Select a supported TCP auto-tuning level only.");
            return;
        }
        Require(request.AdapterId is { } adapter && adapter != Guid.Empty && request.TcpLevel is null,
            "Select a stable network adapter ID.");
        if (request.Kind == OperationKind.ReceiveSideScaling)
        {
            Require(request.Enabled is not null && request.NumericValue is null && request.DnsAutomatic is null
                && request.DnsServers is null && request.AddressFamily is null, "RSS accepts only the adapter and enabled state.");
            return;
        }
        Require(request.AddressFamily is { } family && Enum.IsDefined(family) && request.Enabled is null,
            "Select IPv4 or IPv6 explicitly.");
        if (request.Kind == OperationKind.Mtu)
            Require(request.NumericValue is not null && request.DnsAutomatic is null && request.DnsServers is null,
                "MTU accepts only the adapter, family and numeric value.");
        else if (request.Kind == OperationKind.Dns)
            Require(request.NumericValue is null && request.DnsAutomatic is not null
                && request.DnsServers is not null && request.DnsServers.Length <= 8,
                "Choose automatic DNS or provide up to eight literal DNS addresses.");
        else throw new ArgumentException("Unknown network operation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}
