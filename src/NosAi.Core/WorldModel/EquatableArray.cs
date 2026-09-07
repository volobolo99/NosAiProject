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

    /// <summary>
    /// The backing array, with the <c>default</c> struct normalised to empty.
    /// </summary>
    /// <remarks>
    /// The constructor already refuses a default <see cref="ImmutableArray{T}"/>,
    /// but <c>default(EquatableArray&lt;T&gt;)</c> does not run it: the field
    /// stays a default <see cref="ImmutableArray{T}"/>, whose every member --
    /// <c>Length</c> included -- throws. That state is reachable, and became
    /// common when the World Model started holding these inside
    /// <c>WorldFact&lt;EquatableArray&lt;T&gt;&gt;</c>: an Unknown fact's value
    /// <i>is</i> <c>default(T)</c>, so hashing a snapshot containing one threw
    /// <see cref="NullReferenceException"/> from <see cref="GetHashCode"/>.
    /// Every member reads through here instead of the field.
    /// </remarks>
    private ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    /// <summary>Copies <paramref name="items"/> into a new immutable, equatable sequence.</summary>
    public static EquatableArray<T> From(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new EquatableArray<T>(ImmutableArray.CreateRange(items));
    }

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    /// <summary>Structural equality: same length and every element equal in order, regardless of the two instances' underlying array identity.</summary>
    public bool Equals(EquatableArray<T> other)
    {
        ImmutableArray<T> mine = Items;
        ImmutableArray<T> theirs = other.Items;
        if (mine.Length != theirs.Length)
            return false;

        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < mine.Length; i++)
        {
            if (!comparer.Equals(mine[i], theirs[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (T item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}
