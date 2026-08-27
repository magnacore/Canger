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
/// mean something from the start. These pin two things about a transfer driven directly, with
/// nothing having sized it in advance: the walk finishes before a byte is written, and copying
/// begins on the very next step.
///
/// The second of those was once the whole story, and it is why the queue-wide estimate needed
/// work before it could be built: a job awaiting its turn could not be asked how big it was
/// without also starting it. It can now — see <c>ISizedWork</c> and the queue's sizing channel —
/// and these two remain to say what happens when nothing has. The first is worth keeping
/// whatever else changes: a percentage against an unknown total is a lie.
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
