using Carpatia.Ultimate.Broker;
using Carpatia.Ultimate.Control;
using Carpatia.Ultimate.Helper;

if (!OperatingSystem.IsWindows() || args.Length != 2 || args[0] != "--pipe" || !HelperPipeName.IsValid(args[1])) return 2;
try
{
    // No request can supply or redirect this protected journal location.
    var journal = RecoveryStoreLocation.CreateProtectedJournal();
    var runtime = new WindowsControlRuntime(journal);
    return await HelperHost.RunAsync(args, runtime.Dispatcher);
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    Console.Error.WriteLine($"Helper startup was blocked: {error.Message}");
    return 3;
}
