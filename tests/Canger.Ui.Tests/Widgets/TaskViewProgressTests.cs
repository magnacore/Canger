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

    private static string Render(ICommandProgress? source)
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

        return string.Join("\n", Enumerable.Range(0, 4).Select(screen.TextAt));
    }

    [Fact]
    public void ShowsAPercentageWhenTheTotalIsKnown()
    {
        Assert.Contains("   25%  Compressing: out.tar.lz",
                        Render(new Reported(total: 1000, completed: 250)),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsTheBytesMovedWhenTheTotalIsNot()
    {
        // The archiver case. No bar, but not a bare spinner either.
        Assert.Contains(" 4.1 k  Compressing: out.tar.lz",
                        Render(new Reported(total: null, completed: 4096)),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsTheDescriptionOffTheEdgeWhenThereIsNoFigure()
    {
        // Reported: the description sat flush against the left edge. The field was left out
        // entirely when there was nothing to put in it, so a row stepped in and out as its first
        // checkpoint arrived and rows with and without a figure did not line up.
        string withFigure = Render(new Reported(total: 1000, completed: 250));
        string without = Render(null);

        int indented = withFigure.IndexOf("Compressing:", StringComparison.Ordinal);
        int plain = without.IndexOf("Compressing:", StringComparison.Ordinal);

        Assert.True(plain > 0, "the description is flush against the edge");
        Assert.Equal(indented % 60, plain % 60);
    }

    [Fact]
    public void ShowsNeitherBeforeTheCommandHasSaidAnything()
    {
        string rendered = Render(new Reported(total: 1000, completed: null));

        Assert.Contains("Compressing: out.tar.lz", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("%", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsNeitherForACommandWithNoProgressSourceAtAll()
    {
        // Every other queued command, which keeps the spinner it always had.
        string rendered = Render(null);

        Assert.Contains("Compressing: out.tar.lz", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("%", rendered, StringComparison.Ordinal);
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
        public string Description => "Compressing: out.tar.lz";

        public double? Progress => 1 - ((double)remaining / total);

        public TimeSpan Idle => TimeSpan.Zero;

        public bool IsSized => true;

        public long? TotalBytes => total;

        public long? RemainingBytes => remaining;

        public double? BytesPerSecond => rate;

        public string Subject => Description;

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
        Assert.Contains("left",
                        Render(new SteadyWork(120_000_000, 60_000_000, 1_000_000)),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void PutsItAfterTheNameRatherThanBeforeIt()
    {
        // So a description is not pushed about by a figure that comes and goes.
        string rendered = Render(new SteadyWork(120_000_000, 60_000_000, 1_000_000));

        Assert.True(rendered.IndexOf("left", StringComparison.Ordinal) >
                    rendered.IndexOf("out.tar.lz", StringComparison.Ordinal));
    }

    [Fact]
    public void SaysNothingWhenThereIsNoRateYet()
    {
        Assert.DoesNotContain("left",
                              Render(new SteadyWork(120_000_000, 60_000_000, 0)),
                              StringComparison.Ordinal);
    }
}
