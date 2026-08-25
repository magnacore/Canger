// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;

namespace Canger.Core.Tests.Model;

public class SortOrderTests
{
    [Fact]
    public void DefaultConstruction_AppliesTheIntendedDefaults()
    {
        // SortOrder is a record class rather than a record struct precisely so this holds. A
        // struct's parameterless construction zero-initialises and skips the primary
        // constructor's defaults, which silently meant "files before directories, case
        // sensitive" — the opposite of what is wanted, and invisible until a listing came back
        // in the wrong order.
        foreach (SortOrder order in (SortOrder[])[new SortOrder(), SortOrder.Default])
        {
            Assert.Equal(SortKey.Natural, order.Key);
            Assert.True(order.DirectoriesFirst);
            Assert.True(order.CaseInsensitive);
            Assert.False(order.Reverse);
            Assert.False(order.UseUnicodeCollation);
        }
    }

    [Fact]
    public void With_ChangesOnlyWhatIsNamed()
    {
        SortOrder order = SortOrder.Default with { Reverse = true };

        Assert.True(order.Reverse);
        Assert.True(order.DirectoriesFirst);
        Assert.Equal(SortKey.Natural, order.Key);
    }

    [Theory]
    [InlineData("natural", SortKey.Natural)]
    [InlineData("basename", SortKey.Basename)]
    [InlineData("size", SortKey.Size)]
    [InlineData("mtime", SortKey.ModificationTime)]
    [InlineData("ctime", SortKey.ChangeTime)]
    [InlineData("atime", SortKey.AccessTime)]
    [InlineData("type", SortKey.Type)]
    [InlineData("extension", SortKey.Extension)]
    [InlineData("random", SortKey.Random)]
    public void ParseKey_UnderstandsEverySettingValue(string name, SortKey expected) =>
        Assert.Equal(expected, SortOrder.ParseKey(name));

    [Fact]
    public void ParseKey_FallsBackToNaturalForAnUnknownName() =>
        Assert.Equal(SortKey.Natural, SortOrder.ParseKey("nonsense"));
}
