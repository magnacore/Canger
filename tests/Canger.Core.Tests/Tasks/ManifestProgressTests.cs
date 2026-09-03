// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.Tasks;

/// <summary>
/// Counting the entries an archiver names, against what each is worth.
/// </summary>
/// <remarks>
/// The two ends disagree in opposite directions and both have to work: storing, the manifest holds
/// absolute paths while <c>zip</c> prints the relative names it was given; extracting, the manifest
/// holds names inside the archive while <c>unzip</c> prints where it put them.
/// </remarks>
public class ManifestProgressTests
{
    private static ManifestProgress Extracting() =>
        new([("data/f0.bin", 100), ("data/f1.bin", 200), ("data/sub/f0.bin", 700)]);

    [Fact]
    public void KnowsTheTotalBeforeAnythingHasHappened()
    {
        // An archive's index is read up front, so unlike a tree that must be walked there is a
        // total from the first frame.
        ManifestProgress progress = Extracting();

        Assert.True(progress.IsMeasured);
        Assert.Equal(1000, progress.Total);
        Assert.Null(progress.Completed);
    }

    [Fact]
    public void CountsAnEntryWhereverTheCommandPutIt()
    {
        // unzip prints the destination path, which is longer than the name in the archive.
        ManifestProgress progress = Extracting();

        progress.Update("  inflating: /home/me/out/data/f1.bin\n");

        Assert.Equal(200, progress.Completed);
    }

    [Fact]
    public void CountsAnEntryNamedMoreBrieflyThanTheManifestKnowsIt()
    {
        // The storing direction: the manifest holds absolute paths, zip prints what it was given.
        ManifestProgress progress =
            new([("/home/me/data/f0.bin", 100), ("/home/me/data/f1.bin", 200)]);

        progress.Update("  adding: data/f1.bin (deflated 24%)\n");

        Assert.Equal(200, progress.Completed);
    }

    [Fact]
    public void TellsApartTwoEntriesSharingAName()
    {
        // data/f0.bin and data/sub/f0.bin end in the same segment, and crediting the wrong one
        // would put the figure out by the difference between them for the rest of the run.
        ManifestProgress progress = Extracting();

        progress.Update("  inflating: /out/data/sub/f0.bin\n");

        Assert.Equal(700, progress.Completed);
    }

    [Fact]
    public void CountsEachEntryOnlyOnce()
    {
        // The whole stream is offered every slice, so everything already seen arrives again.
        ManifestProgress progress = Extracting();

        progress.Update("  inflating: /out/data/f0.bin\n");
        progress.Update("  inflating: /out/data/f0.bin\n  inflating: /out/data/f1.bin\n");
        progress.Update("  inflating: /out/data/f0.bin\n  inflating: /out/data/f1.bin\n");

        Assert.Equal(300, progress.Completed);
    }

    [Fact]
    public void IgnoresLinesThatNameNothingItKnows()
    {
        // unzip's banner, and a file from some other archive entirely.
        ManifestProgress progress = Extracting();

        progress.Update("Archive:  out.zip\n  inflating: /out/elsewhere/other.bin\n");

        Assert.Null(progress.Completed);
    }

    [Fact]
    public void ReadsTheStreamInfoZipActuallyTalksOn()
    {
        // Both halves of Info-ZIP announce entries on standard output. A source left reading
        // standard error would hear nothing at all from either of them.
        Assert.True(Extracting().ReadsStandardOutput);
    }

    [Fact]
    public void SurvivesAnEmptyArchive()
    {
        ManifestProgress progress = new([]);

        progress.Update("Archive:  empty.zip\n");

        Assert.Null(progress.Total);
        Assert.Null(progress.Completed);
    }

    [Fact]
    public void WithholdsTheTotalUntilTheWalkHasFinished()
    {
        // The storing direction measures a tree in slices, and a total that grew as it went would
        // make the percentage fall as the measuring caught up.
        InMemoryFileSystem files = new();
        files.AddFileOfSize("/w/data/f0.bin", 100);
        files.AddFileOfSize("/w/data/f1.bin", 200);

        ManifestProgress progress = new(files, ["/w/data"]);

        Assert.False(progress.IsMeasured);
        Assert.Null(progress.Total);

        IEnumerator<Unit> walk = progress.Measure();
        while (walk.MoveNext())
        {
            Assert.Null(progress.Total);
        }

        Assert.True(progress.IsMeasured);
        Assert.Equal(300, progress.Total);
    }
}
