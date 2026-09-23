using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Arrow.SourceGenerator.Model;

/// <summary>
/// An immutable array with value equality, so incremental pipeline steps holding one compare by
/// content. <see cref="ImmutableArray{T}"/> compares by reference and would defeat caching.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    public EquatableArray(IEnumerable<T> items)
    {
        _items = [.. items];
    }

    public static EquatableArray<T> Empty => default;

    public int Count => _items?.Length ?? 0;

    public T this[int index] => (_items ?? [])[index];

    public bool Equals(EquatableArray<T> other)
    {
        T[] left = _items ?? [];
        T[] right = other._items ?? [];
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (T item in _items ?? [])
        {
            hash = unchecked((hash * 31) + (item?.GetHashCode() ?? 0));
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) =>
        left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) =>
        !left.Equals(right);
}

internal static class EquatableArray
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
        where T : IEquatable<T> => new(items);
}
