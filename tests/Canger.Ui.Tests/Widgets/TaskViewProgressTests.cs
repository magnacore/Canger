// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Processes;
using Canger.Core.Tasks;
using Canger.TestSupport;
using Canger.Tui.Rendering;
using Canger.Ui.Styling;
using Canger.Ui.Widgets;

namespace Canger.Ui.Tests.Widgets;

/// <summary>
/// What the task view shows for a queued command.
/// </summary>
/// <remarks>
/// A percentage where the work knows its total, the bytes moved where it does not, and nothing but
/// the description where it can say neither. The middle case is the one an archiver falls into:
/// the file can be watched growing, but how large it ends up depends on a compression ratio nobody
/// knows in advance, so a bar there would be a bar that lies.
///
/// The figures come from the task's own line rather than from a column the view adds. That is what
/// makes an archive and a copy read alike, and it is why the view no longer indents every row by
/// the width of a field only some rows could fill.
/// </remarks>
public class TaskViewProgressTests
{
    /// <summary>A source that simply reports what it is told to.</summary>
    private sealed class Reported(long? total, long? completed) : ICommandProgress
    {
        public long? Total => total;

        public long? Completed => completed;

        public void Update(string standardError)
        {
            // Nothing to observe; the figures are given.
        }
    }

    private static string[] Rows(ICommandProgress? source)
    {
        FakeFileManager.RecordingProcessRunner runner = new();
        runner.BackgroundResults["tar"] = new FakeFileManager.FakeBackgroundProcess
        {
            StepsBeforeExit = 100,
        };

        TaskQueue queue = new();
        CommandTask task = new(
            runner,
            new ProcessRequest("tar -cf out.tar.lz big/", default, "/w"),
            "Compressing: out.tar.lz",
            notify: null,
            finished: null,
            progress: source);

        queue.Add(task);

        ScreenBuffer screen = new(60, 4);
        TaskView view = new(new DefaultColorScheme(), queue) { IsVisible = true };
        view.Layout(new Rect(0, 0, 60, 4));
        view.Render(screen);

        return [.. Enumerable.Range(0, 4).Select(screen.TextAt)];
    }

    /// <summary>The row the one queued task was drawn on.</summary>
    private static string TaskRow(ICommandProgress? source) =>
        Rows(source).Single(row => row.Contains("Compressing", StringComparison.Ordinal));

    [Fact]
    public void ShowsAPercentageWhenTheTotalIsKnown()
    {
        string row = TaskRow(new Reported(total: 1000, completed: 250));

        Assert.Contains("Compressing: out.tar.lz:", row, StringComparison.Ordinal);
        Assert.Contains("25%", row, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsTheBytesMovedWhenTheTotalIsNot()
    {
        // The archiver case. No bar, but not a bare spinner either.
        string row = TaskRow(new Reported(total: null, completed: 4096));

        Assert.Contains("4.1 k", row, StringComparison.Ordinal);
        Assert.DoesNotContain("%", row, StringComparison.Ordinal);
    }

    [Fact]
    public void StartsEveryRowAtTheEdgeWhateverItHasToReport()
    {
        // Reported: "in the task view the task item is indented to the right". A field was
        // reserved at the head of every row for a figure, so a row began with the white space
        // where its percentage would go — and rows that never got one were indented for nothing.
        // The figures now sit in the task's own line, after the name, where a copy has always put
        // them.
        foreach (ICommandProgress? source in
                 new ICommandProgress?[] { new Reported(1000, 250), new Reported(null, 4096), null })
        {
            string row = TaskRow(source);

            Assert.StartsWith("Compressing:", row, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ShowsNeitherBeforeTheCommandHasSaidAnything()
    {
        string row = TaskRow(new Reported(total: 1000, completed: null));

        Assert.Contains("Compressing: out.tar.lz", row, StringComparison.Ordinal);
        Assert.DoesNotContain("%", row, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsNeitherForACommandWithNoProgressSourceAtAll()
    {
        // Every other queued command, which keeps the spinner it always had.
        string row = TaskRow(null);

        Assert.Contains("Compressing: out.tar.lz", row, StringComparison.Ordinal);
        Assert.DoesNotContain("%", row, StringComparison.Ordinal);
    }
}

/// <summary>
/// That the view adds no figures of its own to work that already reports some.
/// </summary>
public class TaskViewDuplicationTests
{
    /// <summary>Work whose line already carries its figures, as a copy's does.</summary>
    private sealed class AlreadyReports : ILoadable
    {
        public string Description => "copying big.mkv:  85%   1.2 G/1.4 G";

        public double? Progress => 0.85;

        public TimeSpan Idle => TimeSpan.Zero;

        public IEnumerator<Unit> Steps()
        {
            yield return Unit.Value;
        }

        public void Dispose()
        {
            // Nothing held.
        }
    }

    [Fact]
    public void DoesNotPrintAPercentageTheTaskHasAlreadyGiven()
    {
        // The view used to draw its own percentage at the head of the row, so a copy — whose line
        // has carried its figures all along — reported the same number twice.
        TaskQueue queue = new();
        queue.Add(new AlreadyReports());

        ScreenBuffer screen = new(60, 4);
        TaskView view = new(new DefaultColorScheme(), queue) { IsVisible = true };
        view.Layout(new Rect(0, 0, 60, 4));
        view.Render(screen);

        string row = Enumerable.Range(0, 4).Select(screen.TextAt)
                               .Single(r => r.Contains("copying", StringComparison.Ordinal));

        Assert.Equal(1, row.Count(c => c == '%'));
        Assert.StartsWith("copying big.mkv:", row, StringComparison.Ordinal);
    }
}

/// <summary>
/// How much longer a job has, shown beside it.
/// </summary>
/// <remarks>
/// Driven through a job with a fixed rate rather than a real command, because a rate measured from
/// the clock cannot be made to say the same thing twice.
/// </remarks>
public class TaskViewEstimateTests
{
    /// <summary>A job that reports a size and a rate and does nothing else.</summary>
    private sealed class SteadyWork(long total, long remaining, double rate) : ILoadable, ISizedWork
    {
        public string Description =>
            $"Compressing: out.tar.lz: {Canger.Core.Model.TransferFigures.Describe(Progress, total - remaining, total, BytesPerSecond, Estimate)}";

        /// <summary>How much longer, composed into the line as every sized job composes it.</summary>
        private TimeSpan? Estimate =>
            rate > 0 ? TimeSpan.FromSeconds(remaining / rate) : null;

        public double? Progress => 1 - ((double)remaining / total);

        public TimeSpan Idle => TimeSpan.Zero;

        public bool IsSized => true;

        public long? TotalBytes => total;

        public long? RemainingBytes => remaining;

        public double? BytesPerSecond => rate;

        public string Subject => "Compressing: out.tar.lz";

        public IEnumerator<Unit> SizingSteps() => Enumerable.Empty<Unit>().GetEnumerator();

        public IEnumerator<Unit> Steps()
        {
            yield return Unit.Value;
        }

        public void Dispose()
        {
            // Nothing held.
        }
    }

    private static string Render(ILoadable work)
    {
        TaskQueue queue = new();
        queue.Add(work);

        ScreenBuffer screen = new(70, 4);
        TaskView view = new(new DefaultColorScheme(), queue) { IsVisible = true };
        view.Layout(new Rect(0, 0, 70, 4));
        view.Render(screen);

        return string.Join("\n", Enumerable.Range(0, 4).Select(screen.TextAt));
    }

    [Fact]
    public void ShowsHowMuchLongerItHas()
    {
        // 60 MB left at 1 MB/s is a minute.
        Assert.Contains("ETA 01:00",
                        Render(new SteadyWork(120_000_000, 60_000_000, 1_000_000)),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void PutsItAfterTheNameRatherThanBeforeIt()
    {
        // So a description is not pushed about by a figure that comes and goes.
        string rendered = Render(new SteadyWork(120_000_000, 60_000_000, 1_000_000));

        Assert.True(rendered.IndexOf("ETA", StringComparison.Ordinal) >
                    rendered.IndexOf("out.tar.lz", StringComparison.Ordinal));
    }

    [Fact]
    public void SaysNothingWhenThereIsNoRateYet()
    {
        Assert.DoesNotContain("ETA",
                              Render(new SteadyWork(120_000_000, 60_000_000, 0)),
                              StringComparison.Ordinal);
    }
}
