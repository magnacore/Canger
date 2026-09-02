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
