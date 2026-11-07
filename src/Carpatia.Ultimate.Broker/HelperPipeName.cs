namespace Carpatia.Ultimate.Broker;

public static class HelperPipeName
{
    public const string Prefix = "Carpatia.Ultimate.Helper.";
    public static string Create() => Prefix + Guid.NewGuid().ToString("N");
    public static bool IsValid(string? name) => name is not null
        && name.Length == Prefix.Length + 32
        && name.StartsWith(Prefix, StringComparison.Ordinal)
        && Guid.TryParseExact(name.AsSpan(Prefix.Length), "N", out var nonce)
        && nonce != Guid.Empty;
}
