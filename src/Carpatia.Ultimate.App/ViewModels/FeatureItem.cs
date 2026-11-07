namespace Carpatia.Ultimate.App.ViewModels;

public sealed record FeatureItem(string Name, string Description, string Status = "In Development");

/// <summary>Roadmap presentation only. These entries are not registered executable tweaks.</summary>
public static class FeatureCatalog
{
    public static IReadOnlyList<FeatureItem> Cpu { get; } = new[]
    {
        new FeatureItem("Processor power policies", "Minimum / maximum processor state, AC and DC. The future adapter must capture the active power scheme and verify each value."),
        new FeatureItem("Energy preference & boost", "Energy Performance Preference and boost policies require power-policy capability detection."),
        new FeatureItem("Voltage, power & frequency", "No validated vendor write interface is integrated. Firmware protections are respected.", "Unsupported")
    };
    public static IReadOnlyList<FeatureItem> Gpu { get; } = new[]
    {
        new FeatureItem("Per-application GPU preference", "Windows graphics preference, with an exact original-state snapshot for each application."),
        new FeatureItem("GPU scheduling & telemetry", "Scheduling support must be detected. Utilization, temperatures and clocks require a real telemetry provider."),
        new FeatureItem("Clock, voltage & power limits", "Device identification does not establish permission to tune hardware.", "Unsupported")
    };
    public static IReadOnlyList<FeatureItem> Network { get; } = new[]
    {
        new FeatureItem("Adapter-specific DNS", "Planned: Automatic, Cloudflare, Quad9, Google and custom IPv4 / IPv6. Original DNS source and server order must be restored."),
        new FeatureItem("MTU & path diagnostics", "Read and validate the selected interface, measure path constraints and preserve its original MTU."),
        new FeatureItem("TCP auto-tuning & NIC settings", "Only documented, supported settings. Experimental controls remain disabled by default.")
    };
    public static IReadOnlyList<FeatureItem> System { get; } = new[]
    {
        new FeatureItem("Power & performance", "Documented processor, PCIe and USB policies with individual preview and verification."),
        new FeatureItem("Windows behavior", "Supported visual effects and startup configuration. No generic registry performance hacks."),
        new FeatureItem("Memory & storage", "Pagefile, memory pressure, TRIM and storage-health diagnostics."),
        new FeatureItem("Interrupt configuration", "No arbitrary MSI / MSI-X registry changes. A validated driver interface is required.", "Unsupported")
    };
    public static IReadOnlyList<FeatureItem> Debloat { get; } = new[]
    {
        new FeatureItem("Installed application inventory", "Package and publisher detection with a clear distinction between optional apps and Windows components."),
        new FeatureItem("Individual removal preview", "Dependency-aware selection. Uninstallation will never be presented as universally reversible.")
    };
    public static IReadOnlyList<FeatureItem> Services { get; } = new[]
    {
        new FeatureItem("Windows service inventory", "Real service state, startup configuration and dependency detection."),
        new FeatureItem("Verified startup changes", "Resource-specific snapshots and restoration. Security, updates, networking and recovery dependencies are protected.")
    };
}
