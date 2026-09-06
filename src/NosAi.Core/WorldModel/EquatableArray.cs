using System.Collections;
using System.Collections.Immutable;

namespace NosAi.Core.WorldModel;

/// <summary>
/// An immutable, order-sensitive sequence with true structural (value)
/// equality and a stable hash code -- unlike <see cref="ImmutableArray{T}"/>
/// or <c>T[]</c>, whose default <c>Equals</c>/<c>GetHashCode</c> compare by
/// reference, not content.
///
/// Every World Model contract in this namespace that needs a collection
/// field (a map's tiles, a player's inventory, a quest's objectives, ...)
/// uses this type instead of a bare array/list, specifically so that two
/// independently-fused snapshots built from the same inputs compare equal
/// -- the "replay deterministico" requirement in
/// docs/ROADMAP_ESECUTIVA.md S:AP-01's Definition of Done. Records
/// containing a bare collection type do not get this for free (see the
/// deliberate "no collections anywhere in this type" note on
/// <c>NosAi.Core.Hardware.HardwareCapabilitySnapshot</c>); this type exists
/// so the World Model does not have to give up collections to keep it.
/// </summary>
public readonly struct EquatableArray<T> : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
{
    private readonly ImmutableArray<T> _items;

    /// <summary>The canonical empty instance. Prefer this over <c>default</c> so every empty collection is observably the same value.</summary>
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    public EquatableArray(ImmutableArray<T> items) => _items = items.IsDefault ? ImmutableArray<T>.Empty : items;

    /// <summary>Copies <paramref name="items"/> into a new immutable, equatable sequence.</summary>
    public static EquatableArray<T> From(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new EquatableArray<T>(ImmutableArray.CreateRange(items));
    }

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    public ImmutableArray<T>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_items).GetEnumerator();

    /// <summary>Structural equality: same length and every element equal in order, regardless of the two instances' underlying array identity.</summary>
    public bool Equals(EquatableArray<T> other)
    {
        if (_items.Length != other._items.Length)
            return false;

        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < _items.Length; i++)
        {
            if (!comparer.Equals(_items[i], other._items[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (T item in _items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}
