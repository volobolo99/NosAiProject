using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class EquatableArrayTests
{
    [Fact]
    public void Empty_HasZeroCount_AndEqualsAnotherEmptyInstance()
    {
        Assert.Empty(EquatableArray<int>.Empty);
        Assert.Equal(EquatableArray<int>.Empty, EquatableArray<int>.From(Array.Empty<int>()));
    }

    [Fact]
    public void TwoArraysWithTheSameElementsInOrder_AreEqual_RegardlessOfUnderlyingArrayIdentity()
    {
        EquatableArray<int> a = EquatableArray<int>.From(new[] { 1, 2, 3 });
        EquatableArray<int> b = EquatableArray<int>.From(new[] { 1, 2, 3 });

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentOrder_IsNotEqual()
    {
        EquatableArray<int> a = EquatableArray<int>.From(new[] { 1, 2, 3 });
        EquatableArray<int> b = EquatableArray<int>.From(new[] { 3, 2, 1 });

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void DifferentLength_IsNotEqual()
    {
        EquatableArray<int> a = EquatableArray<int>.From(new[] { 1, 2 });
        EquatableArray<int> b = EquatableArray<int>.From(new[] { 1, 2, 3 });

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Indexer_AndEnumeration_PreserveOrder()
    {
        EquatableArray<string> array = EquatableArray<string>.From(new[] { "a", "b", "c" });

        Assert.Equal("b", array[1]);
        Assert.Equal(new[] { "a", "b", "c" }, array);
    }

    [Fact]
    public void From_ThrowsOnNullSource()
    {
        Assert.Throws<ArgumentNullException>(() => EquatableArray<int>.From(null!));
    }

    [Fact]
    public void NestedRecordElements_UseStructuralEqualityToo()
    {
        WorldFact<int> f1 = WorldFact<int>.Live(1, 1.0, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WorldFact<int> f2 = WorldFact<int>.Live(1, 1.0, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        EquatableArray<WorldFact<int>> a = EquatableArray<WorldFact<int>>.From(new[] { f1 });
        EquatableArray<WorldFact<int>> b = EquatableArray<WorldFact<int>>.From(new[] { f2 });

        Assert.Equal(a, b);
    }
}
