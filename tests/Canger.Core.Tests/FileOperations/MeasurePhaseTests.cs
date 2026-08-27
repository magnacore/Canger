// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileOperations;
using Canger.Core.Tasks;
using Canger.TestSupport;

namespace Canger.Core.Tests.FileOperations;

/// <summary>
/// The measuring a transfer does before it moves anything.
/// </summary>
/// <remarks>
/// A transfer walks its sources and sums their sizes first, so the percentage and the estimate
/// mean something from the start. These pin two things about that: it finishes before a byte is
/// written, and it is not separable from the copying that follows — which is what stands between
/// Canger and a queue-wide estimate, since a job awaiting its turn cannot be asked how big it is
/// without also starting it.
/// </remarks>
public class MeasurePhaseTests
{
    private static (CopyJob Job, InMemoryFileSystem Fs) Build(int files)
    {
        InMemoryFileSystem fs = new InMemoryFileSystem().AddDirectory("/dest");
        for (int i = 0; i < files; i++)
        {
            fs.AddFileOfSize($"/src/f{i}.bin", 1000, DateTimeOffset.UnixEpoch);
        }

        return (new CopyJob(fs, [.. Enumerable.Range(0, files).Select(i => $"/src/f{i}.bin")],
                            "/dest"),
                fs);
    }

    [Fact]
    public void NothingIsWrittenUntilTheWholeTransferHasBeenMeasured()
    {
        // The percentage and the estimate are nonsense until the total is known, so the total is
        // settled before anything moves.
        (CopyJob job, InMemoryFileSystem fs) = Build(3);
        IEnumerator<Unit> steps = job.Steps();

        int measuredAt = -1;
        int wroteAt = -1;

        for (int step = 1; step <= 12; step++)
        {
            bool more = steps.MoveNext();

            if (measuredAt < 0 && job.Progress.TotalBytes == 3000)
            {
                measuredAt = step;
            }

            if (wroteAt < 0 && fs.Exists("/dest/f0.bin"))
            {
                wroteAt = step;
            }

            if (!more)
            {
                break;
            }
        }

        Assert.True(measuredAt > 0, "the total was never reached");
        Assert.True(wroteAt > measuredAt,
                    $"a byte was written at step {wroteAt}, before measuring finished at {measuredAt}");
    }

    [Fact]
    public void MeasuringAndCopyingAreAdjacentWithNothingBetweenThem()
    {
        // The finding that matters for a queue-wide estimate. Copying begins on the very step
        // after measuring ends, and nothing announces the boundary — so a caller wanting only the
        // size would have to stop after exactly the right number of steps, which is a number it
        // could only learn by measuring. Driving measurement for a queued transfer needs the two
        // phases separated first.
        (CopyJob job, InMemoryFileSystem fs) = Build(3);
        IEnumerator<Unit> steps = job.Steps();

        int step = 0;
        while (job.Progress.TotalBytes < 3000 && step < 12)
        {
            steps.MoveNext();
            step++;
        }

        Assert.False(fs.Exists("/dest/f0.bin"), "measuring should not have written anything");

        steps.MoveNext();

        Assert.True(fs.Exists("/dest/f0.bin"),
                    "the step after measuring should have begun the copy");
    }
}
