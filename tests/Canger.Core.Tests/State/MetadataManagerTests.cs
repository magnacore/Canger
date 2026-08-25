// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.State;
using Canger.TestSupport;

namespace Canger.Core.Tests.State;

/// <summary>
/// The <c>.metadata.json</c> database that travels with the files it describes.
/// </summary>
public class MetadataManagerTests
{
    [Fact]
    public void Get_ReadsAnEntryKeyedByItsBareName()
    {
        // The bare name is what a hand-written database will use, since it is what the user sees.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/books/dune.epub")
            .AddFile("/books/.metadata.json",
                     """{"dune.epub": {"title": "Dune", "authors": "Herbert, Frank"}}""");

        FileMetadata metadata = new MetadataManager(fs).Get("/books/dune.epub");

        Assert.Equal("Dune", metadata.Title);
        Assert.Equal("Herbert, Frank", metadata.Authors);
    }

    [Fact]
    public void Get_PrefersAFullPathOverABareName()
    {
        // A deep-search database has to be able to single out one file among many sharing a name.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/books/dune.epub")
            .AddFile("/books/.metadata.json",
                     """
                     {"dune.epub": {"title": "generic"},
                      "/books/dune.epub": {"title": "specific"}}
                     """);

        Assert.Equal("specific", new MetadataManager(fs).Get("/books/dune.epub").Title);
    }

    [Fact]
    public void Get_ReturnsNothingWhenThereIsNoDatabase()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/books/dune.epub");

        Assert.True(new MetadataManager(fs).Get("/books/dune.epub").IsEmpty);
    }

    [Fact]
    public void Get_SurvivesADatabaseThatIsNotValidJson()
    {
        // A corrupt file must cost its annotations, not the listing.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/books/dune.epub")
            .AddFile("/books/.metadata.json", "{ this is not json");

        Assert.True(new MetadataManager(fs).Get("/books/dune.epub").IsEmpty);
    }

    [Fact]
    public void Get_IgnoresAnAncestorDatabaseUntilDeepSearchIsOn()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/books/scifi/dune.epub")
            .AddFile("/books/.metadata.json", """{"dune.epub": {"title": "Dune"}}""");

        MetadataManager metadata = new(fs);
        Assert.True(metadata.Get("/books/scifi/dune.epub").IsEmpty);

        metadata.DeepSearch = true;
        metadata.Reset();
        Assert.Equal("Dune", metadata.Get("/books/scifi/dune.epub").Title);
    }

    [Fact]
    public void Set_WritesTheDatabaseBesideTheFile()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/books/dune.epub");
        MetadataManager metadata = new(fs);

        metadata.Set("/books/dune.epub", new Dictionary<string, string> { ["title"] = "Dune" });

        Assert.True(fs.Exists("/books/.metadata.json"));
        Assert.Equal("Dune", new MetadataManager(fs).Get("/books/dune.epub").Title);
    }

    [Fact]
    public void Set_KeepsFieldsItWasNotAskedToChange()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/books/dune.epub")
            .AddFile("/books/.metadata.json",
                     """{"dune.epub": {"title": "Dune", "year": "1965"}}""");

        MetadataManager metadata = new(fs);
        metadata.Set("/books/dune.epub", new Dictionary<string, string> { ["authors"] = "Herbert" });

        FileMetadata read = new MetadataManager(fs).Get("/books/dune.epub");
        Assert.Equal("Dune", read.Title);
        Assert.Equal("1965", read.Year);
        Assert.Equal("Herbert", read.Authors);
    }

    [Fact]
    public void Set_RemovesAFieldGivenAnEmptyValue()
    {
        // An empty value is the only way to unset a field, which is what :meta relies on.
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/books/dune.epub")
            .AddFile("/books/.metadata.json", """{"dune.epub": {"title": "Dune", "year": "1965"}}""");

        MetadataManager metadata = new(fs);
        metadata.Set("/books/dune.epub", new Dictionary<string, string> { ["year"] = "" });

        FileMetadata read = new MetadataManager(fs).Get("/books/dune.epub");
        Assert.Equal("Dune", read.Title);
        Assert.False(read.Has("year"));
    }

    [Fact]
    public void Set_MakesTheNewValueVisibleImmediately()
    {
        // The cache that keeps a listing from rereading the file must not hide a fresh write.
        InMemoryFileSystem fs = new InMemoryFileSystem().AddFile("/books/dune.epub");
        MetadataManager metadata = new(fs);

        Assert.True(metadata.Get("/books/dune.epub").IsEmpty);
        metadata.Set("/books/dune.epub", new Dictionary<string, string> { ["title"] = "Dune" });

        Assert.Equal("Dune", metadata.Get("/books/dune.epub").Title);
    }

    [Fact]
    public void Get_ListsRecordedKeysInOrder()
    {
        InMemoryFileSystem fs = new InMemoryFileSystem()
            .AddFile("/books/dune.epub")
            .AddFile("/books/.metadata.json",
                     """{"dune.epub": {"year": "1965", "title": "Dune", "authors": "Herbert"}}""");

        Assert.Equal(["authors", "title", "year"],
                     new MetadataManager(fs).Get("/books/dune.epub").Keys);
    }
}
