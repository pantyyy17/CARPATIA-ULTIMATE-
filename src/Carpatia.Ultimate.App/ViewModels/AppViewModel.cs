using System.Collections.ObjectModel;
using System.Globalization;
using Carpatia.Ultimate.Core;
using Carpatia.Ultimate.Diagnostics;
using Carpatia.Ultimate.Engine;
using Carpatia.Ultimate.Broker;
using Carpatia.Ultimate.Control;

namespace Carpatia.Ultimate.App.ViewModels;

public sealed record ActivityItem(string Name, string Detail, string Timestamp, Guid? OperationId = null, bool CanRestore = false);

public sealed class AppViewModel : ObservableObject
{
    private readonly IHardwareDiagnostics _diagnostics;
    private readonly IOperationJournal _journal;
    private readonly WindowsControlRuntime _control;
    private readonly ElevatedControlClient _elevation = new();
    private bool _isBusy;
    private string _os = "Reading Windows…", _cpu = "Reading processor…", _gpu = "Reading graphics…";
    private string _cpuUsage = "—", _memoryUsage = "—", _memory = "—", _storage = "—", _driver = "—";
    private string _message = "", _sampleTime = "Not sampled", _selectedProfile = "Balanced";
    private string _journalSummary = "Reading local recovery records…";

    public AppViewModel(IHardwareDiagnostics diagnostics, IOperationJournal journal, WindowsControlRuntime control)
    {
        _diagnostics = diagnostics;
        _journal = journal;
        _control = control;
        CpuOperations = new(control, true, AddActivity, LoadRecoveryAsync);
        NetworkOperations = new(control, false, AddActivity, LoadRecoveryAsync);
    }

    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) Notify(nameof(CanRefresh)); } }
    public bool CanRefresh => !IsBusy;
    public string OperatingSystem { get => _os; private set => Set(ref _os, value); }
    public string CpuName { get => _cpu; private set => Set(ref _cpu, value); }
    public string GpuName { get => _gpu; private set => Set(ref _gpu, value); }
    public string GpuDriver { get => _driver; private set => Set(ref _driver, value); }
    public string CpuUsage { get => _cpuUsage; private set => Set(ref _cpuUsage, value); }
    public string MemoryUsage { get => _memoryUsage; private set => Set(ref _memoryUsage, value); }
    public string MemoryDescription { get => _memory; private set => Set(ref _memory, value); }
    public string StorageDescription { get => _storage; private set => Set(ref _storage, value); }
    public string Message { get => _message; private set { if (Set(ref _message, value)) Notify(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public string SampleTime { get => _sampleTime; private set => Set(ref _sampleTime, value); }
    public string JournalSummary { get => _journalSummary; private set => Set(ref _journalSummary, value); }
    public string SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (value is not ("Balanced" or "Performance" or "Ultra" or "Custom")) return;
            if (Set(ref _selectedProfile, value)) Notify(nameof(ProfileDescription));
        }
    }
    public string ProfileDescription => SelectedProfile switch
    {
        "Performance" => "Performance-oriented policies, subject to compatibility and review.",
        "Ultra" => "More aggressive supported policies. Never automatic voltage or clock changes.",
        "Custom" => "Individual settings with an explicit preview before each change.",
        _ => "A conservative starting point, designed around stability and efficiency."
    };
    public ObservableCollection<ActivityItem> Activity { get; } = new();
    public ObservableCollection<ActivityItem> RecoveryRecords { get; } = new();
    public OperationsViewModel CpuOperations { get; }
    public OperationsViewModel NetworkOperations { get; }
    public string JournalDirectory => RecoveryStoreLocation.DirectoryPath;

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Message = "";
        try
        {
            var hardware = await _diagnostics.ReadHardwareAsync();
            OperatingSystem = hardware.OperatingSystem;
            CpuName = hardware.CpuName ?? "Processor information unavailable";
            GpuName = hardware.GraphicsAdapters.Count > 0
                ? string.Join(" · ", hardware.GraphicsAdapters.Select(x => x.Name)) : "Graphics information unavailable";
            GpuDriver = hardware.GraphicsAdapters.FirstOrDefault()?.DriverVersion ?? "Driver information unavailable";
            var sample = await _diagnostics.SampleUtilizationAsync();
            CpuUsage = Percent(sample.CpuPercent);
            MemoryUsage = Percent(sample.MemoryPercent);
            MemoryDescription = hardware.TotalMemoryBytes is { } total ? $"{GiB(total):0.#} GiB installed" : "Memory information unavailable";
            StorageDescription = sample.SystemDrive is { } drive
                ? $"{GiB((ulong)Math.Max(0, drive.FreeBytes)):0.#} GiB free of {GiB((ulong)Math.Max(0, drive.TotalBytes)):0.#} GiB ({drive.Name})"
                : "Storage information unavailable";
            SampleTime = $"Sampled {sample.CapturedAt.ToLocalTime():HH:mm:ss} · refresh on demand";
            Message = string.Join("\n", hardware.Notices.Concat(sample.Notices).Distinct());
            AddActivity("Hardware refreshed", "Read-only Windows diagnostics; no settings changed.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Clear values instead of leaving stale samples looking current after a failed read.
            CpuUsage = MemoryUsage = "—";
            SampleTime = "Sample unavailable";
            Message = $"Diagnostics could not be refreshed: {error.Message}";
        }
        finally { IsBusy = false; }
    }

    public async Task LoadRecoveryAsync()
    {
        try
        {
            RecoveryStoreLocation.ValidateReadAccess();
            var records = await _journal.GetAllAsync();
            RecoveryRecords.Clear();
            foreach (var record in records)
                RecoveryRecords.Add(new ActivityItem(record.TweakId, $"{record.Status} · {record.Detail ?? record.ResourceId}",
                    record.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), record.Id,
                    record.Status != OperationStatus.Restored && _control.Dispatcher.CreateRestoreRequest(record) is not null));
            JournalSummary = records.Count == 0
                ? "No recorded operations. The helper durably records original values before the first system write."
                : $"{records.Count} operation record(s). Review the exact original configuration before requesting restoration.";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            RecoveryRecords.Clear();
            JournalSummary = $"Recovery records could not be validated. Changes must remain blocked. {error.Message}";
        }
    }

    public async Task<ControlResponse> ReadRestorePreviewAsync(Guid operationId)
    {
        try
        {
            var record = (await _journal.GetAllAsync()).Single(x => x.Id == operationId);
            var request = _control.Dispatcher.CreateRestoreRequest(record)
                ?? throw new NotSupportedException("The matching restoration adapter is unavailable.");
            return await Task.Run(() => _control.Dispatcher.ExecuteAsync(request));
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return new(OperationStatus.Failed, error.Message); }
    }

    public async Task<ControlResponse> RestoreReviewedAsync(Guid operationId, ControlPreview preview)
    {
        try
        {
            var record = (await _journal.GetAllAsync()).Single(x => x.Id == operationId);
            var request = _control.Dispatcher.CreateRestoreRequest(record)
                ?? throw new NotSupportedException("The matching restoration adapter is unavailable.");
            var response = await _elevation.ExecuteAsync(request with { Action = ControlAction.Restore, ReviewedPreview = preview });
            AddActivity("Restoration result", $"{response.Status}: {response.Message}");
            await LoadRecoveryAsync();
            return response;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return new(OperationStatus.Failed, error.Message); }
    }

    public void AddActivity(string name, string detail)
    {
        Activity.Insert(0, new ActivityItem(name, detail, DateTimeOffset.Now.ToString("HH:mm", CultureInfo.CurrentCulture)));
        while (Activity.Count > 8) Activity.RemoveAt(Activity.Count - 1);
    }

    public string CreateDiagnosticReport() => $"CARPATIA ULTIMATE EDITION 1.0.0\n{OperatingSystem}\nCPU: {CpuName}\nGPU: {GpuName}\nDriver: {GpuDriver}\nMemory: {MemoryDescription}\nStorage: {StorageDescription}\nCPU: {CpuUsage}; memory: {MemoryUsage}\n{SampleTime}\n{Message}\nPhase B: typed CPU/network adapters implemented; actual Windows hardware validation pending.";

    private static double GiB(ulong bytes) => bytes / 1073741824d;
    private static string Percent(double? value) => value.HasValue ? $"{value.Value:0}%" : "—";
}
