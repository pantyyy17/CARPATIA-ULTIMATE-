using Carpatia.Ultimate.Core;

namespace Carpatia.Ultimate.Engine;

/// <summary>A small typed catalog. The production Phase A catalog is intentionally empty.</summary>
public sealed class TweakCatalog
{
    private readonly Dictionary<string, (TweakDefinition Definition, object Tweak)> _items = new(StringComparer.Ordinal);

    public IReadOnlyList<TweakDefinition> Definitions => _items.Values.Select(x => x.Definition).ToArray();

    public void Register<TState, TValue>(ITweak<TState, TValue> tweak) where TState : notnull where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(tweak);
        ArgumentException.ThrowIfNullOrWhiteSpace(tweak.Definition.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(tweak.Definition.ResourceId);
        if (!_items.TryAdd(tweak.Definition.Id, (tweak.Definition, tweak)))
            throw new ArgumentException($"Tweak '{tweak.Definition.Id}' is already registered.", nameof(tweak));
    }

    public ITweak<TState, TValue> Get<TState, TValue>(string id) where TState : notnull where TValue : notnull
        => _items.TryGetValue(id, out var entry) && entry.Tweak is ITweak<TState, TValue> tweak
            ? tweak : throw new KeyNotFoundException($"Tweak '{id}' is unavailable or uses different value/state types.");
}
